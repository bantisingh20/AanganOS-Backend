using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using App.Application.Abstractions;
using App.Application.Abstractions.Security;
using App.Application.Audit;
using App.Application.Auth;
using App.Application.Config;
using App.Application.Exceptions;
using App.Domain.Identity;
using App.Infrastructure.Audit;
using App.Infrastructure.Auth;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Interceptors;
using App.Infrastructure.Querying;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.IntegrationTests;

/// <summary>
/// Runnable check for Task 5.2 (Auth_Service) against a relational provider (SQLite), proving the
/// behaviors the task requires:
/// <list type="bullet">
///   <item>valid login issues a token with the Task 2.2 claim contract + creates a session (Req 1.1);</item>
///   <item>invalid credentials are rejected with no token (Req 1.2);</item>
///   <item>logout revokes the session so the token no longer authenticates (Req 1.3);</item>
///   <item>the Nth consecutive failure within the window locks the account for the window and
///   writes a security event (Req 1.6), and a further attempt while locked is rejected even with
///   the right password;</item>
///   <item>OTP reset: a valid unexpired OTP sets the new password and marks the OTP used (Req 2.3);
///   an expired OTP is rejected (Req 2.2/2.4); a reused OTP is rejected (Req 2.3/2.4);</item>
///   <item>passwords are stored only as a salted hash — verify round-trips, plaintext never present (Req 2.5);</item>
///   <item>a password change writes a security audit event (Req 2.6).</item>
/// </list>
/// </summary>
public sealed class AuthServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Guid _communityA = Guid.NewGuid();
    private readonly MutableClock _clock = new();
    private readonly IPasswordHasher _hasher = new PasswordHasherAdapter();
    private readonly CapturingOtpDeliverer _deliverer = new();
    private readonly PassthroughConfig _config = new();
    private readonly IAuthTokenIssuer _issuer;

    private static readonly AuthOptions Options = new()
    {
        LockoutThreshold = 5,
        LockoutWindowMinutes = 15,
        OtpExpiryMinutes = 10,
        OtpLength = 6,
        TokenLifetimeMinutes = 60,
    };

    // A 32+ char key so the issuer and the validation handler agree (same "Jwt" contract).
    private const string SigningKey = "unit-test-signing-key-should-be-long-enough-123456";
    private const string Issuer = "communityos-tests";
    private const string Audience = "communityos-clients";

    private sealed class MutableClock : IClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    }

    private sealed class CapturingOtpDeliverer : IOtpDeliverer
    {
        public string? LastDestination { get; private set; }
        public string? LastCode { get; private set; }
        public int DeliveryCount { get; private set; }

        public Task DeliverAsync(string destination, string code, CancellationToken cancellationToken = default)
        {
            LastDestination = destination;
            LastCode = code;
            DeliveryCount++;
            return Task.CompletedTask;
        }
    }

    // Config_Service stub: no overrides configured, so every getter returns the caller's default —
    // i.e. the thresholds fall back to AuthOptions exactly as production does when unset.
    private sealed class PassthroughConfig : IConfigService
    {
        public Task<string?> GetAsync(string key, Guid? communityId = null, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
        public Task<string> GetStringAsync(string key, string defaultValue, Guid? communityId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(defaultValue);
        public Task<int> GetIntAsync(string key, int defaultValue, Guid? communityId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(defaultValue);
        public Task<bool> GetBoolAsync(string key, bool defaultValue, Guid? communityId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(defaultValue);
        public Task<decimal> GetDecimalAsync(string key, decimal defaultValue, Guid? communityId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(defaultValue);
        public Task<TEnum> GetEnumAsync<TEnum>(string key, TEnum defaultValue, Guid? communityId = null, CancellationToken cancellationToken = default) where TEnum : struct, Enum
            => Task.FromResult(defaultValue);
        public Task SetAsync(string key, string value, Guid? communityId = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<IReadOnlyList<MasterDataEntry>> GetMasterDataAsync(string listKey, Guid? communityId = null, bool includeInactive = false, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MasterDataEntry>>([]);
        public Task SetMasterDataAsync(string listKey, MasterDataEntry entry, Guid? communityId = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<IReadOnlyList<ConfigRow>> ListConfigAsync(string? search = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ConfigRow>>([]);
        public Task<ConfigRow> UpsertConfigAsync(UpsertConfigRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ConfigRow(Guid.NewGuid(), request.Key, request.Scope, request.OrganizationId, request.CommunityId, request.Value));
        public Task DeleteConfigAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    public AuthServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _issuer = new JwtAuthTokenIssuer(
            Microsoft.Extensions.Options.Options.Create(new JwtIssuerOptions
            {
                Issuer = Issuer,
                Audience = Audience,
                SigningKey = SigningKey,
            }),
            Microsoft.Extensions.Options.Options.Create(Options),
            _clock);

        var seedTenant = new TestTenantContext { IsPlatformSuperAdmin = true, UserId = "seed" };
        using var ctx = NewContext(seedTenant);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewContext(ITenantContext tenant)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(tenant, _clock))
            .Options;
        return new AppDbContext(options, tenant);
    }

    private TestTenantContext System() =>
        new() { IsPlatformSuperAdmin = true, UserId = "system", AuthorizedCommunityIds = [_communityA] };

    private AuthService NewAuth(AppDbContext ctx, ITenantContext tenant)
    {
        var audit = new AuditService(ctx, tenant, _clock, new StubPageSizePolicy(), new EfAsyncQueryExecutor());
        return new AuthService(
            ctx, _hasher, _issuer, _deliverer, audit, _config, new NoOpNotificationService(),
            Microsoft.Extensions.Options.Options.Create(Options), _clock);
    }

    /// <summary>No-op notification service for the auth tests — single-active-session enqueues a
    /// "signed out elsewhere" notice, but delivery is out of scope here (the outbox is tested
    /// elsewhere), so this simply records nothing.</summary>
    private sealed class NoOpNotificationService : App.Application.Notifications.INotificationService
    {
        public Task<App.Application.Notifications.EnqueueOutcome> EnqueueAsync(
            App.Application.Notifications.NotificationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(App.Application.Notifications.EnqueueOutcome.Queued);
    }

    private sealed class StubPageSizePolicy : App.Application.Querying.IPageSizePolicy
    {
        public int DefaultPageSize => 50;
        public int MaxPageSize => 200;
    }

    /// <summary>Seed a user with a known password hash and optionally a community-scoped role.</summary>
    private async Task<User> SeedUserAsync(string email, string password, string? roleName = null, bool platformRole = false)
    {
        var tenant = System();
        await using var ctx = NewContext(tenant);

        var user = new User { Email = email, PasswordHash = _hasher.Hash(password), Status = UserStatus.Active };
        ctx.Users.Add(user);

        if (roleName is not null)
        {
            var role = new Role
            {
                Name = roleName,
                Scope = platformRole ? RoleScope.Platform : RoleScope.Community,
                CommunityId = platformRole ? null : _communityA,
            };
            ctx.Roles.Add(role);
            ctx.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id,
                CommunityId = platformRole ? null : _communityA,
            });
        }

        await ctx.SaveChangesAsync();
        return user;
    }

    private static JwtSecurityToken ReadToken(string accessToken) =>
        new JwtSecurityTokenHandler().ReadJwtToken(accessToken);

    [Fact]
    public async Task Valid_login_issues_token_with_claims_and_creates_session()
    {
        var user = await SeedUserAsync("alice@x.com", "Correct!123", "CommunityManager");

        var tenant = System();
        await using var ctx = NewContext(tenant);
        var result = await NewAuth(ctx, tenant).LoginAsync(new LoginRequest("alice@x.com", "Correct!123"));

        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.Contains("CommunityManager", result.Roles);

        var jwt = ReadToken(result.AccessToken);
        Assert.Equal(Issuer, jwt.Issuer);
        Assert.Equal(user.Id.ToString(), jwt.Claims.Single(c => c.Type == CommunityClaimTypes.UserId).Value);
        Assert.Equal(_communityA.ToString(), jwt.Claims.Single(c => c.Type == CommunityClaimTypes.CommunityId).Value);
        Assert.Equal("false", jwt.Claims.Single(c => c.Type == CommunityClaimTypes.IsPlatformSuperAdmin).Value);
        Assert.Contains(jwt.Claims, c => c.Type == ClaimTypes.Role && c.Value == "CommunityManager");
        var jti = jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Jti).Value;

        // The session was recorded (active) so the token can later be revoked (Req 1.1 / 1.3).
        var session = await ctx.LoginSessions.AsNoTracking().SingleAsync(s => s.TokenId == jti);
        Assert.Equal(user.Id, session.UserId);
        Assert.Null(session.RevokedAtUtc);
    }

    [Fact]
    public async Task Platform_role_user_gets_super_admin_claim()
    {
        await SeedUserAsync("root@x.com", "Correct!123", "PlatformAdmin", platformRole: true);

        var tenant = System();
        await using var ctx = NewContext(tenant);
        var result = await NewAuth(ctx, tenant).LoginAsync(new LoginRequest("root@x.com", "Correct!123"));

        var jwt = ReadToken(result.AccessToken);
        Assert.Equal("true", jwt.Claims.Single(c => c.Type == CommunityClaimTypes.IsPlatformSuperAdmin).Value);
    }

    [Fact]
    public async Task Invalid_credentials_are_rejected_with_no_token()
    {
        await SeedUserAsync("bob@x.com", "Correct!123");

        var tenant = System();
        await using var ctx = NewContext(tenant);

        await Assert.ThrowsAsync<UnauthenticatedException>(() =>
            NewAuth(ctx, tenant).LoginAsync(new LoginRequest("bob@x.com", "WrongPassword")));

        // No session was created for a failed login.
        Assert.Equal(0, await ctx.LoginSessions.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Logout_revokes_the_session_so_the_token_no_longer_authenticates()
    {
        await SeedUserAsync("carol@x.com", "Correct!123");

        var tenant = System();
        await using var ctx = NewContext(tenant);
        var auth = NewAuth(ctx, tenant);
        var result = await auth.LoginAsync(new LoginRequest("carol@x.com", "Correct!123"));
        var jti = ReadToken(result.AccessToken).Claims.Single(c => c.Type == JwtRegisteredClaimNames.Jti).Value;

        var store = new SessionRevocationStore(ctx);
        Assert.True(await store.IsSessionActiveAsync(jti)); // active before logout

        await auth.LogoutAsync(jti);

        Assert.False(await store.IsSessionActiveAsync(jti)); // revoked → bearer validation rejects it (Req 1.3)
    }

    [Fact]
    public async Task Fifth_consecutive_failure_locks_the_account_and_writes_a_security_event()
    {
        await SeedUserAsync("dave@x.com", "Correct!123");

        // 5 wrong-password attempts within the window.
        for (var i = 0; i < Options.LockoutThreshold; i++)
        {
            var t = System();
            await using var ctx = NewContext(t);
            await Assert.ThrowsAsync<UnauthenticatedException>(() =>
                NewAuth(ctx, t).LoginAsync(new LoginRequest("dave@x.com", "nope")));
            _clock.UtcNow = _clock.UtcNow.AddSeconds(1); // still well inside the 15-min window
        }

        var tenant = System();
        await using var verifyCtx = NewContext(tenant);
        var locked = await verifyCtx.Users.AsNoTracking().SingleAsync(u => u.Email == "dave@x.com");
        Assert.NotNull(locked.LockoutUntil);
        Assert.True(locked.LockoutUntil > _clock.UtcNow);

        // A security event was recorded (Req 1.6).
        var events = await verifyCtx.AuditLogs.AsNoTracking().ToListAsync();
        Assert.Contains(events, e => e is { Action: "AccountLocked", ResourceType: "Auth" });

        // A further attempt while locked is rejected even with the CORRECT password.
        await Assert.ThrowsAsync<UnauthenticatedException>(() =>
            NewAuth(verifyCtx, tenant).LoginAsync(new LoginRequest("dave@x.com", "Correct!123")));
    }

    [Fact]
    public async Task Successful_login_resets_the_failure_counter()
    {
        await SeedUserAsync("erin@x.com", "Correct!123");

        // A couple of failures (below threshold) then a success.
        for (var i = 0; i < 2; i++)
        {
            var t = System();
            await using var fctx = NewContext(t);
            await Assert.ThrowsAsync<UnauthenticatedException>(() =>
                NewAuth(fctx, t).LoginAsync(new LoginRequest("erin@x.com", "nope")));
        }

        var tenant = System();
        await using var ctx = NewContext(tenant);
        await NewAuth(ctx, tenant).LoginAsync(new LoginRequest("erin@x.com", "Correct!123"));

        var user = await ctx.Users.AsNoTracking().SingleAsync(u => u.Email == "erin@x.com");
        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.LockoutUntil);
    }

    [Fact]
    public async Task Otp_reset_sets_new_password_and_marks_the_otp_used()
    {
        await SeedUserAsync("frank@x.com", "OldPass!123");

        var tenant = System();
        await using var ctx = NewContext(tenant);
        var auth = NewAuth(ctx, tenant);

        await auth.RequestPasswordResetAsync(new PasswordResetRequest("frank@x.com"));
        Assert.Equal(1, _deliverer.DeliveryCount);
        var code = _deliverer.LastCode!;

        await auth.ResetPasswordAsync(new PasswordResetConfirm("frank@x.com", code, "NewPass!456"));

        // The OTP is now used (single-use, Req 2.3).
        var otp = await ctx.OtpTokens.AsNoTracking().SingleAsync(o => o.UserId != Guid.Empty);
        Assert.NotNull(otp.UsedAtUtc);

        // The new password works, the old one does not.
        await using var ctx2 = NewContext(System());
        var ok = await NewAuth(ctx2, System()).LoginAsync(new LoginRequest("frank@x.com", "NewPass!456"));
        Assert.False(string.IsNullOrWhiteSpace(ok.AccessToken));

        await using var ctx3 = NewContext(System());
        await Assert.ThrowsAsync<UnauthenticatedException>(() =>
            NewAuth(ctx3, System()).LoginAsync(new LoginRequest("frank@x.com", "OldPass!123")));
    }

    [Fact]
    public async Task Expired_otp_is_rejected()
    {
        await SeedUserAsync("gina@x.com", "OldPass!123");

        var tenant = System();
        await using var ctx = NewContext(tenant);
        var auth = NewAuth(ctx, tenant);

        await auth.RequestPasswordResetAsync(new PasswordResetRequest("gina@x.com"));
        var code = _deliverer.LastCode!;

        // Advance past the 10-minute expiry window (Req 2.2).
        _clock.UtcNow = _clock.UtcNow.AddMinutes(Options.OtpExpiryMinutes + 1);

        await Assert.ThrowsAsync<ValidationException>(() =>
            auth.ResetPasswordAsync(new PasswordResetConfirm("gina@x.com", code, "NewPass!456")));
    }

    [Fact]
    public async Task Reused_otp_is_rejected()
    {
        await SeedUserAsync("hank@x.com", "OldPass!123");

        var tenant = System();
        await using var ctx = NewContext(tenant);
        var auth = NewAuth(ctx, tenant);

        await auth.RequestPasswordResetAsync(new PasswordResetRequest("hank@x.com"));
        var code = _deliverer.LastCode!;

        await auth.ResetPasswordAsync(new PasswordResetConfirm("hank@x.com", code, "NewPass!456"));

        // Submitting the same code again is rejected (already used, Req 2.3/2.4).
        await using var ctx2 = NewContext(System());
        await Assert.ThrowsAsync<ValidationException>(() =>
            NewAuth(ctx2, System()).ResetPasswordAsync(new PasswordResetConfirm("hank@x.com", code, "Another!789")));
    }

    [Fact]
    public async Task Password_is_stored_only_as_a_salted_hash()
    {
        await SeedUserAsync("ivy@x.com", "Secret!123");

        await using var ctx = NewContext(System());
        var user = await ctx.Users.AsNoTracking().SingleAsync(u => u.Email == "ivy@x.com");

        // Plaintext is never present; the stored value is a salted hash that verifies and round-trips.
        Assert.NotNull(user.PasswordHash);
        Assert.DoesNotContain("Secret!123", user.PasswordHash);
        Assert.True(_hasher.Verify(user.PasswordHash!, "Secret!123"));
        Assert.False(_hasher.Verify(user.PasswordHash!, "Secret!124"));

        // Hashing is salted: two hashes of the same input differ.
        Assert.NotEqual(_hasher.Hash("same"), _hasher.Hash("same"));
    }

    [Fact]
    public async Task Password_change_writes_a_security_audit_event()
    {
        await SeedUserAsync("jack@x.com", "OldPass!123");

        var tenant = System();
        await using var ctx = NewContext(tenant);
        var auth = NewAuth(ctx, tenant);

        await auth.RequestPasswordResetAsync(new PasswordResetRequest("jack@x.com"));
        await auth.ResetPasswordAsync(new PasswordResetConfirm("jack@x.com", _deliverer.LastCode!, "NewPass!456"));

        var events = await ctx.AuditLogs.AsNoTracking().ToListAsync();
        Assert.Contains(events, e => e is { Action: "PasswordChanged", ResourceType: "Auth" });
        // The audit detail never contains a credential.
        Assert.All(events, e => Assert.DoesNotContain("NewPass!456", e.Detail ?? string.Empty));
    }
}
