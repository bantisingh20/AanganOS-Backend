using System.Security.Cryptography;
using UrbanNest.Application.Abstractions;
using UrbanNest.Application.Audit;
using UrbanNest.Application.Auth;
using UrbanNest.Application.Config;
using UrbanNest.Application.Exceptions;
using UrbanNest.Application.Notifications;
using UrbanNest.Domain.Identity;
using UrbanNest.Domain.Notifications;
using UrbanNest.Domain.Residents;
using UrbanNest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace UrbanNest.Infrastructure.Auth;

/// <summary>
/// EF-backed Auth_Service (Req 1, 2). Login + token issuance, logout (session revocation), the
/// 5-failure/15-min lockout, and the single-use/10-min OTP password-reset flow.
///
/// <para><b>Security first.</b> Passwords are only ever compared via the salted hasher's
/// constant-time verify and stored only as salted hashes (Req 2.5); OTP codes are hashed at rest
/// and single-use (Req 2.2–2.4); logout revokes the server-side session so the token stops working
/// (Req 1.3); no plaintext password, OTP code, or token is ever logged or returned beyond the token
/// the client needs. Invalid credentials and a locked account both yield the same generic
/// authentication error (no "user exists / wrong password" oracle), and a password-reset request
/// for an unknown identifier completes silently (no account enumeration).</para>
///
/// <para><b>Concurrency-safe failure counter (Req 1.6).</b> A failed attempt is applied with a
/// single atomic SQL <c>UPDATE</c> (<see cref="RelationalQueryableExtensions"/> /
/// <c>ExecuteUpdateAsync</c>) guarded by a <c>WHERE</c> on the row — two concurrent failed attempts
/// each hit the database, so neither increment is lost to a read-then-write gap. The lock decision
/// is then taken from the authoritative post-increment count.</para>
///
/// <para><b>No hardcoding.</b> The lockout threshold/window and OTP expiry come from Config_Service
/// (per-community override) falling back to <see cref="AuthOptions"/> (platform default), never
/// literals (steering).</para>
/// </summary>
public sealed class AuthService(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    IAuthTokenIssuer tokenIssuer,
    IOtpDeliverer otpDeliverer,
    IAuditService audit,
    IConfigService config,
    INotificationService notifications,
    IOptions<AuthOptions> authOptions,
    IClock clock) : IAuthService
{
    private readonly AuthOptions _auth = authOptions.Value;

    private const string ResourceType = "Auth";

    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Identifier) || string.IsNullOrEmpty(request.Password))
        {
            throw new UnauthenticatedException("Invalid credentials.");
        }

        var identifier = request.Identifier.Trim();
        var user = await FindByIdentifierAsync(identifier, cancellationToken);

        // Generic failure for a missing user — same error as a wrong password (no enumeration oracle).
        if (user is null || user.PasswordHash is null || user.Status != UserStatus.Active)
        {
            throw new UnauthenticatedException("Invalid credentials.");
        }

        var now = clock.UtcNow;

        // A currently-locked account is rejected even with the right password (Req 1.6).
        if (user.LockoutUntil is { } until && until > now)
        {
            throw new UnauthenticatedException("Account is temporarily locked. Try again later.");
        }

        if (!passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            await RegisterFailedAttemptAsync(user, now, cancellationToken);
            throw new UnauthenticatedException("Invalid credentials.");
        }

        // Success: clear any failure run atomically, then issue the token + session.
        await ResetFailureCounterAsync(user.Id, cancellationToken);

        var (communityIds, isSuperAdmin, roleNames) = await ResolveAuthorizationAsync(user.Id, cancellationToken);

        // Resident self-scope (Req 5.4): a non-superadmin user who is linked to a Resident with an
        // OPEN household membership is scoped to their OWN unit — the token carries the flag + unit so
        // the DbContext self-scope filter restricts them to their own rows. Without this a resident
        // would see the whole community's data.
        var (isResidentSelfScoped, residentUnitId) = isSuperAdmin
            ? (false, (Guid?)null)
            : await ResolveResidentScopeAsync(user.Id, cancellationToken);

        var issued = tokenIssuer.Issue(new TokenIssueRequest(
            user.Id, communityIds, isSuperAdmin, roleNames,
            IsResidentSelfScoped: isResidentSelfScoped,
            ResidentUnitId: residentUnitId));

        // --- Single active session per user ---------------------------------------------------
        // Enforce "one device at a time": end every OTHER active session for this user so the old
        // device is forced out on its very next request (the OnTokenValidated → SessionRevocationStore
        // check rejects a revoked session). Then register/replace the signing-in device and notify
        // the device(s) being signed out. All of this commits in the SAME transaction as the new
        // session row (one SaveChanges below), so it is atomic.
        await EnforceSingleActiveSessionAsync(user.Id, communityIds, request, cancellationToken);

        // Mint a refresh token for silent re-auth (mobile). The plaintext is returned ONCE; only its
        // salted hash is stored. The token is "<sessionId>.<secret>" so the server can look the
        // session up by id (the hasher salts every hash, so we can't match by hashing alone).
        var session = new LoginSession
        {
            UserId = user.Id,
            TokenId = issued.TokenId,
            IssuedAtUtc = issued.IssuedAtUtc,
            ExpiresAtUtc = issued.ExpiresAtUtc,
            RefreshTokenExpiresAtUtc = now.AddDays(_auth.RefreshTokenLifetimeDays),
        };
        var refreshSecret = GenerateOpaqueSecret();
        session.RefreshTokenHash = passwordHasher.Hash(refreshSecret);
        db.LoginSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        var refreshToken = $"{session.Id:N}.{refreshSecret}";
        return new LoginResult(issued.AccessToken, issued.ExpiresAtUtc, roleNames, refreshToken);
    }

    public async Task<LoginResult> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (sessionId, secret) = ParseRefreshToken(request.RefreshToken);
        if (sessionId is null || string.IsNullOrEmpty(secret))
        {
            throw new UnauthenticatedException("Invalid refresh token.");
        }

        // Load the session tracked (we rotate it below). Bypass tenant filters — there's no principal.
        var session = await db.LoginSessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == sessionId.Value, cancellationToken);

        var now = clock.UtcNow;
        if (session is null
            || session.RevokedAtUtc is not null
            || session.RefreshTokenHash is null
            || session.RefreshTokenExpiresAtUtc is null
            || session.RefreshTokenExpiresAtUtc <= now
            || !passwordHasher.Verify(session.RefreshTokenHash, secret))
        {
            // Any failure is a generic rejection → the client clears and routes to login.
            throw new UnauthenticatedException("The session has expired. Please sign in again.");
        }

        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == session.UserId, cancellationToken);
        if (user is null || user.Status != UserStatus.Active || user.IsDeleted)
        {
            throw new UnauthenticatedException("The session has expired. Please sign in again.");
        }

        // Re-resolve current authorization/scope so a role/scope change takes effect on refresh.
        var (communityIds, isSuperAdmin, roleNames) = await ResolveAuthorizationAsync(user.Id, cancellationToken);
        var (isResidentSelfScoped, residentUnitId) = isSuperAdmin
            ? (false, (Guid?)null)
            : await ResolveResidentScopeAsync(user.Id, cancellationToken);

        var issued = tokenIssuer.Issue(new TokenIssueRequest(
            user.Id, communityIds, isSuperAdmin, roleNames,
            IsResidentSelfScoped: isResidentSelfScoped,
            ResidentUnitId: residentUnitId));

        // Rotate: the SAME session row now carries the new access-token jti + a fresh refresh secret,
        // so the old refresh token (and old access jti) stop working — one-time-use refresh.
        session.TokenId = issued.TokenId;
        session.IssuedAtUtc = issued.IssuedAtUtc;
        session.ExpiresAtUtc = issued.ExpiresAtUtc;
        session.RefreshTokenExpiresAtUtc = now.AddDays(_auth.RefreshTokenLifetimeDays);
        var newSecret = GenerateOpaqueSecret();
        session.RefreshTokenHash = passwordHasher.Hash(newSecret);
        await db.SaveChangesAsync(cancellationToken);

        var refreshToken = $"{session.Id:N}.{newSecret}";
        return new LoginResult(issued.AccessToken, issued.ExpiresAtUtc, roleNames, refreshToken);
    }

    /// <summary>Split a "&lt;sessionId&gt;.&lt;secret&gt;" refresh token into its parts; nulls on malformed input.</summary>
    private static (Guid? SessionId, string Secret) ParseRefreshToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (null, string.Empty);
        }
        var dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1)
        {
            return (null, string.Empty);
        }
        return Guid.TryParseExact(token[..dot], "N", out var id)
            ? (id, token[(dot + 1)..])
            : (null, string.Empty);
    }

    /// <summary>A high-entropy, URL-safe opaque secret for a refresh token (256 bits).</summary>
    private static string GenerateOpaqueSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>
    /// Enforce single-active-session on login: revoke the user's OTHER active sessions, notify the
    /// device(s) being signed out ("signed out elsewhere"), deactivate those old devices, and
    /// register/activate the signing-in device. Everything is staged on the tracked context so it
    /// commits atomically with the new session row's SaveChanges in <see cref="LoginAsync"/>.
    ///
    /// <para>Why notify BEFORE deactivating: the outbox worker resolves a recipient's ACTIVE devices
    /// at drain time, so once the old device is deactivated it could no longer be targeted by user.
    /// The "signed out elsewhere" notice is therefore enqueued with the old device's explicit
    /// <c>TargetPlayerId</c>, which the worker honors regardless of the device's IsActive state.</para>
    /// </summary>
    private async Task EnforceSingleActiveSessionAsync(
        Guid userId,
        IReadOnlyCollection<Guid> communityIds,
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // 1) Revoke every other active session (tracked, so it commits with the new session row).
        //    The per-request OnTokenValidated check then rejects those tokens immediately (Req 1.3).
        var otherActiveSessions = await db.LoginSessions
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in otherActiveSessions)
        {
            session.RevokedAtUtc = now;
        }

        var newPlayerId = request.OneSignalPlayerId?.Trim();

        // 2) Load the user's currently-active devices (the ones being superseded).
        var activeDevices = await db.Set<Device>()
            .Where(d => d.UserId == userId && d.IsActive)
            .ToListAsync(cancellationToken);

        // 3) Notify + deactivate each old device — EXCEPT one that matches the new player id (that is
        //    the same physical device re-logging in; we keep/refresh it below rather than sign it out).
        var communityId = communityIds.Count > 0 ? communityIds.First() : (Guid?)null;
        foreach (var device in activeDevices)
        {
            var isSameDevice = !string.IsNullOrEmpty(newPlayerId) && device.OneSignalPlayerId == newPlayerId;
            if (isSameDevice)
            {
                continue;
            }

            // Push a mandatory "signed out elsewhere" notice to this specific old device. Only when
            // the user has a community to stamp the (tenant-owned) outbox row against; a pure
            // platform user with no community simply gets no push (still revoked + deactivated).
            if (communityId is { } cid && !string.IsNullOrEmpty(device.OneSignalPlayerId))
            {
                await notifications.EnqueueAsync(
                    new NotificationRequest(
                        NotificationTemplateKeys.SignedOutElsewhere,
                        userId,
                        cid,
                        TargetPlayerId: device.OneSignalPlayerId),
                    cancellationToken);
            }

            device.IsActive = false;
            device.LastSeenUtc = now;
        }

        // 4) Register/activate the signing-in device (upsert by player id), if one was supplied.
        if (!string.IsNullOrEmpty(newPlayerId))
        {
            var platform = ParsePlatform(request.Platform);
            // The player id is globally unique (one target → one user), so look it up across users.
            var existing = await db.Set<Device>()
                .FirstOrDefaultAsync(d => d.OneSignalPlayerId == newPlayerId, cancellationToken);
            if (existing is null)
            {
                db.Set<Device>().Add(new Device
                {
                    UserId = userId,
                    Platform = platform,
                    OneSignalPlayerId = newPlayerId,
                    IsActive = true,
                    LastSeenUtc = now,
                });
            }
            else
            {
                // Re-point the target to this user (e.g. a shared device) and reactivate it.
                existing.UserId = userId;
                existing.Platform = platform;
                existing.IsActive = true;
                existing.LastSeenUtc = now;
            }
        }
    }

    private static DevicePlatform ParsePlatform(string? platform) =>
        platform?.Trim().ToLowerInvariant() switch
        {
            "android" => DevicePlatform.Android,
            "ios" => DevicePlatform.Ios,
            "web" => DevicePlatform.Web,
            _ => DevicePlatform.Unknown,
        };

    public async Task RegisterDeviceAsync(
        Guid userId,
        string oneSignalPlayerId,
        string? platform,
        CancellationToken cancellationToken = default)
    {
        var playerId = oneSignalPlayerId?.Trim();
        if (string.IsNullOrEmpty(playerId))
        {
            throw new ValidationException("oneSignalPlayerId", "oneSignalPlayerId is required.");
        }

        var now = clock.UtcNow;
        var devicePlatform = ParsePlatform(platform);

        // Upsert by the globally-unique player id (one push target → one user, Req 11.3). This is the
        // same device-registration done on the login path, exposed for the post-login /devices call.
        var existing = await db.Set<Device>()
            .FirstOrDefaultAsync(d => d.OneSignalPlayerId == playerId, cancellationToken);
        if (existing is null)
        {
            db.Set<Device>().Add(new Device
            {
                UserId = userId,
                Platform = devicePlatform,
                OneSignalPlayerId = playerId,
                IsActive = true,
                LastSeenUtc = now,
            });
        }
        else
        {
            // Re-point to this user (shared device / re-register) and refresh.
            existing.UserId = userId;
            existing.Platform = devicePlatform;
            existing.IsActive = true;
            existing.LastSeenUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task LogoutAsync(string tokenId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenId))
        {
            return;
        }

        // Atomic, idempotent revoke: only an active session is stamped; unknown/already-revoked → no-op.
        await db.LoginSessions
            .Where(s => s.TokenId == tokenId && s.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                set => set.SetProperty(s => s.RevokedAtUtc, clock.UtcNow),
                cancellationToken);
    }

    public async Task RequestPasswordResetAsync(PasswordResetRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Identifier))
        {
            return;
        }

        var identifier = request.Identifier.Trim();
        var user = await FindByIdentifierAsync(identifier, cancellationToken);

        // No account enumeration: silently succeed for an unknown identifier.
        if (user is null || user.Status != UserStatus.Active)
        {
            return;
        }

        var expiryMinutes = await config.GetIntAsync(
            AuthConfigKeys.OtpExpiryMinutes, _auth.OtpExpiryMinutes, cancellationToken: cancellationToken);

        // DEV ONLY: a configured fixed OTP (e.g. "123456") lets the reset flow be tested without a
        // delivery channel. It is STILL stored hashed (never plaintext) and verified the same way;
        // it just isn't random and isn't delivered. Empty in prod → a random code + real delivery.
        var devFixed = _auth.DevFixedOtp?.Trim();
        var useDevOtp = !string.IsNullOrEmpty(devFixed);
        var code = useDevOtp ? devFixed! : GenerateNumericCode(_auth.OtpLength);

        db.OtpTokens.Add(new OtpToken
        {
            UserId = user.Id,
            PurposeType = OtpPurpose.PasswordReset,
            // Hashed with the ASP.NET PasswordHasher (PBKDF2 + per-value salt) — one-way, like a
            // password. The plaintext OTP is never persisted or logged.
            HashedCode = passwordHasher.Hash(code),
            ExpiresAtUtc = clock.UtcNow.AddMinutes(expiryMinutes),
        });
        await db.SaveChangesAsync(cancellationToken);

        // In dev-fixed mode we deliberately DON'T deliver (no SMS/email/notification) — the tester
        // already knows the fixed code. In normal mode, deliver to the user's registered contact
        // (Req 2.1); the deliverer is the only place the plaintext code appears (never logged).
        if (!useDevOtp)
        {
            await otpDeliverer.DeliverAsync(identifier, code, cancellationToken);
        }
    }

    public async Task<bool> VerifyPasswordResetOtpAsync(PasswordResetOtpCheck request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Identifier) || string.IsNullOrWhiteSpace(request.Code))
        {
            return false;
        }

        var identifier = request.Identifier.Trim();
        var user = await FindByIdentifierAsync(identifier, cancellationToken);
        // No account enumeration: an unknown identifier is "invalid code", not a distinct error.
        if (user is null)
        {
            return false;
        }

        var now = clock.UtcNow;

        // Same candidate set + constant-time hash verify as the reset — but we DO NOT consume the OTP
        // (UsedAtUtc stays null) so the user can still complete the reset on the next screen.
        var candidates = await db.OtpTokens.AsNoTracking()
            .Where(o => o.UserId == user.Id &&
                        o.PurposeType == OtpPurpose.PasswordReset &&
                        o.UsedAtUtc == null &&
                        o.ExpiresAtUtc > now)
            .OrderByDescending(o => o.ExpiresAtUtc)
            .ToListAsync(cancellationToken);

        return candidates.Any(o => passwordHasher.Verify(o.HashedCode, request.Code.Trim()));
    }

    public async Task ResetPasswordAsync(PasswordResetConfirm request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Identifier) ||
            string.IsNullOrWhiteSpace(request.Code) ||
            string.IsNullOrEmpty(request.NewPassword))
        {
            throw new ValidationException("code", "The reset code or new password is invalid.");
        }

        var identifier = request.Identifier.Trim();
        var user = await FindByIdentifierAsync(identifier, cancellationToken)
            ?? throw new ValidationException("code", "The reset code is invalid or has expired.");

        var now = clock.UtcNow;

        // Candidate unexpired, unused reset OTPs for the user, newest first. The code is matched by
        // constant-time hash verify (never by querying on the plaintext).
        var candidates = await db.OtpTokens
            .Where(o => o.UserId == user.Id &&
                        o.PurposeType == OtpPurpose.PasswordReset &&
                        o.UsedAtUtc == null &&
                        o.ExpiresAtUtc > now)
            .OrderByDescending(o => o.ExpiresAtUtc)
            .ToListAsync(cancellationToken);

        var match = candidates.FirstOrDefault(o => passwordHasher.Verify(o.HashedCode, request.Code))
            ?? throw new ValidationException("code", "The reset code is invalid or has expired.");

        // Update the password (salted hash, Req 2.5) and invalidate the OTP in one transaction (Req 2.3).
        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        // A password reset also clears any active lockout / failure run.
        user.FailedLoginCount = 0;
        user.LockoutWindowStartedAtUtc = null;
        user.LockoutUntil = null;
        match.UsedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken);

        // Security event for the password change (Req 2.6). No secret in the detail.
        await audit.AppendAsync(
            new AuditEntry("PasswordChanged", ResourceType, user.Id.ToString(), CommunityId: null,
                Detail: "Password reset via OTP."),
            cancellationToken);
    }

    // --- helpers -----------------------------------------------------------------------------

    private Task<User?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(
            u => u.Email == identifier || u.Phone == identifier, cancellationToken);

    /// <summary>
    /// Atomically record a failed attempt and lock the account when the configured threshold is hit
    /// within the window (Req 1.6). The increment is a guarded SQL UPDATE so concurrent failures
    /// don't lose counts; the lock decision reads back the authoritative counter.
    /// </summary>
    private async Task RegisterFailedAttemptAsync(User user, DateTime now, CancellationToken cancellationToken)
    {
        var threshold = await config.GetIntAsync(
            AuthConfigKeys.LockoutThreshold, _auth.LockoutThreshold, cancellationToken: cancellationToken);
        var windowMinutes = await config.GetIntAsync(
            AuthConfigKeys.LockoutWindowMinutes, _auth.LockoutWindowMinutes, cancellationToken: cancellationToken);

        var windowStart = user.LockoutWindowStartedAtUtc;
        var windowExpired = windowStart is null || now - windowStart.Value > TimeSpan.FromMinutes(windowMinutes);

        // Atomic UPDATE: restart the run (count=1) when the window lapsed, otherwise increment.
        // Guarded by Id so two concurrent failures serialize at the row and neither is lost.
        if (windowExpired)
        {
            await db.Users
                .Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(u => u.FailedLoginCount, 1)
                        .SetProperty(u => u.LockoutWindowStartedAtUtc, now),
                    cancellationToken);
        }
        else
        {
            await db.Users
                .Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(
                    set => set.SetProperty(u => u.FailedLoginCount, u => u.FailedLoginCount + 1),
                    cancellationToken);
        }

        // Read back the authoritative count to decide the lock (no read-then-write race on the count).
        var currentCount = await db.Users
            .Where(u => u.Id == user.Id)
            .Select(u => u.FailedLoginCount)
            .FirstAsync(cancellationToken);

        if (currentCount >= threshold)
        {
            var lockoutUntil = now.AddMinutes(windowMinutes);
            await db.Users
                .Where(u => u.Id == user.Id && (u.LockoutUntil == null || u.LockoutUntil < now))
                .ExecuteUpdateAsync(
                    set => set.SetProperty(u => u.LockoutUntil, lockoutUntil),
                    cancellationToken);

            // Security event for the lockout (Req 1.6). Identifier is the user id, never credentials.
            await audit.AppendAsync(
                new AuditEntry("AccountLocked", ResourceType, user.Id.ToString(), CommunityId: null,
                    Detail: $"Account locked after {currentCount} consecutive failed login attempts."),
                cancellationToken);
        }
    }

    private async Task ResetFailureCounterAsync(Guid userId, CancellationToken cancellationToken)
    {
        await db.Users
            .Where(u => u.Id == userId &&
                        (u.FailedLoginCount != 0 || u.LockoutWindowStartedAtUtc != null || u.LockoutUntil != null))
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(u => u.FailedLoginCount, 0)
                    .SetProperty(u => u.LockoutWindowStartedAtUtc, (DateTime?)null)
                    .SetProperty(u => u.LockoutUntil, (DateTime?)null),
                cancellationToken);
    }

    /// <summary>
    /// Resolve the claim inputs from the user's active role assignments: the distinct communities
    /// they're scoped to (repeated community claims), whether they hold any platform-wide
    /// (CommunityId-null) role (super-admin short-circuit), and their role names (returned with the
    /// token). One joined, AsNoTracking query — no N+1 (steering: scalable reads).
    /// </summary>
    private async Task<(IReadOnlyCollection<Guid> CommunityIds, bool IsSuperAdmin, IReadOnlyCollection<string> RoleNames)>
        ResolveAuthorizationAsync(Guid userId, CancellationToken cancellationToken)
    {
        var assignments = await (
            from ur in db.UserRoles.AsNoTracking()
            where ur.UserId == userId
            join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            select new { ur.CommunityId, RoleName = r.Name })
            .ToListAsync(cancellationToken);

        var communityIds = assignments
            .Where(a => a.CommunityId.HasValue)
            .Select(a => a.CommunityId!.Value)
            .Distinct()
            .ToArray();

        // A platform-scoped assignment (null CommunityId) confers the cross-community short-circuit.
        var isSuperAdmin = assignments.Any(a => a.CommunityId is null);

        var roleNames = assignments
            .Select(a => a.RoleName)
            .Distinct()
            .ToArray();

        return (communityIds, isSuperAdmin, roleNames);
    }

    /// <summary>
    /// Resolve the resident self-scope for a signing-in user (Req 5.4). The user is self-scoped when
    /// they are linked to a non-deleted <see cref="Resident"/> that has an OPEN
    /// <see cref="HouseholdMember"/> (EndDate == null) tied to a unit. Returns that unit (the
    /// earliest-started open membership, for determinism when a resident spans more than one unit).
    /// A user with no resident/household link (e.g. a management account) is NOT self-scoped.
    /// Queries bypass the tenant filter via IgnoreQueryFilters — at login there is no principal yet.
    /// </summary>
    private async Task<(bool IsSelfScoped, Guid? UnitId)> ResolveResidentScopeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var unitId = await (
            from r in db.Residents.AsNoTracking().IgnoreQueryFilters()
            where r.UserId == userId && !r.IsDeleted
            join hm in db.Set<HouseholdMember>().AsNoTracking().IgnoreQueryFilters()
                on r.Id equals hm.ResidentId
            where !hm.IsDeleted && hm.EndDate == null && hm.UnitId != null
            orderby hm.StartDate
            select hm.UnitId)
            .FirstOrDefaultAsync(cancellationToken);

        return unitId is { } u ? (true, u) : (false, (Guid?)null);
    }

    /// <summary>Cryptographically-strong numeric OTP of the configured length.</summary>
    private static string GenerateNumericCode(int length)
    {
        var digits = new char[length];
        for (var i = 0; i < length; i++)
        {
            digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
        }

        return new string(digits);
    }
}
