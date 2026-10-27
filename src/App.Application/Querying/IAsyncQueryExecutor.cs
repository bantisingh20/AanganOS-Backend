namespace App.Application.Querying;

/// <summary>
/// Provider-agnostic seam for the two async terminal operations the paged helper needs — a
/// COUNT and a page materialization. Application shapes the <see cref="IQueryable{T}"/> (filter/
/// sort/search/Skip/Take) but does not reference an ORM; Infrastructure supplies an EF Core
/// implementation that calls <c>CountAsync</c>/<c>ToListAsync</c> so both run as SQL (Req 9.3).
/// This keeps the generic helper in the Application layer without leaking the EF provider up.
/// </summary>
public interface IAsyncQueryExecutor
{
    /// <summary>Execute a COUNT over the (already filtered) query as SQL.</summary>
    Task<long> CountAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default);

    /// <summary>Materialize the (already filtered/sorted/paged) query to a list as SQL.</summary>
    Task<List<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default);
}
