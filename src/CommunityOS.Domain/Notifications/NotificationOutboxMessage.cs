using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Notifications;

/// <summary>
/// A transactional-outbox row (Req 11.5). <c>INotificationService.EnqueueAsync</c> writes
/// this row inside the caller's tracked <c>DbContext</c> so the notification is committed
/// atomically with the business change (if SaveChanges succeeds the message is guaranteed to be
/// processed; if it rolls back no stale notification exists). The Task 7.2 background worker drains
/// <see cref="OutboxStatus.Pending"/> rows, dispatches via the <c>INotificationDispatcher</c>
/// seam, updates <see cref="Status"/> + <see cref="Attempts"/> and schedules retries through
/// <see cref="NextAttemptUtc"/>.
///
/// <para><b>Tenant scoping.</b> The outbox message targets a specific recipient user within a
/// specific community. It derives from <see cref="TenantOwnedEntity"/> so the standard tenant
/// filter automatically hides messages belonging to other communities (an admin viewing "sent
/// notifications" only ever sees their own community's rows). The <see cref="TenantOwnedEntity.CommunityId"/>
/// is the originating community; the worker needs cross-tenant access (via super-admin context)
/// to drain all queues — exactly as the SLA/escalation/billing workers do.</para>
///
/// <para><b>Confidentiality (Req 11.3, 64.6).</b> Each row targets exactly one
/// <see cref="RecipientUserId"/>. The worker resolves that user's device(s) and sends only to
/// those player ids; there is no fan-out to other residents, and the <see cref="PayloadJson"/>
/// contains data scoped to the intended recipient only.</para>
/// </summary>
public class NotificationOutboxMessage : TenantOwnedEntity
{
    /// <summary>The template key that identifies what notification this is (Req 11.1).</summary>
    public string TemplateKey { get; set; } = string.Empty;

    /// <summary>
    /// Grouping category copied from the template at enqueue time so the worker can apply
    /// preference/suppression checks without re-resolving the template.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Copied from the template so the worker knows whether to check optional preferences or
    /// always send (Req 20.3, 20.4).
    /// </summary>
    public NotificationCategoryKind CategoryKind { get; set; }

    /// <summary>Channels the template declared at enqueue time (bit flags).</summary>
    public NotificationChannel Channels { get; set; }

    /// <summary>The intended recipient (Req 11.3, 64.6). The worker resolves their device(s).</summary>
    public Guid RecipientUserId { get; set; }

    /// <summary>
    /// Optional explicit push target (a OneSignal player id). When set, the worker delivers to EXACTLY
    /// this device instead of resolving the recipient's current active devices. Needed for a
    /// "signed out elsewhere" notice to a device that is being deregistered in the same operation —
    /// the user-scoped resolver would otherwise no longer target it. Null = resolve by recipient user
    /// (the normal path).
    /// </summary>
    public string? TargetPlayerId { get; set; }

    /// <summary>Rendered title (placeholders already filled at enqueue time).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Rendered body (placeholders already filled at enqueue time).</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Optional structured payload (JSON) the sending module supplied (e.g. resource id, deep-link
    /// data). Scoped to the intended recipient only — never cross-recipient data (Req 11.3, 64.6).
    /// The worker forwards it as the push data payload.
    /// </summary>
    public string? PayloadJson { get; set; }

    /// <summary>Lifecycle status (Req 11.4). Task 7.1 writes <see cref="OutboxStatus.Pending"/>.</summary>
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    /// <summary>Delivery attempts made by the worker. Starts at 0; incremented on each try.</summary>
    public int Attempts { get; set; }

    /// <summary>
    /// When the worker should next attempt dispatch (UTC). Null = eligible now. Retries push this
    /// forward by the configured backoff interval (Req 11.4, exponential or fixed per config).
    /// </summary>
    public DateTime? NextAttemptUtc { get; set; }

    /// <summary>Last error message from a failed dispatch attempt (diagnostic only; never contains PII).</summary>
    public string? LastError { get; set; }

    /// <summary>
    /// In-app inbox read state (Task 20261008). The same outbox row doubles as the recipient's
    /// readable in-app notification: <c>false</c> until the recipient opens/acknowledges it. Drives
    /// the unread badge. Independent of the push <see cref="Status"/> (a push may be Sent while the
    /// in-app item is still unread).
    /// </summary>
    public bool IsRead { get; set; }

    /// <summary>When the recipient marked this notification read (UTC), else null.</summary>
    public DateTime? ReadAtUtc { get; set; }
}
