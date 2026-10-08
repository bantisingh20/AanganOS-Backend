using CommunityOS.Application.Abstractions;
using CommunityOS.Application.Audit;
using CommunityOS.Application.Querying;
using CommunityOS.Domain.Audit;
using CommunityOS.Domain.Tenancy;
using CommunityOS.Infrastructure.Audit;
using CommunityOS.Infrastructure.Config;
using CommunityOS.Infrastructure.Persistence;
using CommunityOS.Infrastructure.Persistence.Interceptors;
using CommunityOS.Infrastructure.Querying;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommunityOS.IntegrationTests;

/// <summary>
/// Runnable check for Task 4.1 (Audit_Service + append-only AuditLog). Exercises the real
/// <see cref="AuditService"/> and the append-only data-layer guard against a relational provider
/// (SQLite), proving:
/// <list type="bullet">
///   <item><see cref="AppendAsync"/> writes an entry stamping acting user / action / resource /
///   community / timestamp from the tenant context + clock (Req 6.1, 6.2);</item>
///   <item>modifying or deleting a persisted <c>AuditLog</c> row through the context is rejected —
///   append-only (Req 6.3);</item>
///   <item>a soft-deleted resource's audit entries are still retrievable (Req 6.4);</item>
///   <item>the audit read is tenant-scoped — a community admin sees only their community's entries,
///   a super admin sees across (Req 6.5);</item>
///   <item>a Config_Service edit now produces a real audit entry via the real sink (Req 8.2).</item>
/// </list>
/// Properties 5 (completeness) and 6 (append-only immutability/ordering) are sibling Tasks 4.2/4.3
/// and are intentionally not implemented here.
/// </summary>
public sealed class AuditServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Guid _communityA = Guid.NewGuid();
    private readonly Guid _communityB = Guid.NewGuid();
    private readonly IAsyncQueryExecutor _executor = new EfAsyncQueryExecutor();

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    }

    private sealed class StubPageSizePolicy(int defaultSize = 50, int maxSize = 200) : IPageSizePolicy
    {
        public int DefaultPageSize { get; } = defaultSize;
        public int MaxPageSize { get; } = maxSize;
    }

    public AuditServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var seedTenant = new TestTenantContext { IsPlatformSuperAdmin = true };
        using var ctx = NewContext(seedTenant, new FixedClock());
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewContext(ITenantContext tenant, IClock clock)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(tenant, clock))
            .Options;
        return new AppDbContext(options, tenant);
    }

    private AuditService NewService(AppDbContext ctx, ITenantContext tenant, IClock clock) =>
        new(ctx, tenant, clock, new StubPageSizePolicy(), _executor);

    private TestTenantContext Admin(Guid community, string user) =>
        new() { AuthorizedCommunityIds = [community], UserId = user };

    [Fact]
    public async Task AppendAsync_writes_entry_with_actor_action_resource_community_timestamp()
    {
        var clock = new FixedClock();
        var tenant = Admin(_communityA, "admin-a");
        await using var ctx = NewContext(tenant, clock);
        var svc = NewService(ctx, tenant, clock);

        var resourceId = Guid.NewGuid().ToString();
        await svc.AppendAsync(new AuditEntry("Create", "Unit", resourceId, _communityA, "{\"n\":1}"));

        var row = await ctx.AuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal("admin-a", row.ActingUserId);        // from tenant context, not the caller (Req 6.1)
        Assert.Equal("Create", row.Action);
        Assert.Equal("Unit", row.ResourceType);
        Assert.Equal(resourceId, row.ResourceId);
        Assert.Equal(_communityA, row.CommunityId);
        Assert.Equal(clock.UtcNow, row.TimestampUtc);     // from the clock, not the caller
        Assert.Equal("{\"n\":1}", row.Detail);
    }

    [Fact]
    public async Task Login_security_event_can_be_recorded_without_a_community()
    {
        var clock = new FixedClock();
        var tenant = new TestTenantContext { UserId = "someone" };
        await using var ctx = NewContext(tenant, clock);
        var svc = NewService(ctx, tenant, clock);

        // A platform-level security event (Req 6.2) has no owning community.
        await svc.AppendAsync(new AuditEntry("LoginFailed", "User", "someone", CommunityId: null, Detail: "bad password"));

        var row = await ctx.AuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal("LoginFailed", row.Action);
        Assert.Null(row.CommunityId);
    }

    [Fact]
    public async Task Modifying_a_persisted_audit_row_is_rejected()
    {
        var clock = new FixedClock();
        var tenant = Admin(_communityA, "admin-a");
        await using var ctx = NewContext(tenant, clock);
        var svc = NewService(ctx, tenant, clock);

        await svc.AppendAsync(new AuditEntry("Create", "Unit", "u1", _communityA, null));

        var row = await ctx.AuditLogs.SingleAsync(); // tracked
        row.Action = "Tampered";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
        Assert.Contains("append-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deleting_a_persisted_audit_row_is_rejected()
    {
        var clock = new FixedClock();
        var tenant = Admin(_communityA, "admin-a");
        await using var ctx = NewContext(tenant, clock);
        var svc = NewService(ctx, tenant, clock);

        await svc.AppendAsync(new AuditEntry("Create", "Unit", "u1", _communityA, null));

        var row = await ctx.AuditLogs.SingleAsync();
        ctx.AuditLogs.Remove(row);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());

        // The row is still physically there (nothing was deleted).
        await using var verify = NewContext(tenant, clock);
        Assert.Equal(1, await verify.AuditLogs.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Audit_entries_survive_soft_delete_of_the_referenced_resource()
    {
        var clock = new FixedClock();
        var admin = new TestTenantContext { IsPlatformSuperAdmin = true, UserId = "admin-a", AuthorizedCommunityIds = [_communityA] };
        await using var ctx = NewContext(admin, clock);
        var svc = NewService(ctx, admin, clock);

        // Create a resource and audit its creation.
        var resource = new SampleTenantRecord { CommunityId = _communityA, Name = "unit-1" };
        ctx.SampleTenantRecords.Add(resource);
        await ctx.SaveChangesAsync();
        await svc.AppendAsync(new AuditEntry("Create", "SampleTenantRecord", resource.Id.ToString(), _communityA, null));

        // Soft-delete the resource (interceptor converts Remove into a soft delete).
        ctx.SampleTenantRecords.Remove(resource);
        await ctx.SaveChangesAsync();

        // The resource is gone from ordinary queries...
        Assert.Empty(await ctx.SampleTenantRecords.AsNoTracking().ToListAsync());
        // ...but its audit entry remains fully retrievable (Req 6.4).
        var page = await svc.QueryAsync(new ListQueryParameters
        {
            Filters = [new FilterTerm("resourceId", FilterOperator.Equals, resource.Id.ToString())],
        });
        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Create", page.Items[0].Action);
    }

    [Fact]
    public async Task Read_is_tenant_scoped_community_admin_sees_only_own_super_admin_sees_across()
    {
        var clock = new FixedClock();

        // Seed entries for two communities + a platform event, each from its own admin context.
        await using (var ctxA = NewContext(Admin(_communityA, "admin-a"), clock))
        {
            await NewService(ctxA, Admin(_communityA, "admin-a"), clock)
                .AppendAsync(new AuditEntry("Create", "Unit", "a1", _communityA, null));
        }
        await using (var ctxB = NewContext(Admin(_communityB, "admin-b"), clock))
        {
            await NewService(ctxB, Admin(_communityB, "admin-b"), clock)
                .AppendAsync(new AuditEntry("Create", "Unit", "b1", _communityB, null));
        }
        await using (var platform = NewContext(new TestTenantContext { UserId = "sys" }, clock))
        {
            await NewService(platform, new TestTenantContext { UserId = "sys" }, clock)
                .AppendAsync(new AuditEntry("PlatformConfig", "Configuration", "k", CommunityId: null, Detail: null));
        }

        // Community-A admin: sees only community-A entries (not B, not the platform event).
        var tenantA = Admin(_communityA, "admin-a");
        await using (var ctx = NewContext(tenantA, clock))
        {
            var page = await NewService(ctx, tenantA, clock).QueryAsync(new ListQueryParameters());
            Assert.Equal(1, page.TotalCount);
            Assert.All(page.Items, e => Assert.Equal(_communityA, e.CommunityId));
        }

        // Super admin: sees across all communities and the platform event.
        var superAdmin = new TestTenantContext { IsPlatformSuperAdmin = true, UserId = "root" };
        await using (var ctx = NewContext(superAdmin, clock))
        {
            var page = await NewService(ctx, superAdmin, clock).QueryAsync(new ListQueryParameters());
            Assert.Equal(3, page.TotalCount);
        }
    }

    [Fact]
    public async Task Config_edit_produces_a_real_audit_entry_via_the_sink()
    {
        var clock = new FixedClock();
        var tenant = Admin(_communityA, "config-admin");
        await using var ctx = NewContext(tenant, clock);

        var auditSvc = NewService(ctx, tenant, clock);
        var sink = new AuditConfigAuditSink(auditSvc, NullLogger<AuditConfigAuditSink>.Instance);
        var config = new ConfigService(ctx, new MemoryCache(new MemoryCacheOptions()), sink, tenant);

        await config.SetAsync("feature.flag", "on", _communityA);

        var row = await ctx.AuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal("Configuration", row.ResourceType);
        Assert.Equal("feature.flag", row.ResourceId);
        Assert.Equal("Create", row.Action);          // first write of the key
        Assert.Equal(_communityA, row.CommunityId);
        Assert.Equal("config-admin", row.ActingUserId);
        Assert.Contains("on", row.Detail);
    }

    [Fact]
    public async Task Config_edit_of_a_secret_key_redacts_the_value_in_the_audit_detail()
    {
        var clock = new FixedClock();
        var tenant = Admin(_communityA, "config-admin");
        await using var ctx = NewContext(tenant, clock);

        var sink = new AuditConfigAuditSink(NewService(ctx, tenant, clock), NullLogger<AuditConfigAuditSink>.Instance);
        var config = new ConfigService(ctx, new MemoryCache(new MemoryCacheOptions()), sink, tenant);

        await config.SetAsync("integration.apiToken", "super-secret-value", _communityA);

        var row = await ctx.AuditLogs.AsNoTracking().SingleAsync();
        Assert.DoesNotContain("super-secret-value", row.Detail);
        Assert.Contains("***", row.Detail);
    }
}
