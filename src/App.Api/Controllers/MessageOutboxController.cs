using App.Api.Endpoints;
using App.Api.Security;
using App.Application.Messaging;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

[ApiController]
[Route("api/v1/message-outbox")]
public sealed class MessageOutboxController(IMessageOutboxQueryService messages) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionAction.View, "Audit")]
    public async Task<IActionResult> List(
        [FromQuery] string? channel,
        [FromQuery] string? status,
        [FromQuery] string? recipient,
        [FromQuery] string? fromDate,
        [FromQuery] string? toDate,
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        if (!string.IsNullOrWhiteSpace(channel))
        {
            filters.Add(new FilterTerm("channel", FilterOperator.Equals, channel));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            filters.Add(new FilterTerm("status", FilterOperator.Equals, status));
        }
        if (!string.IsNullOrWhiteSpace(recipient))
        {
            filters.Add(new FilterTerm("recipient", FilterOperator.Contains, recipient));
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

        var data = await messages.QueryAsync(parameters, ct);
        return Ok(ApiResult<PagedData<MessageOutboxLogDto>>.Ok(data, string.Empty));
    }
}
