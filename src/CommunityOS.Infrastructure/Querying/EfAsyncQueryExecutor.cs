using CommunityOS.Application.Querying;
using Microsoft.EntityFrameworkCore;

namespace CommunityOS.Infrastructure.Querying;

/// <summary>
/// EF Core implementation of the Application-layer <see cref="IAsyncQueryExecutor"/> seam. Runs
/// the COUNT and the page as async SQL (<c>CountAsync</c>/<c>ToListAsync</c>) so paging never
/// blocks the thread pool and never materializes the whole table (Req 9.3, 65.1). Lives in
/// Infrastructure so the EF provider dependency stays out of the Application layer.
/// </summary>
public sealed class EfAsyncQueryExecutor : IAsyncQueryExecutor
{
    public Task<long> CountAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default) =>
        query.LongCountAsync(cancellationToken);

    public Task<List<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default) =>
        query.ToListAsync(cancellationToken);
}
