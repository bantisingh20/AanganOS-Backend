using App.Api.Security;
using App.Application.Notifications;
using App.Application.Querying;
using App.Contracts.Envelope;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
[Route("api/v1/notifications")]
[Authorize]
public sealed class NotificationsController(INotificationInboxService inbox) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] bool? unreadOnly,
        CancellationToken ct)
    {
        var parameters = new ListQueryParameters { Page = page, PageSize = pageSize };
        var data = await inbox.ListAsync(parameters, unreadOnly ?? false, ct);
        return Ok(ApiResult<PagedData<NotificationInboxItemDto>>.Ok(data, string.Empty));
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken ct)
    {
        var count = await inbox.UnreadCountAsync(ct);
        return Ok(ApiResult<UnreadCountResponse>.Ok(new UnreadCountResponse(count), string.Empty));
    }

    [HttpPost("{id:guid}/read")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await inbox.MarkReadAsync(id, ct);
        return Ok(ApiResult<MarkReadResponse>.Ok(new MarkReadResponse(id), string.Empty));
    }

    [HttpPost("read-all")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        var updated = await inbox.MarkAllReadAsync(ct);
        return Ok(ApiResult<ReadAllResponse>.Ok(new ReadAllResponse(updated), string.Empty));
    }

    [HttpDelete("{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await inbox.DeleteAsync(id, ct);
        return Ok(ApiResult<DeleteResponse>.Ok(new DeleteResponse(id), string.Empty));
    }

    [HttpDelete]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    public async Task<IActionResult> DeleteAll(CancellationToken ct)
    {
        var deleted = await inbox.DeleteAllAsync(ct);
        return Ok(ApiResult<DeleteAllResponse>.Ok(new DeleteAllResponse(deleted), string.Empty));
    }

    public sealed record UnreadCountResponse(int UnreadCount);
    public sealed record MarkReadResponse(Guid Id);
    public sealed record ReadAllResponse(int Updated);
    public sealed record DeleteResponse(Guid Id);
    public sealed record DeleteAllResponse(int Deleted);
}
