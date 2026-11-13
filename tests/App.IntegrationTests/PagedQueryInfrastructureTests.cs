using App.Application.Abstractions;
using App.Application.Exceptions;
using App.Application.Querying;
using App.Domain.Tenancy;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Interceptors;
using App.Infrastructure.Querying;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace App.IntegrationTests;

/// <summary>
/// Runnable check for Task 2.3: exercises the shared pagination/filter/sort/search helper end to
/// end against a real relational provider (SQLite) so filter/sort/search/count/page all run as
/// generated SQL — not in-memory LINQ (Req 9.3, 9.4, 9.5, 65.1). Proves: pageSize is clamped to
/// the configured max; page/items/totalCount are correct across pages; allow-listed sort asc/desc
/// works and an unknown sort field is rejected; search matches expected rows; a filter reduces the
/// set in-query.
/// </summary>
public sealed class PagedQueryInfrastructureTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Guid _community = Guid.NewGuid();
    private readonly IAsyncQueryExecutor _executor = new EfAsyncQueryExecutor();

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private sealed class StubPageSizePolicy(int defaultSize, int maxSize) : IPageSizePolicy
    {
        public int DefaultPageSize { get; } = defaultSize;
        public int MaxPageSize { get; } = maxSize;
    }

    // The entity's declared allow-list: only these fields may be sorted/searched/filtered.
    private static ListQueryConfiguration<SampleTenantRecord> Config() =>
        new ListQueryConfiguration<SampleTenantRecord>()
            .Sortable("name", r => r.Name)
            .Searchable(r => r.Name)
            .Filterable("name", r => r.Name)
            .WithDefaultSort(r => r.Name);

    public PagedQueryInfrastructureTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        SeedAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewContext()
    {
        var tenant = new TestTenantContext { AuthorizedCommunityIds = [_community] };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(tenant, new FixedClock()))
            .Options;
        return new AppDbContext(options, tenant);
    }

    private async Task SeedAsync()
    {
        var seedTenant = new TestTenantContext { IsPlatformSuperAdmin = true };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(seedTenant, new FixedClock()))
            .Options;
        await using var ctx = new AppDbContext(options, seedTenant);
        await ctx.Database.EnsureCreatedAsync();

        // 10 rows: "Item 00".."Item 09".
        for (var i = 0; i < 10; i++)
        {
            ctx.SampleTenantRecords.Add(new SampleTenantRecord
            {
                CommunityId = _community,
                Name = $"Item {i:00}",
            });
        }

        await ctx.SaveChangesAsync();
    }

    private IQueryable<SampleTenantRecord> Query(AppDbContext ctx) =>
        ctx.SampleTenantRecords.AsNoTracking();

    [Fact]
    public async Task Clamps_requested_pageSize_to_configured_max()
    {
        await using var ctx = NewContext();
        var policy = new StubPageSizePolicy(defaultSize: 50, maxSize: 3);
        var parameters = new ListQueryParameters { Page = 1, PageSize = 999 };

        var result = await Query(ctx)
            .ToPagedResultAsync(parameters, Config(), policy, _executor);

        Assert.Equal(3, result.PageSize);          // clamped down from 999
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(10, result.TotalCount);        // total unaffected by paging
    }

    [Fact]
    public async Task Default_pageSize_used_when_unset()
    {
        await using var ctx = NewContext();
        var policy = new StubPageSizePolicy(defaultSize: 4, maxSize: 100);

        var result = await Query(ctx)
            .ToPagedResultAsync(new ListQueryParameters(), Config(), policy, _executor);

        Assert.Equal(4, result.PageSize);
        Assert.Equal(4, result.Items.Count);
    }

    [Fact]
    public async Task Paging_returns_correct_window_and_totalCount_across_pages()
    {
        await using var ctx = NewContext();
        var policy = new StubPageSizePolicy(defaultSize: 50, maxSize: 50);

        var page1 = await Query(ctx).ToPagedResultAsync(
            new ListQueryParameters { Page = 1, PageSize = 4 }, Config(), policy, _executor);
        var page2 = await Query(ctx).ToPagedResultAsync(
            new ListQueryParameters { Page = 2, PageSize = 4 }, Config(), policy, _executor);
        var page3 = await Query(ctx).ToPagedResultAsync(
            new ListQueryParameters { Page = 3, PageSize = 4 }, Config(), policy, _executor);

        Assert.Equal(10, page1.TotalCount);
        Assert.Equal(["Item 00", "Item 01", "Item 02", "Item 03"], page1.Items.Select(r => r.Name));
        Assert.Equal(["Item 04", "Item 05", "Item 06", "Item 07"], page2.Items.Select(r => r.Name));
        Assert.Equal(["Item 08", "Item 09"], page3.Items.Select(r => r.Name)); // last partial page
    }

    [Fact]
    public async Task Sort_by_allowlisted_field_descending_orders_rows()
    {
        await using var ctx = NewContext();
        var policy = new StubPageSizePolicy(defaultSize: 50, maxSize: 50);
        var parameters = new ListQueryParameters
        {
            Sort = [new SortField("name", SortDirection.Descending)],
        };

        var result = await Query(ctx).ToPagedResultAsync(parameters, Config(), policy, _executor);

        Assert.Equal("Item 09", result.Items[0].Name);
        Assert.Equal("Item 00", result.Items[^1].Name);
    }

    [Fact]
    public async Task Unknown_sort_field_is_rejected()
    {
        await using var ctx = NewContext();
        var policy = new StubPageSizePolicy(defaultSize: 50, maxSize: 50);
        var parameters = new ListQueryParameters
        {
            Sort = [new SortField("passwordHash")], // not in the allow-list
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            Query(ctx).ToPagedResultAsync(parameters, Config(), policy, _executor));
    }

    [Fact]
    public async Task Search_matches_expected_rows_in_query()
    {
        await using var ctx = NewContext();
        var policy = new StubPageSizePolicy(defaultSize: 50, maxSize: 50);
        var parameters = new ListQueryParameters { Search = "0" }; // matches Item 00..Item 09? only "0" in number

        var result = await Query(ctx).ToPagedResultAsync(parameters, Config(), policy, _executor);

        // Every "Item 0X" name contains a '0', so all 10 match "0"; narrow to a unique token:
        var narrow = await Query(ctx).ToPagedResultAsync(
            new ListQueryParameters { Search = "07" }, Config(), policy, _executor);

        Assert.Equal(10, result.TotalCount);
        Assert.Single(narrow.Items);
        Assert.Equal("Item 07", narrow.Items[0].Name);
    }

    [Fact]
    public async Task Filter_reduces_the_set_in_query()
    {
        await using var ctx = NewContext();
        var policy = new StubPageSizePolicy(defaultSize: 50, maxSize: 50);
        var parameters = new ListQueryParameters
        {
            Filters = [new FilterTerm("name", FilterOperator.Equals, "Item 05")],
        };

        var result = await Query(ctx).ToPagedResultAsync(parameters, Config(), policy, _executor);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("Item 05", result.Items[0].Name);
    }

    [Fact]
    public async Task Unknown_filter_field_is_rejected()
    {
        await using var ctx = NewContext();
        var policy = new StubPageSizePolicy(defaultSize: 50, maxSize: 50);
        var parameters = new ListQueryParameters
        {
            Filters = [new FilterTerm("isDeleted", FilterOperator.Equals, "true")], // not allow-listed
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            Query(ctx).ToPagedResultAsync(parameters, Config(), policy, _executor));
    }
}
