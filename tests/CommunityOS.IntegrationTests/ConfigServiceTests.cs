using CommunityOS.Application.Abstractions;
using CommunityOS.Application.Abstractions.Audit;
using CommunityOS.Application.Config;
using CommunityOS.Application.Querying;
using CommunityOS.Infrastructure.Config;
using CommunityOS.Infrastructure.Persistence;
using CommunityOS.Infrastructure.Persistence.Interceptors;
using CommunityOS.Infrastructure.Querying;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CommunityOS.IntegrationTests;

/// <summary>
/// Runnable check for Task 3.1 (Config_Service + master-data + page-size seam). Exercises the real
/// <see cref="ConfigService"/> and <see cref="ConfigPageSizePolicy"/> against a relational provider
/// (SQLite) with the EF seed applied, proving:
/// <list type="bullet">
///   <item>a community-scoped value overrides the platform value for the same key (Req 8.1);</item>
///   <item>resolution falls back to the platform default when no community value exists (Req 8.3);</item>
///   <item>typed getters parse safely and fall back on garbage (int/bool/enum/decimal);</item>
///   <item>a <c>Set</c> invalidates the cache so the next read returns the new value (Req 65.2);</item>
///   <item>the seeded Unit_Type / Unit_Status master lists resolve (Req 8.4);</item>
///   <item>a community page-size override is honoured, else the platform PaginationOptions default (Req 9.4, 65.2).</item>
/// </list>
/// The dedicated Property-13 fallback test is sibling Task 3.2 and is intentionally not implemented here.
/// </summary>
public sealed class ConfigServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Guid _community = Guid.NewGuid();
    private readonly Guid _otherCommunity = Guid.NewGuid();

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>Minimal <see cref="IOptionsSnapshot{T}"/> wrapper so the policy can take platform defaults.</summary>
    private sealed class StubOptionsSnapshot(PaginationOptions value) : IOptionsSnapshot<PaginationOptions>
    {
        public PaginationOptions Value { get; } = value;
        public PaginationOptions Get(string? name) => Value;
    }

    public ConfigServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        // Create schema (applies HasData seed for Unit_Type / Unit_Status).
        var seedTenant = new TestTenantContext { IsPlatformSuperAdmin = true };
        using var ctx = NewContext(seedTenant);
        ctx.Database.EnsureCreated();
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

    private static (IMemoryCache cache, ConfigService svc, AppDbContext ctx) BuildService(
        AppDbContext ctx, ITenantContext tenant)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var svc = new ConfigService(ctx, cache, new NoOpConfigAuditSink(), tenant);
        return (cache, svc, ctx);
    }

    private TestTenantContext Tenant(Guid? community = null) =>
        new() { AuthorizedCommunityIds = community is null ? [] : [community.Value], UserId = "tester" };

    [Fact]
    public async Task Community_value_overrides_platform_value_for_same_key()
    {
        var tenant = Tenant(_community);
        await using var ctx = NewContext(tenant);
        var (_, svc, _) = BuildService(ctx, tenant);

        await svc.SetAsync("greeting", "hello-platform");            // platform default
        await svc.SetAsync("greeting", "hello-community", _community); // community override

        Assert.Equal("hello-community", await svc.GetAsync("greeting", _community));
        Assert.Equal("hello-platform", await svc.GetAsync("greeting")); // platform read unaffected
    }

    [Fact]
    public async Task Falls_back_to_platform_default_when_no_community_value()
    {
        var tenant = Tenant(_community);
        await using var ctx = NewContext(tenant);
        var (_, svc, _) = BuildService(ctx, tenant);

        await svc.SetAsync("timezone", "UTC"); // platform only

        // A community with no override for the key gets the platform default (Req 8.3).
        Assert.Equal("UTC", await svc.GetAsync("timezone", _community));
        Assert.Equal("UTC", await svc.GetAsync("timezone", _otherCommunity));
        Assert.Null(await svc.GetAsync("missing.key", _community));
    }

    [Fact]
    public async Task Typed_getters_parse_and_fall_back_on_garbage()
    {
        var tenant = Tenant(_community);
        await using var ctx = NewContext(tenant);
        var (_, svc, _) = BuildService(ctx, tenant);

        await svc.SetAsync("limit", "42");
        await svc.SetAsync("enabled", "true");
        await svc.SetAsync("rate", "3.5");
        await svc.SetAsync("mode", "Descending"); // SortDirection enum value
        await svc.SetAsync("broken", "not-a-number");

        Assert.Equal(42, await svc.GetIntAsync("limit", 0));
        Assert.True(await svc.GetBoolAsync("enabled", false));
        Assert.Equal(3.5m, await svc.GetDecimalAsync("rate", 0m));
        Assert.Equal(SortDirection.Descending, await svc.GetEnumAsync("mode", SortDirection.Ascending));

        // Unparseable / unset values fall back to the supplied default (safe parsing).
        Assert.Equal(7, await svc.GetIntAsync("broken", 7));
        Assert.Equal(99, await svc.GetIntAsync("absent", 99));
    }

    [Fact]
    public async Task Set_invalidates_cache_so_next_read_returns_new_value()
    {
        var tenant = Tenant(_community);
        await using var ctx = NewContext(tenant);
        var (_, svc, _) = BuildService(ctx, tenant);

        await svc.SetAsync("feature.flag", "off", _community);
        Assert.Equal("off", await svc.GetAsync("feature.flag", _community)); // populates cache

        await svc.SetAsync("feature.flag", "on", _community); // must evict the cached "off"
        Assert.Equal("on", await svc.GetAsync("feature.flag", _community));
    }

    [Fact]
    public async Task Seeded_unit_type_and_unit_status_master_lists_resolve()
    {
        var tenant = Tenant(_community);
        await using var ctx = NewContext(tenant);
        var (_, svc, _) = BuildService(ctx, tenant);

        var types = await svc.GetMasterDataAsync(MasterDataKeys.UnitType, _community);
        var statuses = await svc.GetMasterDataAsync(MasterDataKeys.UnitStatus, _community);

        Assert.Contains(types, e => e.Code == "apartment");
        Assert.Contains(types, e => e.Code == "villa");
        Assert.Equal(6, types.Count); // apartment, villa, bungalow, townhouse, plot, parking
        Assert.Contains(statuses, e => e.Code == "occupied");
        Assert.Equal(5, statuses.Count);
        // Ordered by seeded SortOrder.
        Assert.Equal("apartment", types[0].Code);
    }

    [Fact]
    public async Task Community_master_data_entry_overrides_platform_entry_with_same_code()
    {
        var tenant = Tenant(_community);
        await using var ctx = NewContext(tenant);
        var (_, svc, _) = BuildService(ctx, tenant);

        // Relabel "apartment" just for this community; platform list stays unchanged.
        await svc.SetMasterDataAsync(MasterDataKeys.UnitType,
            new MasterDataEntry("apartment", "Flat", 0, true), _community);

        var forCommunity = await svc.GetMasterDataAsync(MasterDataKeys.UnitType, _community);
        var forPlatform = await svc.GetMasterDataAsync(MasterDataKeys.UnitType);

        Assert.Equal("Flat", forCommunity.Single(e => e.Code == "apartment").Label);
        Assert.Equal("Apartment", forPlatform.Single(e => e.Code == "apartment").Label);
    }

    [Fact]
    public async Task PageSizePolicy_honours_community_override_else_platform_default()
    {
        var platformOptions = new StubOptionsSnapshot(new PaginationOptions { DefaultPageSize = 50, MaxPageSize = 200 });

        // Seed a per-community max override via Config_Service.
        var writerTenant = Tenant(_community);
        await using (var writeCtx = NewContext(writerTenant))
        {
            var (_, svc, _) = BuildService(writeCtx, writerTenant);
            await svc.SetAsync(ConfigKeys.PaginationMaxPageSize, "10", _community);
        }

        // Policy for the overriding community sees the override.
        var communityTenant = Tenant(_community);
        await using (var ctx = NewContext(communityTenant))
        {
            var policy = new ConfigPageSizePolicy(platformOptions, ctx, new MemoryCache(new MemoryCacheOptions()), communityTenant);
            Assert.Equal(10, policy.MaxPageSize);
            Assert.Equal(10, policy.DefaultPageSize); // clamped down to the override max
        }

        // Policy for a different community (no override) falls back to the platform default.
        var otherTenant = Tenant(_otherCommunity);
        await using (var ctx = NewContext(otherTenant))
        {
            var policy = new ConfigPageSizePolicy(platformOptions, ctx, new MemoryCache(new MemoryCacheOptions()), otherTenant);
            Assert.Equal(200, policy.MaxPageSize);
            Assert.Equal(50, policy.DefaultPageSize);
        }
    }
}
