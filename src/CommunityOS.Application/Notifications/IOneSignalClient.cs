namespace CommunityOS.Application.Notifications;

/// <summary>
/// Thin seam over the OneSignal REST API that the Task 7.2 worker's dispatcher calls to actually
/// send a push. <b>Task 7.1 defines this interface only; Task 7.2 implements the HTTP call.</b>
/// It is separate from <c>INotificationDispatcher</c> so the dispatcher owns the outbox/retry
/// orchestration while this client owns just the provider wire call — making the OneSignal
/// dependency a single, swappable adapter.
///
/// <para><b>Security.</b> The OneSignal app id and REST API key the implementation uses MUST come
/// from configuration/secrets (never source, steering), and the implementation MUST NOT log the
/// key or notification PII.</para>
/// </summary>
public interface IOneSignalClient
{
    /// <summary>
    /// Send a push to the given OneSignal player ids (Task 7.2). Returns a result the dispatcher
    /// turns into a <see cref="DispatchResult"/>. Implementations surface transport/provider errors
    /// as a non-success result rather than throwing, so the worker can retry per policy (Req 11.4).
    /// </summary>
    Task<OneSignalSendResult> SendPushAsync(
        OneSignalPushRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The provider-agnostic push payload the Task 7.2 worker builds from a drained outbox message.
/// Carries only data scoped to the intended recipient (Req 11.3, 64.6).
/// </summary>
/// <param name="PlayerIds">The intended recipient's resolved OneSignal player ids.</param>
/// <param name="Title">Rendered title.</param>
/// <param name="Body">Rendered body.</param>
/// <param name="DataJson">Optional structured data payload (deep-link/resource ids).</param>
public sealed record OneSignalPushRequest(
    IReadOnlyCollection<string> PlayerIds,
    string Title,
    string Body,
    string? DataJson = null);

/// <summary>
/// Result of a OneSignal send call (Task 7.2). <paramref name="Transient"/> classifies a non-accepted
/// result so the dispatcher/worker knows whether a retry is worthwhile: provider 5xx / 429 / network
/// / timeout are transient (retry with backoff), other 4xx are permanent (fail fast). Only meaningful
/// when <paramref name="Accepted"/> is false.
/// </summary>
/// <param name="Accepted">True when OneSignal accepted the request.</param>
/// <param name="ProviderMessageId">Provider-assigned id when accepted (for correlation).</param>
/// <param name="Error">Error summary when not accepted (diagnostic only; no PII).</param>
/// <param name="Transient">When not accepted, true if a retry may succeed; false if permanent.</param>
public sealed record OneSignalSendResult(
    bool Accepted,
    string? ProviderMessageId = null,
    string? Error = null,
    bool Transient = false);
