using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using App.Application.Abstractions.Security;
using App.Domain.Identity;
using App.Domain.Tenancy;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace App.IntegrationTests;

/// <summary>
/// Boots the real API host (full Task 2.2 pipeline) over a shared in-memory SQLite database so the
/// Phase 1.1 EF global tenant filter runs in generated SQL. The JWT bearer scheme keeps its real
/// config-driven validation — tests mint tokens signed with the <see cref="SigningKey"/> below, so
/// authentication + principal construction + the claim → tenant-context flow are all exercised for
/// real, not stubbed.
/// </summary>
public sealed class PipelineWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "communityos-test";
    public const string Audience = "communityos-test-clients";
    public const string SigningKey = "integration-test-signing-key-must-be-32+chars-long";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public PipelineWebApplicationFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:SigningKey"] = SigningKey,
                ["Jwt:ClockSkewSeconds"] = "30",
                // Keep a non-empty connection string so DatabaseOptions validation passes; the
                // provider is swapped for SQLite below so this value is never actually used.
                ["Database:ConnectionString"] = "Server=unused;Database=unused;",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Drop every EF Core registration tied to the SQL Server AppDbContext so re-adding the
            // SQLite one below doesn't leave two providers registered in the same container.
            var efDescriptors = services.Where(d =>
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(AppDbContext) ||
                    (d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ?? false))
                .ToList();
            foreach (var descriptor in efDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AppDbContext>((sp, options) =>
            {
                options.UseSqlite(_connection);
                options.AddInterceptors(
                    sp.GetRequiredService<App.Infrastructure.Persistence.Interceptors.AuditSaveChangesInterceptor>());
            });

            // These Task 2.2 pipeline scenarios exercise correlation → auth → tenant-context →
            // validation on the demo endpoints; they predate RBAC seeding. Task 5.3 replaced the
            // permissive default IPermissionEvaluator with the real role-driven AuthorizationService,
            // which (correctly) denies a token carrying no seeded roles. Re-register the permissive
            // evaluator here so the pipeline tests keep testing the pipeline rather than RBAC data;
            // the real evaluator's deny/allow behaviour is covered separately by PermissionGateTests
            // and the Property-3/4 tests (5.5/5.6). Added last so it wins DI resolution.
            services.AddScoped<IPermissionEvaluator,
                App.Infrastructure.Security.AllowAuthenticatedPermissionEvaluator>();
        });
    }

    /// <summary>Ensures the schema exists and seeds tenant rows for the given communities (as super admin).</summary>
    public async Task SeedAsync(params (Guid CommunityId, string Name)[] rows)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        // Seeding bypasses the tenant filter on write by inserting directly; the global filter is a
        // read filter, so inserts land regardless of the request scope.
        db.SampleTenantRecords.AddRange(rows.Select(r => new SampleTenantRecord
        {
            CommunityId = r.CommunityId,
            Name = r.Name,
        }));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Mints a bearer token signed with the test key carrying the App claim contract.
    /// Includes a <c>jti</c> and seeds a matching active <see cref="LoginSession"/> so the Task 5.2
    /// session-revocation hook (<c>OnTokenValidated</c>) accepts the token — the pipeline now
    /// rejects a token with no backing session, which is the whole point of logout revocation.
    /// </summary>
    public string CreateToken(
        string userId,
        IEnumerable<Guid>? communityIds = null,
        bool isSuperAdmin = false)
    {
        var tokenId = Guid.NewGuid().ToString("N");
        var claims = new List<Claim>
        {
            new(CommunityClaimTypes.UserId, userId),
            new(JwtRegisteredClaimNames.Jti, tokenId),
        };
        foreach (var id in communityIds ?? [])
        {
            claims.Add(new Claim(CommunityClaimTypes.CommunityId, id.ToString()));
        }

        if (isSuperAdmin)
        {
            claims.Add(new Claim(CommunityClaimTypes.IsPlatformSuperAdmin, "true"));
        }

        var issuedAt = DateTime.UtcNow.AddMinutes(-1);
        var expiresAt = DateTime.UtcNow.AddMinutes(10);
        SeedActiveSession(userId, tokenId, issuedAt, expiresAt);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: issuedAt,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Inserts an active LoginSession row so the revocation hook finds the token's session.</summary>
    private void SeedActiveSession(string userId, string tokenId, DateTime issuedAt, DateTime expiresAt)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        db.LoginSessions.Add(new LoginSession
        {
            // The pipeline tests use opaque string user ids; the session only needs a stable Guid key.
            UserId = Guid.NewGuid(),
            TokenId = tokenId,
            IssuedAtUtc = issuedAt,
            ExpiresAtUtc = expiresAt,
        });
        db.SaveChanges();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connection.Dispose();
        }

        base.Dispose(disposing);
    }
}
