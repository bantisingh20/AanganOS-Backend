using UrbanNest.Application.Abstractions;
using UrbanNest.Application.Notifications;
using UrbanNest.Domain.Notifications;
using UrbanNest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace UrbanNest.Worker;

/// <summary>
/// Background drain for the transactional notification outbox (Phase 1 Task 7.2, Req 11.4, 11.5,
/// 62.3, 63.6, 65.4). It periodically pulls Pending rows that are due, dispatches each through
/// <see cref="INotificationDispatcher"/> (OneSignal push), and updates the row: Sent on success,
/// a retry schedule on a transient failure, Failed on a permanent failure or once the configured
/// max attempts are exhausted.
///
/// <para><b>Non-blocking (Req 11.5).</b> Dispatch happens only here, out of band from the business
/// request that enqueued the row. A delivery failure can never roll back or stall the originating
/// operation; the worker simply records it and retries later.</para>
///
/// <para><b>Scope-per-batch (Req 65.4).</b> The hosted service is a singleton, but a short-lived DI
/// scope is created for each drain pass so the <see cref="AppDbContext"/> and the scoped dispatcher
/// services are fresh per batch — never a long-lived singleton context accumulating tracked entities
/// across the worker's lifetime.</para>
///
/// <para><b>Bounded + due-only query.</b> Each pass reads at most <c>BatchSize</c> rows
/// (<c>WHERE Status = Pending AND (NextAttemptUtc IS NULL OR NextAttemptUtc &lt;= now)</c>) ordered
/// by <see cref="NotificationOutboxMessage.CreatedAtUtc"/> (oldest first, fair ordering). It keeps
/// draining back-to-back while full batches come back and only sleeps once a short batch signals the
/// queue is drained — so latency stays low under load without unbounded memory use.</para>
///
/// <para><b>Retry policy (Req 11.4).</b> Max attempts and the base backoff come from
/// <see cref="NotificationOptions"/> (config, not hardcoded). Backoff is exponential:
/// <c>base * 2^(attempts-1)</c> after each failed attempt, capped to avoid overflow.</para>
///
/// <para><b>Logging.</b> Delivery failures are logged with the message id as the correlation handle
/// (Req 11.4). Rendered title/body (potential PII) and the OneSignal API key are never logged.</para>
/// </summary>
public sealed class OutboxWorkerService(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationOptions> notificationOptions,
    IOptions<NotificationWorkerOptions> workerOptions,
    ILogger<OutboxWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(workerOptions.Value.PollIntervalSeconds);
        logger.LogInformation(
            "Notification outbox worker started (batch size {BatchSize}, poll {PollSeconds}s, max attempts {MaxAttempts}).",
            workerOptions.Value.BatchSize,
            workerOptions.Value.PollIntervalSeconds,
            notificationOptions.Value.MaxDeliveryAttempts);

        while (!stoppingToken.IsCancellationRequested)
        {
            int processed;
            try
            {
                processed = await DrainBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A batch-level failure (e.g. DB unavailable) must not kill the worker. Log and
                // back off; the next pass retries. The business side is unaffected either way.
                logger.LogError(ex, "Notification outbox drain pass failed; backing off.");
                processed = 0;
            }

            // Only sleep when the queue is (likely) drained. A full batch means there may be more
            // work, so loop immediately to keep delivery latency low under load.
            if (processed < workerOptions.Value.BatchSize)
            {
                await DelaySafelyAsync(pollInterval, stoppingToken);
            }
        }

        logger.LogInformation("Notification outbox worker stopping.");
    }

    /// <summary>
    /// Drain one batch inside a fresh DI scope. Returns the number of rows processed so the caller
    /// knows whether to keep draining or sleep.
    /// </summary>
    private async Task<int> DrainBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<AppDbContext>();
        var dispatcher = sp.GetRequiredService<INotificationDispatcher>();
        var recipientResolver = sp.GetRequiredService<INotificationRecipientResolver>();
        var clock = sp.GetRequiredService<IClock>();

        var now = clock.UtcNow;
        var batchSize = workerOptions.Value.BatchSize;

        var due = await db.Set<NotificationOutboxMessage>()
            .Where(m => m.Status == OutboxStatus.Pending &&
                        (m.NextAttemptUtc == null || m.NextAttemptUtc <= now))
            .OrderBy(m => m.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
        {
            return 0;
        }

        foreach (var message in due)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await ProcessMessageAsync(message, dispatcher, recipientResolver, clock, cancellationToken);
            // Persist per message so one poisoned row (or a mid-batch shutdown) never discards the
            // progress already made on the rest of the batch.
            await db.SaveChangesAsync(cancellationToken);
        }

        return due.Count;
    }

    /// <summary>
    /// Resolve the recipient's devices, dispatch, and mutate the message's status/attempts/next-attempt
    /// per the retry policy. Never throws for a dispatch failure — it records it on the row.
    /// </summary>
    private async Task ProcessMessageAsync(
        NotificationOutboxMessage message,
        INotificationDispatcher dispatcher,
        INotificationRecipientResolver recipientResolver,
        IClock clock,
        CancellationToken cancellationToken)
    {
        message.Attempts++;

        // An explicit TargetPlayerId pins delivery to one device (e.g. "signed out elsewhere" to a
        // device being deregistered in the same operation, which the user-scoped resolver would no
        // longer return). Otherwise resolve the recipient's current active devices (the normal path).
        var playerIds = string.IsNullOrWhiteSpace(message.TargetPlayerId)
            ? await recipientResolver.ResolvePlayerIdsAsync(message.RecipientUserId, cancellationToken)
            : new[] { message.TargetPlayerId };
        var result = await dispatcher.DispatchAsync(message, playerIds, cancellationToken);

        if (result.Delivered)
        {
            message.Status = OutboxStatus.Sent;
            message.NextAttemptUtc = null;
            message.LastError = null;
            return;
        }

        message.LastError = result.Error;

        var maxAttempts = notificationOptions.Value.MaxDeliveryAttempts;
        var exhausted = message.Attempts >= maxAttempts;

        if (!result.Transient || exhausted)
        {
            // Permanent failure, or we've burned the configured attempts — stop retrying (Req 11.4).
            message.Status = OutboxStatus.Failed;
            message.NextAttemptUtc = null;
            logger.LogWarning(
                "Notification {MessageId} marked Failed after {Attempts}/{MaxAttempts} attempt(s): {Reason}. Error: {Error}",
                message.Id,
                message.Attempts,
                maxAttempts,
                result.Transient ? "attempts exhausted" : "permanent failure",
                result.Error);
            return;
        }

        // Transient failure with attempts remaining — schedule an exponential-backoff retry.
        message.Status = OutboxStatus.Pending;
        message.NextAttemptUtc = clock.UtcNow.Add(ComputeBackoff(message.Attempts));
        logger.LogWarning(
            "Notification {MessageId} delivery failed (attempt {Attempts}/{MaxAttempts}); retrying at {NextAttemptUtc:o}. Error: {Error}",
            message.Id,
            message.Attempts,
            maxAttempts,
            message.NextAttemptUtc,
            result.Error);
    }

    /// <summary>
    /// Exponential backoff from the configured base: <c>base * 2^(attempts-1)</c>. Capped at ~7 days
    /// so the shift never overflows and a stuck message can't be scheduled absurdly far out.
    /// </summary>
    private TimeSpan ComputeBackoff(int attempts)
    {
        var baseSeconds = notificationOptions.Value.RetryBackoffSeconds;
        var exponent = Math.Min(attempts - 1, 20); // cap the shift to avoid overflow
        var factor = 1L << exponent;
        var seconds = Math.Min(baseSeconds * (double)factor, TimeSpan.FromDays(7).TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    private static async Task DelaySafelyAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested during the wait — fine, the loop condition will exit.
        }
    }
}
