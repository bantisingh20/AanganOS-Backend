using CommunityOS.Application.Querying;
using CommunityOS.Contracts.Envelope;

namespace CommunityOS.Application.Notifications;

/// <summary>
/// Read side of the notifications feature — the in-app INBOX (Task 20261008). The same transactional
/// outbox rows written by the ONE common helper (<see cref="INotificationService.EnqueueAsync"/>,
/// called by every feature — walk-in register/approve, tickets, parcels, SLA, …) are surfaced here
/// as the signed-in user's readable notification list. There is NO per-feature save path: a feature
/// enqueues once via the common helper and it automatically appears in that recipient's inbox.
///
/// <para><b>Self-scoped.</b> Every method resolves the current user from the request context and
/// returns ONLY rows whose <c>RecipientUserId</c> is that user — a user can only ever read/clear
/// their own notifications (Req 11.3, 64.6). No resource permission gate; just authentication.</para>
/// </summary>
public interface INotificationInboxService
{
    /// <summary>The signed-in user's notifications, newest-first, paginated. <paramref name="unreadOnly"/> filters to unread.</summary>
    Task<PagedData<NotificationInboxItemDto>> ListAsync(ListQueryParameters parameters, bool unreadOnly = false, CancellationToken cancellationToken = default);

    /// <summary>Count of the signed-in user's unread notifications (drives the bell badge).</summary>
    Task<int> UnreadCountAsync(CancellationToken cancellationToken = default);

    /// <summary>Mark one of the signed-in user's notifications read. Unknown/foreign id → no-op (idempotent).</summary>
    Task MarkReadAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Mark ALL of the signed-in user's unread notifications read. Returns how many were updated.</summary>
    Task<int> MarkAllReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// SOFT-DELETE one of the signed-in user's notifications (sets <c>IsDeleted</c> — never a hard
    /// delete, Req 3.7). The row stays in the table but the global soft-delete filter hides it from
    /// the inbox. Unknown/foreign id → no-op (idempotent).
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Soft-delete ALL of the signed-in user's notifications. Returns how many were deleted.</summary>
    Task<int> DeleteAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>One in-app notification for the signed-in recipient (a readable projection of an outbox row).</summary>
/// <param name="Id">The notification id.</param>
/// <param name="Category">Grouping category (e.g. visitor, complaint, parcel) for icon/filtering.</param>
/// <param name="Title">Rendered title.</param>
/// <param name="Body">Rendered body.</param>
/// <param name="PayloadJson">Optional structured payload (e.g. a resource id for deep-linking), or null.</param>
/// <param name="IsRead">Whether the recipient has read it.</param>
/// <param name="CreatedAtUtc">When it was raised.</param>
public sealed record NotificationInboxItemDto(
    Guid Id,
    string Category,
    string Title,
    string Body,
    string? PayloadJson,
    bool IsRead,
    DateTime CreatedAtUtc);
