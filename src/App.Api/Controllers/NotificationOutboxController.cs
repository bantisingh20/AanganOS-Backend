using App.Api.Endpoints;
using App.Api.Security;
using App.Application.Notifications;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

[ApiController]
[Route("api/v1/notification-outbox")]
public sealed class NotificationOutboxController(INotificationOutboxQueryService notifications) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionAction.View, "Audit")]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? category,
        [FromQuery] string? templateKey,
        [FromQuery] string? recipientUserId,
        [FromQuery] string? fromDate,
        [FromQuery] string? toDate,
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        if (!string.IsNullOrWhiteSpace(status))
        {
            filters.Add(new FilterTerm("status", FilterOperator.Equals, status));
        }
        if (!string.IsNullOrWhiteSpace(category))
        {
            filters.Add(new FilterTerm("category", FilterOperator.Equals, category));
        }
        if (!string.IsNullOrWhiteSpace(templateKey))
        {
            filters.Add(new FilterTerm("templateKey", FilterOperator.Equals, templateKey));
        }
        if (!string.IsNullOrWhiteSpace(recipientUserId))
        {
            filters.Add(new FilterTerm("recipientUserId", FilterOperator.Equals, recipientUserId));
        }
        if (!string.IsNullOrWhiteSpace(fromDate))
        {
            filters.Add(new FilterTerm("createdAt", FilterOperator.GreaterThanOrEqual, fromDate));
        }
        if (!string.IsNullOrWhiteSpace(toDate))
        {
            filters.Add(new FilterTerm("createdAt", FilterOperator.LessThanOrEqual, toDate));
        }

        var parameters = new ListQueryParameters
        {
            Page = page,
            PageSize = pageSize,
            Search = string.IsNullOrWhiteSpace(search) ? null : search,
            Filters = filters,
            Sort = EndpointSort.Parse(sort),
        };

        var data = await notifications.QueryAsync(parameters, ct);
        return Ok(ApiResult<PagedData<NotificationOutboxLogDto>>.Ok(data, string.Empty));
    }
}
