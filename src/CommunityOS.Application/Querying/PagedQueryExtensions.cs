using CommunityOS.Contracts.Envelope;

namespace CommunityOS.Application.Querying;

/// <summary>
/// The single entry point module list endpoints call: given a (tenant-filtered, AsNoTracking)
/// <see cref="IQueryable{T}"/>, the client's <see cref="ListQueryParameters"/> and the entity's
/// allow-list <see cref="ListQueryConfiguration{T}"/>, it applies filter/sort/search in-query,
/// clamps the page size against the configured policy (Req 9.4), runs a COUNT and the page via
/// Skip/Take, and returns a <see cref="PagedData{T}"/> (items + page + pageSize + totalCount —
/// Req 9.3). Everything is pushed to SQL through <see cref="IAsyncQueryExecutor"/>; nothing is
/// filtered in memory (Req 9.5, 65.1).
/// </summary>
public static class PagedQueryExtensions
{
    /// <summary>Apply list params + clamp + count + page and project to a result shape.</summary>
    public static async Task<PagedData<TResult>> ToPagedResultAsync<T, TResult>(
        this IQueryable<T> source,
        ListQueryParameters parameters,
        ListQueryConfiguration<T> config,
        Func<T, TResult> projection,
        IPageSizePolicy pageSizePolicy,
        IAsyncQueryExecutor executor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(pageSizePolicy);
        ArgumentNullException.ThrowIfNull(executor);

        var page = NormalizePage(parameters.Page);
        var pageSize = ClampPageSize(parameters.PageSize, pageSizePolicy);

        // Shape the query (filter -> search -> sort + deterministic tie-break) once; reuse the
        // filtered-but-unpaged query for the COUNT so both see the same predicate.
        var filtered = ListQueryBuilder.Apply(source, parameters, config);

        var totalCount = await executor.CountAsync(filtered, cancellationToken);

        var pageQuery = filtered.Skip((page - 1) * pageSize).Take(pageSize);
        var rows = await executor.ToListAsync(pageQuery, cancellationToken);

        return new PagedData<TResult>
        {
            Items = rows.Select(projection).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        };
    }

    /// <summary>Overload for list endpoints that return the entity shape directly (no projection).</summary>
    public static Task<PagedData<T>> ToPagedResultAsync<T>(
        this IQueryable<T> source,
        ListQueryParameters parameters,
        ListQueryConfiguration<T> config,
        IPageSizePolicy pageSizePolicy,
        IAsyncQueryExecutor executor,
        CancellationToken cancellationToken = default) =>
        source.ToPagedResultAsync(parameters, config, static x => x, pageSizePolicy, executor, cancellationToken);

    private static int NormalizePage(int? page) => page is null or < 1 ? 1 : page.Value;

    /// <summary>
    /// Default when unset/≤0; clamp down to the configured maximum when the client asks for more
    /// (Req 9.4). The ceiling comes from <see cref="IPageSizePolicy"/>, never a literal here.
    /// </summary>
    private static int ClampPageSize(int? requested, IPageSizePolicy policy)
    {
        if (requested is null or < 1)
        {
            return Math.Min(policy.DefaultPageSize, policy.MaxPageSize);
        }

        return Math.Min(requested.Value, policy.MaxPageSize);
    }
}
