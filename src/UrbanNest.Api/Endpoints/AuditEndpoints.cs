using UrbanNest.Api.Security;
using UrbanNest.Application.Abstractions.Security;
using UrbanNest.Domain.Identity;
using UrbanNest.Application.Audit;
using UrbanNest.Application.Querying;
using UrbanNest.Contracts.Envelope;

namespace UrbanNest.Api.Endpoints;


public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/audit-logs", async (
                string? action,
                string? resourceType,
                string? resourceId,
                string? search,
                string? sort,
                int? page,
                int? pageSize,
                IAuditService audit,
                CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                if (!string.IsNullOrWhiteSpace(action))
                {
                    filters.Add(new FilterTerm("action", FilterOperator.Equals, action));
                }
                if (!string.IsNullOrWhiteSpace(resourceType))
                {
                    filters.Add(new FilterTerm("resourceType", FilterOperator.Equals, resourceType));
                }
                if (!string.IsNullOrWhiteSpace(resourceId))
                {
                    filters.Add(new FilterTerm("resourceId", FilterOperator.Equals, resourceId));
                }

                var parameters = new ListQueryParameters
                {
                    Page = page,
                    PageSize = pageSize,
                    Search = string.IsNullOrWhiteSpace(search) ? null : search,
                    Filters = filters,
                    Sort = ParseSort(sort),
                };

                var data = await audit.QueryAsync(parameters, ct);
                return Results.Ok(ApiResult<PagedData<AuditLogEntry>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Audit"));

        return app;
    }

    // "field" or "field:desc"; unknown fields are rejected downstream by the allow-list, so this
    // just shapes the request — it never trusts the value into a query by itself.
    private static IReadOnlyList<SortField> ParseSort(string? sort)
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
