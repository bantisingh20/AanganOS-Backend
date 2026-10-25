namespace UrbanNest.Contracts.Envelope;

/// <summary>
/// The <c>data</c> payload for a list endpoint: the page of items plus paging metadata
/// (Req 9.3). Pagination/filter/sort/search query infrastructure is Task 2.3; this is only the
/// response shape.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed class PagedData<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];

    public int Page { get; init; }

    public int PageSize { get; init; }

    public long TotalCount { get; init; }
}
