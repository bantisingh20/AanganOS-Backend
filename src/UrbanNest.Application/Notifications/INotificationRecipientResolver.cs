namespace UrbanNest.Application.Notifications;

/// <summary>
/// Resolves an intended recipient user into the OneSignal player ids of their own active devices
/// (Req 11.3, 64.6). This is the confidentiality boundary for push targeting: given a single
/// recipient user id it returns only <i>that user's</i> device targets, so a notification can never
/// be routed to another resident's device.
///
/// <para>Used by the Task 7.2 worker at dispatch time (so a device registered after enqueue is
/// still reached). It is defined in Task 7.1 because recipient resolution is a Notification_Service
/// concern and the service owns the <c>Device</c> store; the worker consumes it.</para>
/// </summary>
public interface INotificationRecipientResolver
{
    /// <summary>
    /// Return the active OneSignal player ids registered to <paramref name="recipientUserId"/>
    /// (Req 11.3). Empty when the user has no registered device — the caller then records a
    /// non-delivery rather than broadcasting.
    /// </summary>
    Task<IReadOnlyCollection<string>> ResolvePlayerIdsAsync(
        Guid recipientUserId,
        CancellationToken cancellationToken = default);
}
