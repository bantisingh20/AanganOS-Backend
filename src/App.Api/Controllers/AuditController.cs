using App.Api.Security;
using App.Application.Audit;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

[ApiController]
[Route("api/v1/audit-logs")]
public sealed class AuditController(IAuditService audit) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionAction.View, "Audit")]
    public async Task<IActionResult> List(
        [FromQuery] string? action,
        [FromQuery] string? resourceType,
        [FromQuery] string? resourceId,
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
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
        return Ok(ApiResult<PagedData<AuditLogEntry>>.Ok(data, string.Empty));
    }

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
