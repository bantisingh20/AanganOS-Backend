namespace CommunityOS.Application.Notifications;

/// <summary>
/// Notification_Service (Req 11, 20.3–20.4, 64.6). Phase 1 Task 7.1 scope: resolve a named,
/// reusable template (Req 11.1), apply the resident's communication preferences (Req 20.3, 20.4),
/// render it for the single intended recipient (Req 11.3, 64.6), and write a <b>transactional
/// outbox</b> row inside the caller's business transaction (Req 11.5). It does <b>not</b> call
/// OneSignal — the Task 7.2 background worker drains the outbox and dispatches asynchronously, so a
/// delivery failure can never block or roll back the originating business operation.
///
/// <para><b>Transactional outbox contract.</b> <see cref="EnqueueAsync"/> only <i>Adds</i> the
/// outbox row to the shared request-scoped <c>DbContext</c>; it does NOT call <c>SaveChanges</c>.
/// The caller's existing <c>SaveChanges</c>/transaction commits the business change and the outbox
/// row together — atomically. If the business operation rolls back, no notification is queued; if
/// it commits, the notification is guaranteed to be picked up (at-least-once).</para>
///
/// <para><b>Confidentiality.</b> Each request targets exactly one recipient and the resolved
/// message is scoped to that recipient's own data and devices — a notification is never fanned out
/// to, or made visible to, an unauthorized recipient (Req 11.3, 64.6).</para>
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Stage a template-based notification for the request's single intended recipient (Req 11.1,
    /// 11.2). Resolves the template (community override → platform default), applies the preference
    /// check (optional categories require opt-in, Req 20.3; mandatory account/security bypass it,
    /// Req 20.4), renders the title/body, and <b>Adds a Pending outbox row to the current
    /// DbContext without saving</b> so it commits with the caller's business transaction (Req 11.5).
    /// Returns the <see cref="EnqueueOutcome"/>; never throws for an opted-out resident or an
    /// unknown template, so a notification decision cannot break the business operation.
    /// </summary>
    Task<EnqueueOutcome> EnqueueAsync(NotificationRequest request, CancellationToken cancellationToken = default);
}
