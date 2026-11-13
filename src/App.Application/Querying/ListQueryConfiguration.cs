using System.Linq.Expressions;

namespace App.Application.Querying;

/// <summary>
/// Per-entity declaration of what a client is allowed to sort, search and filter on — the
/// security + index-friendliness seam. A list endpoint builds one of these once (mapping
/// client-facing field names to property expressions) and the shared query helper refuses any
/// sort/filter/search target that is not declared here. This is how we avoid arbitrary client
/// LINQ/SQL expressions (injection + over-exposure) while keeping each module's field map
/// explicit and in one place rather than magic strings sprinkled around (steering: no hardcoding).
///
/// Field names are matched case-insensitively. <see cref="DefaultSort"/> gives a stable
/// ordering when the client supplies none, and a tie-break on <see cref="DefaultSort"/> (or Id)
/// is always appended so paging via Skip/Take is deterministic.
/// </summary>
/// <typeparam name="T">The entity being queried.</typeparam>
public sealed class ListQueryConfiguration<T>
{
    private readonly Dictionary<string, LambdaExpression> _sortable =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FilterableField> _filterable =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Expression<Func<T, string?>>> _searchable = [];

    /// <summary>
    /// The deterministic fallback sort used when the client requests none, and also appended as
    /// the final tie-break on every query so Skip/Take paging is stable. Defaults to ascending
    /// by <c>Id</c> via <see cref="SortById"/>; override with <see cref="WithDefaultSort{TKey}"/>.
    /// </summary>
    public SortTerm DefaultSort { get; private set; } = SortById();

    internal IReadOnlyDictionary<string, LambdaExpression> SortableFields => _sortable;
    internal IReadOnlyDictionary<string, FilterableField> FilterableFields => _filterable;
    internal IReadOnlyList<Expression<Func<T, string?>>> SearchableFields => _searchable;

    /// <summary>Declare a field the client may sort by, mapping its name to the property selector.</summary>
    public ListQueryConfiguration<T> Sortable<TKey>(string field, Expression<Func<T, TKey>> selector)
    {
        _sortable[field] = selector;
        return this;
    }

    /// <summary>
    /// Declare a field the client may filter on, mapping its name to the property selector.
    /// The selector's return type is used to parse the incoming string value.
    /// </summary>
    public ListQueryConfiguration<T> Filterable<TKey>(string field, Expression<Func<T, TKey>> selector)
    {
        _filterable[field] = new FilterableField(selector, typeof(TKey));
        return this;
    }

    /// <summary>Declare a string field included in free-text search (contains).</summary>
    public ListQueryConfiguration<T> Searchable(Expression<Func<T, string?>> selector)
    {
        _searchable.Add(selector);
        return this;
    }

    /// <summary>Set the deterministic default/tie-break sort to an explicit property.</summary>
    public ListQueryConfiguration<T> WithDefaultSort<TKey>(
        Expression<Func<T, TKey>> selector,
        SortDirection direction = SortDirection.Ascending)
    {
        DefaultSort = new SortTerm(selector, direction);
        return this;
    }

    private static SortTerm SortById() => new((Expression<Func<T, Guid>>)DefaultIdSelector(), SortDirection.Ascending);

    // All entities derive from a base carrying a Guid Id; bind to it by name so the default
    // sort/tie-break works without each module restating it.
    private static LambdaExpression DefaultIdSelector()
    {
        var param = Expression.Parameter(typeof(T), "e");
        var body = Expression.Property(param, "Id");
        return Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(T), typeof(Guid)), body, param);
    }

    /// <summary>A resolved default/tie-break sort: the selector and its direction.</summary>
    public sealed record SortTerm(LambdaExpression Selector, SortDirection Direction);

    /// <summary>An allow-listed filterable field: its selector plus the CLR type to parse values into.</summary>
    internal sealed record FilterableField(LambdaExpression Selector, Type ValueType);
}
