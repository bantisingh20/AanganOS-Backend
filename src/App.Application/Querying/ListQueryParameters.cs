namespace App.Application.Querying;

/// <summary>
/// A single requested sort term: the client-facing field name plus direction.
/// The field name is validated against the entity's allow-list before use.
/// </summary>
/// <param name="Field">Client-facing field name (e.g. <c>name</c>), matched case-insensitively against the sort allow-list.</param>
/// <param name="Direction">Ascending or descending.</param>
public sealed record SortField(string Field, SortDirection Direction = SortDirection.Ascending);

/// <summary>
/// A single requested filter term: field, operator and the raw string value (parsed to the
/// target property type when applied). The field + operator pair is validated against the
/// entity's filter allow-list before use.
/// </summary>
/// <param name="Field">Client-facing field name, matched case-insensitively against the filter allow-list.</param>
/// <param name="Operator">One of the allow-listed <see cref="FilterOperator"/> values.</param>
/// <param name="Value">The comparison value as a string; converted to the property's CLR type when applied.</param>
public sealed record FilterTerm(string Field, FilterOperator Operator, string Value);

/// <summary>
/// Standard list parameters accepted by every list endpoint (Req 9.3–9.5). This is a plain,
/// transport-agnostic request model — controllers / minimal APIs bind the query string into it
/// (see <c>QueryStringListParametersBinder</c> helpers) and hand it to the shared query helper.
///
/// Nothing here is trusted blindly: <see cref="Page"/>/<see cref="PageSize"/> are clamped against
/// configured bounds, and every <see cref="Sort"/>/<see cref="Filters"/>/<see cref="Search"/>
/// target is checked against the entity's allow-list before it ever touches a query.
/// </summary>
public sealed class ListQueryParameters
{
    /// <summary>1-based page index. Values &lt; 1 are normalized to 1 by the helper.</summary>
    public int? Page { get; init; }

    /// <summary>
    /// Requested page size. Null/≤0 falls back to the configured default; values above the
    /// configured maximum are clamped down to that maximum (Req 9.4).
    /// </summary>
    public int? PageSize { get; init; }

    /// <summary>Free-text search applied across the entity's allow-listed searchable fields (contains).</summary>
    public string? Search { get; init; }

    /// <summary>Ordered sort terms. Applied in order; a deterministic tie-break is always appended.</summary>
    public IReadOnlyList<SortField> Sort { get; init; } = [];

    /// <summary>Filter terms, AND-combined. Each is validated against the entity's filter allow-list.</summary>
    public IReadOnlyList<FilterTerm> Filters { get; init; } = [];
}
