using CommunityOS.Application.Maintenance;
using Microsoft.Extensions.Options;

namespace CommunityOS.Worker;

/// <summary>
/// Background sweep for the AMC-contract expiry reminders (Task 21.3, Req 37.3). It periodically asks
/// <see cref="IMaintenanceService.SendDueAmcExpiryRemindersAsync"/> to remind a bounded batch of
/// active AMC contracts that have entered their configured expiry-reminder window and have not yet
/// been reminded for their current end date: each gets a one-time reminder stamp (so it is never
/// reminded twice for the same window) and an <c>amc.expiry-reminder</c> notification to the
/// responsible user via the transactional outbox.
///
/// <para><b>Mirrors the ticket/parcel SLA workers.</b> Same structure as
/// <see cref="TicketSlaWorkerService"/> / <see cref="CustodySlaWorkerService"/> /
/// <see cref="PreventiveMaintenanceWorkerService"/>: a singleton hosted service that creates a FRESH
/// DI scope per pass (so the <c>AppDbContext</c> and the scoped <see cref="IMaintenanceService"/> are
/// never a long-lived singleton accumulating tracked entities), runs under the worker's cross-tenant
/// super-admin context (the sweep legitimately spans all communities), keeps draining back-to-back
/// while full batches come back and only sleeps once a short batch signals the backlog is cleared —
/// low latency without unbounded memory.</para>
///
/// <para><b>Bounded + fail-safe.</b> Each pass reminds at most <c>BatchSize</c> contracts (the service
/// additionally caps the per-call work), and the service saves per contract so one contract's
/// concurrency loss is isolated. A pass-level failure (e.g. DB unavailable) is logged and backed off —
/// it never kills the worker, and the reminder side-effects ride the outbox and are retried on the
/// next pass. The reminder WINDOW (days before expiry) is resolved per community via Config_Service in
/// the service, not here (no hardcoding).</para>
/// </summary>
public sealed class AmcExpiryReminderWorkerService(
    IServiceScopeFactory scopeFactory,
    IOptions<AmcExpiryReminderWorkerOptions> workerOptions,
    ILogger<AmcExpiryReminderWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(workerOptions.Value.PollIntervalSeconds);
        logger.LogInformation(
            "AMC expiry-reminder worker started (batch size {BatchSize}, poll {PollSeconds}s).",
            workerOptions.Value.BatchSize,
            workerOptions.Value.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            int reminded;
            try
            {
                reminded = await SweepBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A pass-level failure must not kill the worker. Log and back off; the next pass
                // retries. Nothing reminded this pass stays due and is picked up next time.
                logger.LogError(ex, "AMC expiry-reminder sweep pass failed; backing off.");
                reminded = 0;
            }

            // Only sleep once the backlog is (likely) cleared. A full batch means there may be more due
            // contracts, so loop immediately to keep reminder latency low under load.
            if (reminded < workerOptions.Value.BatchSize)
            {
                await DelaySafelyAsync(pollInterval, stoppingToken);
            }
        }

        logger.LogInformation("AMC expiry-reminder worker stopping.");
    }

    /// <summary>
    /// Run one sweep inside a fresh DI scope. Returns the number of reminders sent so the caller knows
    /// whether to keep sweeping or sleep.
    /// </summary>
    private async Task<int> SweepBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<IMaintenanceService>();
        return await maintenance.SendDueAmcExpiryRemindersAsync(workerOptions.Value.BatchSize, cancellationToken);
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
/// Tunables for the <see cref="AmcExpiryReminderWorkerService"/> (Task 21.3). Bound from the
/// "AmcExpiryReminderWorker" configuration section (options — no hardcoding, same shape as
/// <c>TicketSlaWorkerOptions</c>). The reminder WINDOW itself is NOT here — it is resolved per
/// community via Config_Service (<c>maintenance.amc.expiryReminderDays</c>); these only tune how the
/// sweep runs.
/// </summary>
public sealed class AmcExpiryReminderWorkerOptions
{
    public const string SectionName = "AmcExpiryReminderWorker";

    /// <summary>How often (seconds) a sweep pass runs when the backlog is cleared. Default 3600 (1 hour).</summary>
    public int PollIntervalSeconds { get; set; } = 3600;

    /// <summary>Max contracts reminded per pass (the service caps the per-call work on top). Default 100.</summary>
    public int BatchSize { get; set; } = 100;
}
