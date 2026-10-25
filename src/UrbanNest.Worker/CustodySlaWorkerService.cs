using UrbanNest.Application.Parcel;
using Microsoft.Extensions.Options;

namespace UrbanNest.Worker;

/// <summary>
/// Background sweep for the parcel custody-SLA breach escalation (Task 18.4, Req 31.2, 64.2). It
/// periodically asks <see cref="IParcelService.EscalateOverdueCustodyParcelsAsync"/> to escalate a
/// bounded batch of parcels that have sat in custody past their configurable Parcel_SLA: each gets
/// an append-only <c>sla_escalated</c> timeline entry, a one-time escalation stamp (so it is never
/// double-escalated) and an SLA-exceeded notification to the recipient residents AND the community
/// admin via the transactional outbox (push + email, Req 64.2).
///
/// <para><b>Mirrors the notification outbox worker.</b> Same structure as
/// <see cref="OutboxWorkerService"/>: a singleton hosted service that creates a FRESH DI scope per
/// pass (so the <c>AppDbContext</c> and the scoped <see cref="IParcelService"/> are never a
/// long-lived singleton accumulating tracked entities, Req 65.4), runs under the worker's
/// cross-tenant super-admin context (the sweep legitimately spans all communities), keeps draining
/// back-to-back while full batches come back and only sleeps once a short batch signals the backlog
/// is cleared — low latency without unbounded memory.</para>
///
/// <para><b>Bounded + fail-safe.</b> Each pass escalates at most <c>BatchSize</c> parcels (the
/// service additionally caps the per-call work), and the service saves per parcel so one parcel's
/// concurrency loss is isolated. A pass-level failure (e.g. DB unavailable) is logged and backed off
/// — it never kills the worker, and the business side is unaffected because escalation side-effects
/// ride the outbox and are retried on the next pass.</para>
/// </summary>
public sealed class CustodySlaWorkerService(
    IServiceScopeFactory scopeFactory,
    IOptions<CustodySlaWorkerOptions> workerOptions,
    ILogger<CustodySlaWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(workerOptions.Value.PollIntervalSeconds);
        logger.LogInformation(
            "Parcel custody-SLA worker started (batch size {BatchSize}, poll {PollSeconds}s).",
            workerOptions.Value.BatchSize,
            workerOptions.Value.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            int escalated;
            try
            {
                escalated = await SweepBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A pass-level failure must not kill the worker. Log and back off; the next pass
                // retries. Nothing escalated this pass stays overdue and is picked up next time.
                logger.LogError(ex, "Parcel custody-SLA sweep pass failed; backing off.");
                escalated = 0;
            }

            // Only sleep once the backlog is (likely) cleared. A full batch means there may be more
            // overdue parcels, so loop immediately to keep escalation latency low under load.
            if (escalated < workerOptions.Value.BatchSize)
            {
                await DelaySafelyAsync(pollInterval, stoppingToken);
            }
        }

        logger.LogInformation("Parcel custody-SLA worker stopping.");
    }

    /// <summary>
    /// Run one sweep inside a fresh DI scope. Returns the number of parcels escalated so the caller
    /// knows whether to keep sweeping or sleep.
    /// </summary>
    private async Task<int> SweepBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var parcels = scope.ServiceProvider.GetRequiredService<IParcelService>();
        return await parcels.EscalateOverdueCustodyParcelsAsync(workerOptions.Value.BatchSize, cancellationToken);
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

/// <summary>
/// Tunables for the <see cref="CustodySlaWorkerService"/> (Task 18.4). Bound from the
/// "CustodySlaWorker" configuration section (options — no hardcoding, same shape as
/// <c>NotificationWorkerOptions</c>). The SLA DURATION itself is NOT here — it is resolved per
/// community via Config_Service (<c>parcel.custody.slaHours</c>); these only tune how the sweep runs.
/// </summary>
public sealed class CustodySlaWorkerOptions
{
    public const string SectionName = "CustodySlaWorker";

    /// <summary>How often (seconds) a sweep pass runs when the backlog is cleared. Default 300 (5 min).</summary>
    public int PollIntervalSeconds { get; set; } = 300;

    /// <summary>Max parcels escalated per pass (the service caps the per-call work on top). Default 100.</summary>
    public int BatchSize { get; set; } = 100;
}
