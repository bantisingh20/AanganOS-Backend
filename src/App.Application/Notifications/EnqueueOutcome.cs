namespace App.Application.Notifications;

/// <summary>
/// The result of <c>INotificationService.EnqueueAsync</c>: whether an outbox row was staged for
/// dispatch, suppressed by the resident's preferences, or skipped because the template was unknown.
/// Returned (not thrown) so a notification decision never alters the control flow of the business
/// operation that requested it (Req 11.5 — notifications must not block/break the originating op).
/// </summary>
public enum EnqueueOutcome
{
    /// <summary>A Pending outbox row was staged in the caller's transaction and will dispatch on commit.</summary>
    Queued = 0,

    /// <summary>Not queued: the resident opted out of this optional category (Req 20.3).</summary>
    SuppressedByPreference = 1,

    /// <summary>Not queued: no active template exists for the requested key (misconfiguration).</summary>
    TemplateNotFound = 2,
}
