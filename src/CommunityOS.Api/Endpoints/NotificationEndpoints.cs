using CommunityOS.Api.Security;
using CommunityOS.Application.Notifications;
using CommunityOS.Application.Querying;
using CommunityOS.Contracts.Envelope;

namespace CommunityOS.Api.Endpoints;

/// <summary>
/// In-app notification INBOX HTTP surface (Task 20261008) over <see cref="INotificationInboxService"/>.
/// The rows are the SAME transactional-outbox rows the common helper
/// (<see cref="INotificationService.EnqueueAsync"/>) writes for every feature — this just exposes a
/// recipient's own notifications as a readable list + read-state.
///
/// <para><b>Authorization.</b> These endpoints are AUTHENTICATED-ONLY (<c>.RequireAuthorization()</c>)
/// with NO resource-permission gate: every user reads/clears their OWN notifications, which the
/// service pins to <c>RecipientUserId == </c> the token's user id (self-scoped, Req 11.3, 64.6). An
/// anonymous caller gets 401; there is no way to read another user's notifications.</para>
/// </summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notifications");

        // GET /api/v1/notifications?page=&pageSize=&unreadOnly= — the signed-in user's notifications,
        // newest-first, paginated. Self-scoped by RecipientUserId in the service.
        group.MapGet("/", async (
                int? page,
                int? pageSize,
                bool? unreadOnly,
                INotificationInboxService inbox,
                CancellationToken ct) =>
            {
                var parameters = new ListQueryParameters { Page = page, PageSize = pageSize };
                var data = await inbox.ListAsync(parameters, unreadOnly ?? false, ct);
                return Results.Ok(ApiResult<PagedData<NotificationInboxItemDto>>.Ok(data, string.Empty));
            })
            .RequireAuthorization();

        // GET /api/v1/notifications/unread-count — the bell badge count.
        group.MapGet("/unread-count", async (
                INotificationInboxService inbox,
                CancellationToken ct) =>
            {
                var count = await inbox.UnreadCountAsync(ct);
                return Results.Ok(ApiResult<UnreadCountResponse>.Ok(new UnreadCountResponse(count), string.Empty));
            })
            .RequireAuthorization();

        // POST /api/v1/notifications/{id}/read — mark one of the caller's notifications read (idempotent).
        group.MapPost("/{id:guid}/read", async (
                Guid id,
                INotificationInboxService inbox,
                CancellationToken ct) =>
            {
                await inbox.MarkReadAsync(id, ct);
                return Results.Ok(ApiResult<MarkReadResponse>.Ok(new MarkReadResponse(id), string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .RequireAuthorization();

        // POST /api/v1/notifications/read-all — mark ALL the caller's unread notifications read.
        group.MapPost("/read-all", async (
                INotificationInboxService inbox,
                CancellationToken ct) =>
            {
                var updated = await inbox.MarkAllReadAsync(ct);
                return Results.Ok(ApiResult<ReadAllResponse>.Ok(new ReadAllResponse(updated), string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .RequireAuthorization();

        // DELETE /api/v1/notifications/{id} — SOFT-delete one of the caller's notifications (IsDeleted;
        // retained in the table, hidden from the inbox). Idempotent.
        group.MapDelete("/{id:guid}", async (
                Guid id,
                INotificationInboxService inbox,
                CancellationToken ct) =>
            {
                await inbox.DeleteAsync(id, ct);
                return Results.Ok(ApiResult<DeleteResponse>.Ok(new DeleteResponse(id), string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .RequireAuthorization();

        // DELETE /api/v1/notifications — SOFT-delete ALL the caller's notifications (clear inbox).
        group.MapDelete("/", async (
                INotificationInboxService inbox,
                CancellationToken ct) =>
            {
                var deleted = await inbox.DeleteAllAsync(ct);
                return Results.Ok(ApiResult<DeleteAllResponse>.Ok(new DeleteAllResponse(deleted), string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .RequireAuthorization();

        return app;
    }

    public sealed record UnreadCountResponse(int UnreadCount);
    public sealed record MarkReadResponse(Guid Id);
    public sealed record ReadAllResponse(int Updated);
    public sealed record DeleteResponse(Guid Id);
    public sealed record DeleteAllResponse(int Deleted);
}
