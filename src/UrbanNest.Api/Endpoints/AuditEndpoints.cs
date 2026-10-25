using UrbanNest.Api.Security;
using UrbanNest.Application.Abstractions.Security;
using UrbanNest.Domain.Identity;
using UrbanNest.Application.Audit;
using UrbanNest.Application.Querying;
using UrbanNest.Contracts.Envelope;

namespace UrbanNest.Api.Endpoints;

/// <summary>
/// Read surface over the append-only Audit_Service (Task 4.1). There is intentionally no write
/// endpoint — audit entries are produced by services calling <see cref="IAuditService.AppendAsync"/>
/// on their own mutations, never by a client (append-only, Req 6.3).
///
/// <para><c>GET /api/v1/audit-logs</c> requires the <see cref="PermissionAction.View"/> permission
/// on the <c>Audit</c> resource (Req 6.5) via the Task 5.3 authorization seam, and returns a
/// tenant-scoped, paginated page through the shared pagination infra: a community admin sees only
/// their authorized communities' entries; a platform super admin sees across (scoping is enforced
/// inside the service). Optional <c>action</c>/<c>resourceType</c>/<c>resourceId</c> filters,
/// <c>search</c>, and <c>sort</c> (<c>timestamp|action|resourceType</c>, with <c>:desc</c>) narrow
/// the result; results default to newest-first.</para>
/// </summary>
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
