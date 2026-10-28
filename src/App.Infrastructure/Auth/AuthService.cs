using System.Security.Cryptography;
using System.Globalization;
using System.Security.Cryptography;
using App.Application.Abstractions;
using App.Application.Audit;
using App.Application.Auth;
using App.Application.Config;
using App.Application.Exceptions;
using App.Application.Notifications;
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

        return await IssueSessionCoreAsync(user.Id, request, cancellationToken);
    }

    public Task<LoginResult> IssueSessionForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        IssueSessionCoreAsync(userId, new LoginRequest(string.Empty, string.Empty), cancellationToken);

    private async Task<LoginResult> IssueSessionCoreAsync(Guid userId, LoginRequest request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var (communityIds, isSuperAdmin, roleNames, permissions) = await ResolveAuthorizationAsync(userId, cancellationToken);

        var (isResidentSelfScoped, residentUnitId) = isSuperAdmin
            ? (false, (Guid?)null)
            : await ResolveResidentScopeAsync(userId, cancellationToken);

        var issued = tokenIssuer.Issue(new TokenIssueRequest(
            userId, communityIds, isSuperAdmin, roleNames,
            Permissions: permissions,
            IsResidentSelfScoped: isResidentSelfScoped,
            ResidentUnitId: residentUnitId));

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
        var userDto = await BuildLoginUserAsync(userId, communityIds, roleNames, permissions, cancellationToken);
        return new LoginResult(issued.AccessToken, issued.ExpiresAtUtc, roleNames, refreshToken, userDto);
    }

    private async Task<LoginUserDto> BuildLoginUserAsync(
        Guid userId,
        IReadOnlyCollection<Guid> communityIds,
        IReadOnlyCollection<string> roleNames,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Name, u.Email, u.Phone })
            .FirstOrDefaultAsync(cancellationToken);

        var resident = await db.Residents.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.UserId == userId && !r.IsDeleted)
            .Select(r => new { r.Id, r.Name })
            .FirstOrDefaultAsync(cancellationToken);

        var name = !string.IsNullOrWhiteSpace(resident?.Name) ? resident!.Name.Trim()
            : !string.IsNullOrWhiteSpace(user?.Name) ? user!.Name!.Trim()
            : null;

        Guid? communityId = communityIds.Count > 0 ? communityIds.First() : null;
        string? communityName = null;
        Guid? organizationId = null;
        string? organizationName = null;
        if (communityId is { } cid)
        {
            var community = await db.Communities.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.Id == cid)
                .Select(c => new { c.Name, c.OrganizationId })
                .FirstOrDefaultAsync(cancellationToken);
            if (community is not null)
            {
                communityName = community.Name;
                organizationId = community.OrganizationId;
                organizationName = await db.Organizations.IgnoreQueryFilters().AsNoTracking()
                    .Where(o => o.Id == community.OrganizationId)
                    .Select(o => o.Name)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        return new LoginUserDto(
            userId,
            name,
            string.IsNullOrWhiteSpace(user?.Email) ? null : user!.Email,
            string.IsNullOrWhiteSpace(user?.Phone) ? null : user!.Phone,
            resident?.Id,
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
        var (communityIds, isSuperAdmin, roleNames, permissions) = await ResolveAuthorizationAsync(user.Id, cancellationToken);
        var (isResidentSelfScoped, residentUnitId) = isSuperAdmin
            ? (false, (Guid?)null)
            : await ResolveResidentScopeAsync(user.Id, cancellationToken);

        var issued = tokenIssuer.Issue(new TokenIssueRequest(
            user.Id, communityIds, isSuperAdmin, roleNames,
            Permissions: permissions,
            IsResidentSelfScoped: isResidentSelfScoped,
            ResidentUnitId: residentUnitId));

        session.TokenId = issued.TokenId;
        session.IssuedAtUtc = issued.IssuedAtUtc;
        session.ExpiresAtUtc = issued.ExpiresAtUtc;
        session.RefreshTokenExpiresAtUtc = now.AddDays(_auth.RefreshTokenLifetimeDays);
        var newSecret = GenerateOpaqueSecret();
        session.RefreshTokenHash = passwordHasher.Hash(newSecret);
        await db.SaveChangesAsync(cancellationToken);

        var refreshToken = $"{session.Id:N}.{newSecret}";
        var userDto = await BuildLoginUserAsync(user.Id, communityIds, roleNames, permissions, cancellationToken);
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

    private static string GenerateNumericCode(int length)
    {
        var digits = length > 0 ? length : 6;
        var max = (int)Math.Pow(10, digits);
        var value = RandomNumberGenerator.GetInt32(0, max);
        return value.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
    }


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

        var activeDevices = await db.Set<Device>()
            .Where(d => d.UserId == userId && d.IsActive)
            .ToListAsync(cancellationToken);

        var communityId = communityIds.Count > 0 ? communityIds.First() : (Guid?)null;
        foreach (var device in activeDevices)
        {
            var isSameDevice = !string.IsNullOrEmpty(newPlayerId) && device.OneSignalPlayerId == newPlayerId;
            if (isSameDevice)
            {
                continue;
            }

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

        if (!string.IsNullOrEmpty(newPlayerId))
        {
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

        var devFixed = _auth.DevFixedOtp?.Trim();
        var useDevOtp = !string.IsNullOrEmpty(devFixed);
        var code = useDevOtp ? devFixed! : GenerateNumericCode(_auth.OtpLength);

        db.OtpTokens.Add(new OtpToken
        {
            UserId = user.Id,
            PurposeType = OtpPurpose.PasswordReset,
            HashedCode = passwordHasher.Hash(code),
            ExpiresAtUtc = clock.UtcNow.AddMinutes(expiryMinutes),
        });
        await db.SaveChangesAsync(cancellationToken);

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

    private async Task<(IReadOnlyCollection<Guid> CommunityIds, bool IsSuperAdmin, IReadOnlyCollection<string> RoleNames, IReadOnlyCollection<string> Permissions)>
        ResolveAuthorizationAsync(Guid userId, CancellationToken cancellationToken)
    {
        var assignments = await (
            from ur in db.UserRoles.AsNoTracking()
            where ur.UserId == userId
            join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            select new { ur.CommunityId, ur.RoleId, RoleName = r.Name })
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

        var roleIds = assignments.Select(a => a.RoleId).Distinct().ToArray();
        var permissions = await (
            from rp in db.Set<RolePermission>().AsNoTracking()
            where roleIds.Contains(rp.RoleId)
            join p in db.Set<Permission>().AsNoTracking() on rp.PermissionId equals p.Id
            select new { p.ResourceType, p.Action })
            .Distinct()
            .ToListAsync(cancellationToken);

        var permissionCodes = permissions
            .Select(p => $"{p.ResourceType}:{(int)p.Action}")
            .Distinct()
            .ToArray();

        return (communityIds, isSuperAdmin, roleNames, permissionCodes);
    }
}
