using UrbanNest.Application.Abstractions;
using UrbanNest.Domain.Tenancy;
using UrbanNest.Infrastructure.Persistence;
using UrbanNest.Infrastructure.Persistence.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace UrbanNest.IntegrationTests;

/// <summary>
/// Smoke tests proving the EF Core data layer wires up and the multi-tenant global query
/// filter actually applies (Task 1.1 / Req 5.2–5.5, 3.7, 22.4). Runs against a real relational
/// provider (SQLite shared in-memory) so the filter is exercised in generated SQL, not an
/// in-memory stub.
/// </summary>
public sealed class AppDbContextGlobalFilterSmokeTests : IDisposable
{
    private readonly SqliteConnection _connection;

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    public AppDbContextGlobalFilterSmokeTests()
    {
        // One shared in-memory DB for the whole test: the connection stays open so the schema
        // and seeded rows survive across the several context instances we create below.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewContext(ITenantContext tenant)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(tenant, new FixedClock()))
            .Options;
        return new AppDbContext(options, tenant);
    }

    [Fact]
    public async Task Context_constructs_and_creates_schema()
    {
        var ctx = NewContext(new TestTenantContext());
        var created = await ctx.Database.EnsureCreatedAsync();
        Assert.True(created);
    }

    [Fact]
    public async Task Global_filter_isolates_tenants_and_excludes_soft_deleted()
    {
        var communityA = Guid.NewGuid();
        var communityB = Guid.NewGuid();

        // Seed as super admin so inserts aren't themselves filtered.
        var seedCtx = NewContext(new TestTenantContext { IsPlatformSuperAdmin = true });
        await seedCtx.Database.EnsureCreatedAsync();
        seedCtx.SampleTenantRecords.AddRange(
            new SampleTenantRecord { CommunityId = communityA, Name = "A-live" },
            new SampleTenantRecord { CommunityId = communityA, Name = "A-deleted", IsDeleted = true },
            new SampleTenantRecord { CommunityId = communityB, Name = "B-live" });
        await seedCtx.SaveChangesAsync();

        // A tenant-A user: sees only A's live row. Foreign tenant + soft-deleted are gone.
        var ctxA = NewContext(new TestTenantContext { AuthorizedCommunityIds = [communityA] });
        var namesA = await ctxA.SampleTenantRecords.AsNoTracking()
            .Select(r => r.Name).ToListAsync();
        Assert.Equal(["A-live"], namesA);

        // A different context (same cached model) scoped to B sees only B — proves the filter
        // reads the *current* tenant context per request, not a stale instance baked into the model.
        var ctxB = NewContext(new TestTenantContext { AuthorizedCommunityIds = [communityB] });
        var namesB = await ctxB.SampleTenantRecords.AsNoTracking()
            .Select(r => r.Name).ToListAsync();
        Assert.Equal(["B-live"], namesB);

        // Super admin short-circuit: sees both live rows across tenants, still excludes soft-deleted.
        var ctxAdmin = NewContext(new TestTenantContext { IsPlatformSuperAdmin = true });
        var adminNames = await ctxAdmin.SampleTenantRecords.AsNoTracking()
            .Select(r => r.Name).OrderBy(n => n).ToListAsync();
        Assert.Equal(["A-live", "B-live"], adminNames);
    }

    [Fact]
    public async Task Resident_self_scope_restricts_to_own_unit()
    {
        var community = Guid.NewGuid();
        var myUnit = Guid.NewGuid();
        var otherUnit = Guid.NewGuid();

        var seedCtx = NewContext(new TestTenantContext { IsPlatformSuperAdmin = true });
        await seedCtx.Database.EnsureCreatedAsync();
        seedCtx.SampleTenantRecords.AddRange(
            new SampleTenantRecord { CommunityId = community, UnitId = myUnit, Name = "mine" },
            new SampleTenantRecord { CommunityId = community, UnitId = otherUnit, Name = "neighbour" });
        await seedCtx.SaveChangesAsync();

        var resident = new TestTenantContext
        {
            AuthorizedCommunityIds = [community],
            IsResidentSelfScoped = true,
            ResidentUnitId = myUnit,
        };
        var ctx = NewContext(resident);
        var names = await ctx.SampleTenantRecords.AsNoTracking().Select(r => r.Name).ToListAsync();
        Assert.Equal(["mine"], names);
    }

    [Fact]
    public async Task Soft_delete_interceptor_sets_flag_instead_of_removing_row()
    {
        var community = Guid.NewGuid();
        var admin = new TestTenantContext { IsPlatformSuperAdmin = true, UserId = "tester" };

        var ctx = NewContext(admin);
        await ctx.Database.EnsureCreatedAsync();
        var row = new SampleTenantRecord { CommunityId = community, Name = "to-delete" };
        ctx.SampleTenantRecords.Add(row);
        await ctx.SaveChangesAsync();

        ctx.SampleTenantRecords.Remove(row);
        await ctx.SaveChangesAsync();

        // Filtered query hides it, but IgnoreQueryFilters finds the physically-retained soft-deleted row.
        Assert.Empty(await ctx.SampleTenantRecords.AsNoTracking().ToListAsync());
        var retained = await ctx.SampleTenantRecords.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        Assert.True(retained.IsDeleted);
        Assert.NotNull(retained.DeletedAtUtc);
        Assert.Equal("tester", retained.CreatedBy);
    }
}
