using App.Application.Notifications;
using App.Domain.Notifications;

namespace App.Infrastructure.Notifications;

/// <summary>
/// Default <see cref="INotificationDispatcher"/> for Phase 1: delivers a drained outbox message to
/// its single intended recipient's devices through <see cref="IOneSignalClient"/> (push).
///
/// <para><b>Flow.</b> The worker has already resolved the recipient's player ids (via
/// <see cref="INotificationRecipientResolver"/>) and passes them in. If the set is empty the
/// recipient has no registered device, so there is nothing to deliver to — a <b>permanent</b>
/// failure (retrying cannot conjure a device), which the worker counts toward max attempts and then
/// marks Failed rather than retrying forever (Req 11.4). Otherwise it calls OneSignal and maps the
/// provider result to a <see cref="DispatchResult"/>, preserving the transient/permanent
/// classification so the worker retries only what is worth retrying.</para>
///
/// <para><b>Confidentiality (Req 11.3, 64.6).</b> It sends only to the player ids resolved for the
/// message's own <see cref="NotificationOutboxMessage.RecipientUserId"/> — never a broadcast.</para>
///
/// <para><b>Channels.</b> Phase 1 handles <see cref="NotificationChannel.Push"/> only. SMS, email and
/// WhatsApp (Req 64) are left as TODOs for later phases; the message already carries the full
/// <see cref="NotificationOutboxMessage.Channels"/> matrix so those dispatchers plug in without a
/// schema change.</para>
/// </summary>
public sealed class NotificationDispatcher(IOneSignalClient oneSignalClient) : INotificationDispatcher
{
    public async Task<DispatchResult> DispatchAsync(
        NotificationOutboxMessage message,
        IReadOnlyCollection<string> playerIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(playerIds);

        if (playerIds.Count == 0)
        {
            // No device to reach. Permanent: another attempt cannot help. The worker still bounds
            // this via max attempts so a message never retries forever (Req 11.4).
            return DispatchResult.PermanentFailure("No active device registered for recipient");
        }

        // Push is dispatched here; email/SMS/WhatsApp are fanned out to the MessageOutbox at enqueue
        // time (NotificationService). A message with no Push bit has nothing to do here — treat it as
        // a successful no-op so it isn't left spinning as a failure in the push outbox.
        if (!message.Channels.HasFlag(NotificationChannel.Push))
        {
            return DispatchResult.Success();
        }

        var result = await oneSignalClient.SendPushAsync(
            new OneSignalPushRequest(playerIds, message.Title, message.Body, message.PayloadJson),
            cancellationToken);

        if (result.Accepted)
        {
            return DispatchResult.Success();
        }

        var error = result.Error ?? "Unknown provider error";
        return result.Transient
            ? DispatchResult.TransientFailure(error)
            : DispatchResult.PermanentFailure(error);
    }
}
