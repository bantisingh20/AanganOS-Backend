using UrbanNest.Domain.Notifications;

namespace UrbanNest.Application.Notifications;

/// <summary>
/// The seam the Task 7.2 background worker implements to actually deliver a drained outbox message
/// through a provider (OneSignal for push). <b>Task 7.1 defines this interface but does NOT
/// implement the HTTP call</b> — enqueue only writes the outbox row. Keeping dispatch behind this
/// interface means Notification_Service and the outbox schema never depend on OneSignal, and a
/// later SMS/email/WhatsApp provider (Req 64) plugs in as another dispatcher without touching the
/// enqueue path.
///
/// <para><b>Confidentiality (Req 11.3, 64.6).</b> The dispatcher receives a message already
/// resolved to its single intended recipient and must deliver only to that recipient's resolved
/// device targets — never broadcast.</para>
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>
    /// Deliver one outbox message to its intended recipient's device targets (Task 7.2). Returns a
    /// result the worker uses to mark the row Sent or schedule a retry (Req 11.4). Implementations
    /// must not throw for a provider error — they report it in the result so the worker records the
    /// failure and retries per policy without crashing the drain loop.
    /// </summary>
    Task<DispatchResult> DispatchAsync(
        NotificationOutboxMessage message,
        IReadOnlyCollection<string> playerIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of a single dispatch attempt (consumed by the Task 7.2 retry loop, Req 11.4).
///
/// <para><b>Transient vs permanent.</b> The worker uses <see cref="Transient"/> to decide whether a
/// failure is worth another attempt. A transient failure (provider 5xx, 429, network/timeout) is
/// retried with backoff until the configured max attempts; a permanent failure (provider 4xx such
/// as a bad request or auth error, or a non-recoverable condition like no registered device) is
/// marked Failed immediately because retrying cannot help (Req 11.4). <see cref="Transient"/> is
/// only meaningful when <see cref="Delivered"/> is false.</para>
/// </summary>
/// <param name="Delivered">True when the provider accepted the message.</param>
/// <param name="Error">Provider error summary when not delivered (diagnostic only; no PII).</param>
/// <param name="Transient">
/// When not delivered, true if the failure may succeed on retry (retry per policy); false if it is
/// permanent (fail fast). Ignored when <paramref name="Delivered"/> is true.
/// </param>
public sealed record DispatchResult(bool Delivered, string? Error = null, bool Transient = false)
{
    public static DispatchResult Success() => new(true);

    /// <summary>A retryable failure — the worker schedules another attempt with backoff (Req 11.4).</summary>
    public static DispatchResult TransientFailure(string error) => new(false, error, Transient: true);

    /// <summary>A non-retryable failure — the worker marks the message Failed immediately (Req 11.4).</summary>
    public static DispatchResult PermanentFailure(string error) => new(false, error, Transient: false);

    /// <summary>Back-compat alias for <see cref="TransientFailure"/> (default: treat as retryable).</summary>
    public static DispatchResult Failure(string error) => TransientFailure(error);
}
