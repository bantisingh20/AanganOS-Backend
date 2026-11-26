using System.Security.Cryptography;
using System.Globalization;
using System.Security.Cryptography;
using App.Application.Abstractions;
using App.Application.Abstractions.Security;
using App.Application.Audit;
using App.Application.Auth;
using App.Application.Config;
using App.Application.Exceptions;
using App.Application.Notifications;
using App.Application.Subscription;
using App.Domain.Identity;
using App.Domain.Notifications;
using App.Domain.Residents;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Auth;


public sealed class AuthService(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    JwtTokenIssuer tokenIssuer,
    IOtpDeliverer otpDeliverer,
    IOtpGenerator otpGenerator,
    IAuditService audit,
    IConfigService config,
    INotificationService notifications,
    ISubscriptionGate subscriptionGate,
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

        return await IssueSessionCoreAsync(user.Id, request, cancellationToken);
    }

    public Task<LoginResult> IssueSessionForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        IssueSessionCoreAsync(userId, new LoginRequest(string.Empty, string.Empty), cancellationToken);

    private async Task<LoginResult> IssueSessionCoreAsync(Guid userId, LoginRequest request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var (user, communityIds, isSuperAdmin, roleNames, permissions) = await ResolveAuthorizationAsync(userId, cancellationToken);

        var (residentId, residentName, residentType, residentUnitId) = isSuperAdmin
            ? ((Guid?)null, (string?)null, (string?)null, (Guid?)null)
            : await ResolveResidentInfoAsync(userId, cancellationToken);

        var isResidentSelfScoped = residentUnitId.HasValue;

        var organizationId = user.OrganizationId is { } uo && uo != Guid.Empty
            ? uo
            : await ResolveOrganizationIdAsync(userId, communityIds, cancellationToken);

        await EnforceOrganizationActiveAsync(isSuperAdmin, organizationId, cancellationToken);

        await EnforceSubscriptionGateAsync(isSuperAdmin, roleNames, organizationId, cancellationToken);

        var issued = tokenIssuer.Issue(new TokenIssueRequest(
            userId, communityIds.Count > 0 ? communityIds.First() : null, isSuperAdmin, roleNames,
            Permissions: permissions,
            IsResidentSelfScoped: isResidentSelfScoped,
            ResidentUnitId: residentUnitId,
            OrganizationId: organizationId,
            ResidentId: residentId,
            ResidentType: residentType));

        await EnforceSingleActiveSessionAsync(userId, communityIds, request, cancellationToken);

        var session = new LoginSession
        {
            UserId = userId,
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
        var userDto = await BuildLoginUserAsync(user, residentId, residentName, communityIds, organizationId, roleNames, permissions, cancellationToken);
        return new LoginResult(issued.AccessToken, issued.ExpiresAtUtc, roleNames, refreshToken, userDto);
    }

    private async Task<LoginUserDto> BuildLoginUserAsync(
        User user,
        Guid? residentId,
        string? residentName,
        IReadOnlyCollection<Guid> communityIds,
        Guid? organizationId,
        IReadOnlyCollection<string> roleNames,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        var name = !string.IsNullOrWhiteSpace(residentName) ? residentName.Trim()
            : !string.IsNullOrWhiteSpace(user?.Name) ? user.Name.Trim()
            : null;

        Guid? communityId = communityIds.Count > 0 ? communityIds.First() : null;
        string? communityName = null;
        string? organizationName = null;

        if (communityId is { } cid)
        {
            var commInfo = await (
                from c in db.Communities.IgnoreQueryFilters().AsNoTracking()
                where c.Id == cid
                join o in db.Organizations.IgnoreQueryFilters().AsNoTracking() on c.OrganizationId equals o.Id into og
                from o in og.DefaultIfEmpty()
                select new { CommunityName = c.Name, OrganizationId = c.OrganizationId, OrganizationName = o.Name }
            ).FirstOrDefaultAsync(cancellationToken);

            if (commInfo != null)
            {
                communityName = commInfo.CommunityName;
                organizationId ??= commInfo.OrganizationId;
                organizationName = commInfo.OrganizationName;
            }
        }
        else if (organizationId is { } oid && oid != Guid.Empty)
        {
            organizationName = await db.Organizations.IgnoreQueryFilters().AsNoTracking()
                .Where(o => o.Id == oid)
                .Select(o => o.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new LoginUserDto(
            user.Id,
            name,
            string.IsNullOrWhiteSpace(user.Email) ? null : user.Email,
            string.IsNullOrWhiteSpace(user.Phone) ? null : user.Phone,
            residentId,
            communityId,
            communityName,
            organizationId,
            organizationName,
            roleNames,
            permissions);
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
        var (_, communityIds, isSuperAdmin, roleNames, permissions) = await ResolveAuthorizationAsync(user.Id, cancellationToken);
        var (residentId, residentName, residentType, residentUnitId) = isSuperAdmin
            ? ((Guid?)null, (string?)null, (string?)null, (Guid?)null)
            : await ResolveResidentInfoAsync(user.Id, cancellationToken);

        var isResidentSelfScoped = residentUnitId.HasValue;

        var organizationId = user.OrganizationId is { } uo && uo != Guid.Empty
            ? uo
            : await ResolveOrganizationIdAsync(user.Id, communityIds, cancellationToken);

        await EnforceSubscriptionGateAsync(isSuperAdmin, roleNames, organizationId, cancellationToken);

        var issued = tokenIssuer.Issue(new TokenIssueRequest(
            user.Id, communityIds.Count > 0 ? communityIds.First() : null, isSuperAdmin, roleNames,
            Permissions: permissions,
            IsResidentSelfScoped: isResidentSelfScoped,
            ResidentUnitId: residentUnitId,
            OrganizationId: organizationId,
            ResidentId: residentId,
            ResidentType: residentType));

        session.TokenId = issued.TokenId;
        session.IssuedAtUtc = issued.IssuedAtUtc;
        session.ExpiresAtUtc = issued.ExpiresAtUtc;
        session.RefreshTokenExpiresAtUtc = now.AddDays(_auth.RefreshTokenLifetimeDays);
        var newSecret = GenerateOpaqueSecret();
        session.RefreshTokenHash = passwordHasher.Hash(newSecret);
        await db.SaveChangesAsync(cancellationToken);

        var refreshToken = $"{session.Id:N}.{newSecret}";
        var userDto = await BuildLoginUserAsync(user, residentId, residentName, communityIds, organizationId, roleNames, permissions, cancellationToken);
        return new LoginResult(issued.AccessToken, issued.ExpiresAtUtc, roleNames, refreshToken, userDto);
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




    private async Task EnforceSingleActiveSessionAsync(
        Guid userId,
        IReadOnlyCollection<Guid> communityIds,
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var otherActiveSessions = await db.LoginSessions
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in otherActiveSessions)
        {
            session.RevokedAtUtc = now;
        }

        var newPlayerId = request.OneSignalPlayerId?.Trim();

        // The app did not say which device this is (it registers it via POST /devices right after
        // login). We can't tell "same phone" from "other phone" yet, so don't push "signed out" now -
        // RegisterDeviceAsync does it once the device is known, skipping this same device.
        if (string.IsNullOrEmpty(newPlayerId))
        {
            return;
        }

        var communityId = communityIds.Count > 0 ? communityIds.First() : (Guid?)null;
        await SignOutOtherDevicesAsync(userId, newPlayerId, communityId, now, cancellationToken);

        // Save / reactivate this device as the user's active one.
        var platform = ParsePlatform(request.Platform);
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

    /// <summary>
    /// Single active device: deactivate every other active device of the user and push them
    /// "you've been signed out". The current device (<paramref name="currentPlayerId"/>) is skipped,
    /// so logging in again on the same phone never shows that notification.
    /// </summary>
    private async Task SignOutOtherDevicesAsync(
        Guid userId, string currentPlayerId, Guid? communityId, DateTime now, CancellationToken cancellationToken)
    {
        var otherDevices = await db.Set<Device>()
            .Where(d => d.UserId == userId && d.IsActive && d.OneSignalPlayerId != currentPlayerId)
            .ToListAsync(cancellationToken);

        foreach (var device in otherDevices)
        {
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

        // Now the device is known: push "signed out" only to the user's OTHER active devices.
        var communityId = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.CommunityId)
            .FirstOrDefaultAsync(cancellationToken);
        await SignOutOtherDevicesAsync(userId, playerId, communityId, now, cancellationToken);

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

        var otp = otpGenerator.Generate(_auth.OtpLength);

        db.OtpTokens.Add(new OtpToken
        {
            UserId = user.Id,
            PurposeType = OtpPurpose.PasswordReset,
            HashedCode = passwordHasher.Hash(otp.Code),
            ExpiresAtUtc = clock.UtcNow.AddMinutes(expiryMinutes),
        });
        await db.SaveChangesAsync(cancellationToken);

        if (!otp.IsDefault)
        {
            await otpDeliverer.DeliverAsync(identifier, otp.Code, cancellationToken);
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

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword))
        {
            throw new ValidationException("currentPassword", "The current and new password are required.");
        }
        if (request.NewPassword.Length < 6)
        {
            throw new ValidationException("newPassword", "The new password must be at least 6 characters.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new UnauthenticatedException("Invalid credentials.");

        if (user.PasswordHash is null || !passwordHasher.Verify(user.PasswordHash, request.CurrentPassword))
        {
            throw new ValidationException("currentPassword", "The current password is incorrect.");
        }

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        user.FailedLoginCount = 0;
        user.LockoutWindowStartedAtUtc = null;
        user.LockoutUntil = null;

        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("PasswordChanged", ResourceType, user.Id.ToString(), CommunityId: null,
                Detail: "Password changed by the user."),
            cancellationToken);
    }

    // --- helpers -----------------------------------------------------------------------------

    private Task<User?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(
            u => u.Email == identifier || u.Phone == identifier, cancellationToken);


    private async Task RegisterFailedAttemptAsync(User user, DateTime now, CancellationToken cancellationToken)
    {
        var threshold = await config.GetIntAsync(
            AuthConfigKeys.LockoutThreshold, _auth.LockoutThreshold, cancellationToken: cancellationToken);
        var windowMinutes = await config.GetIntAsync(
            AuthConfigKeys.LockoutWindowMinutes, _auth.LockoutWindowMinutes, cancellationToken: cancellationToken);

        var windowStart = user.LockoutWindowStartedAtUtc;
        var windowExpired = windowStart is null || now - windowStart.Value > TimeSpan.FromMinutes(windowMinutes);

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


    private async Task<(Guid? ResidentId, string? ResidentName, string? ResidentType, Guid? UnitId)>
        ResolveResidentInfoAsync(Guid userId, CancellationToken cancellationToken)
    {
        var resident = await db.Residents.AsNoTracking().IgnoreQueryFilters()
            .Where(r => r.UserId == userId && !r.IsDeleted)
            .OrderBy(r => r.CreatedAtUtc)
            .Select(r => new { r.Id, r.Name, r.ResidentType })
            .FirstOrDefaultAsync(cancellationToken);

        if (resident is null) return (null, null, null, null);

        var unitId = await db.Set<HouseholdMember>().AsNoTracking().IgnoreQueryFilters()
            .Where(hm => hm.ResidentId == resident.Id && !hm.IsDeleted && hm.EndDate == null && hm.UnitId != null)
            .OrderBy(hm => hm.StartDate)
            .Select(hm => hm.UnitId)
            .FirstOrDefaultAsync(cancellationToken);

        return (resident.Id, resident.Name, resident.ResidentType, unitId);
    }

    private async Task EnforceOrganizationActiveAsync(
        bool isSuperAdmin,
        Guid? organizationId,
        CancellationToken cancellationToken)
    {
        if (isSuperAdmin || organizationId is not { } orgId)
        {
            return;
        }

        var org = await db.Organizations.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.Id == orgId)
            .Select(o => new { o.IsDeleted, o.IsActive })
            .FirstOrDefaultAsync(cancellationToken);

        if (org is null || org.IsDeleted || !org.IsActive)
        {
            throw new ForbiddenException("Your society/organization is currently inactive or suspended. Please contact your administrator.");
        }
    }

    private async Task EnforceSubscriptionGateAsync(
        bool isSuperAdmin,
        IReadOnlyCollection<string> roleNames,
        Guid? organizationId,
        CancellationToken cancellationToken)
    {
        if (isSuperAdmin || HasAdminRole(roleNames) || organizationId is not { } orgId)
        {
            return;
        }

        if (await subscriptionGate.IsExpiredForOrganizationAsync(orgId, cancellationToken))
        {
            throw new SubscriptionExpiredException();
        }
    }

    private static bool HasAdminRole(IReadOnlyCollection<string> roleNames) =>
        roleNames.Any(name =>
            string.Equals(name, "PowerUser", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Admin", StringComparison.OrdinalIgnoreCase));

    private async Task<Guid?> ResolveOrganizationIdAsync(Guid userId, IReadOnlyCollection<Guid> communityIds, CancellationToken cancellationToken)
    {
        // The user's own organization first (an org admin may have no community row yet) ...
        var userOrg = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.OrganizationId)
            .FirstOrDefaultAsync(cancellationToken);
        if (userOrg is { } org && org != Guid.Empty)
        {
            return org;
        }

        // ... else the organization of their default community.
        if (communityIds.Count == 0)
        {
            return null;
        }

        var first = communityIds.First();
        return await db.Communities.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Id == first)
            .Select(c => (Guid?)c.OrganizationId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<(User User, IReadOnlyCollection<Guid> CommunityIds, bool IsSuperAdmin, IReadOnlyCollection<string> RoleNames, IReadOnlyCollection<string> Permissions)>
        ResolveAuthorizationAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null || !user.RoleId.HasValue || user.Role is null)
        {
            throw new UnauthenticatedException("Invalid credentials.");
        }

        var communityList = new List<Guid>();
        if (user.CommunityId.HasValue)
        {
            communityList.Add(user.CommunityId.Value);
        }

        // ONE default community per user (Users.CommunityId). An organization admin's access to the
        // other communities of the organization is decided by role on every request (AuthSetup),
        // not stored in the token or the DB. If an org admin has no default yet, use the org's first.
        var isOrgAdmin = string.Equals(user.Role.Name, CurrentUser.OrganizationAdminRole, StringComparison.OrdinalIgnoreCase);
        if (isOrgAdmin && communityList.Count == 0 && user.OrganizationId is { } orgId && orgId != Guid.Empty)
        {
            var firstCommunity = await db.Communities.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.OrganizationId == orgId && !c.IsDeleted)
                .OrderBy(c => c.CreatedAtUtc)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (firstCommunity is { } fc)
            {
                communityList.Add(fc);
            }
        }

        var communityIds = communityList.ToArray();
        var isSuperAdmin = string.Equals(user.Role.Name, "Superadmin", StringComparison.OrdinalIgnoreCase)
            || (user.CommunityId is null && !isOrgAdmin);

        var roleNames = new[] { user.Role.Name };
        var roleId = user.RoleId.Value;

        var permissions = await (
            from rp in db.Set<RolePermission>().AsNoTracking()
            where rp.RoleId == roleId
            join p in db.Set<Permission>().AsNoTracking() on rp.PermissionId equals p.Id
            select new { p.ResourceType, p.Action })
            .Distinct()
            .ToListAsync(cancellationToken);

        var permissionCodes = permissions
            .Select(p => $"{p.ResourceType}:{(int)p.Action}")
            .Distinct()
            .ToArray();

        return (user, communityIds, isSuperAdmin, roleNames, permissionCodes);
    }
}


