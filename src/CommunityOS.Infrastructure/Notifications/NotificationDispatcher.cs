using CommunityOS.Application.Notifications;
using CommunityOS.Domain.Notifications;

namespace CommunityOS.Infrastructure.Notifications;

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

        // Phase 1: push only. TODO (Req 64, later phases): fan out to SMS/email/WhatsApp dispatchers
        // for the other bits set in message.Channels.
        if (!message.Channels.HasFlag(NotificationChannel.Push))
        {
            // Nothing dispatchable in Phase 1 for a non-push-only message; don't spin retries on it.
            return DispatchResult.PermanentFailure("No push channel on message (Phase 1 dispatches push only)");
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
