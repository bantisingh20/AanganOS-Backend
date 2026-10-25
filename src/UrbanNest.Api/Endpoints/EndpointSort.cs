using UrbanNest.Application.Querying;

namespace UrbanNest.Api.Endpoints;

/// <summary>
/// Shared helpers for the RBAC endpoint groups: parsing the <c>sort</c> query parameter into the
/// shared <see cref="SortField"/> shape, and the common delete acknowledgement payload.
/// Centralised here so the five endpoint files don't each re-implement the same parser.
/// </summary>
public static class EndpointSort
{
    /// <summary>
    /// Parse a <c>field</c> or <c>field:desc</c> sort string into a single <see cref="SortField"/>.
    /// This only shapes the request — the field name is validated against the entity's sort
    /// allow-list downstream, so an unknown field is rejected there rather than trusted into a query.
    /// </summary>
    public static IReadOnlyList<SortField> Parse(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return [];
        }

        var parts = sort.Split(':', 2, StringSplitOptions.TrimEntries);
        var direction = parts.Length == 2 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase)
            ? SortDirection.Descending
            : SortDirection.Ascending;
        return [new SortField(parts[0], direction)];
    }
}

/// <summary>Acknowledgement payload for a soft-delete, echoing the archived record's id (Req 3.7).</summary>
public sealed record DeletedResponse(Guid Id);
