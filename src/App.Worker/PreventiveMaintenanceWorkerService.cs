using App.Application.Maintenance;
using Microsoft.Extensions.Options;

namespace App.Worker;

/// <summary>
/// Background sweep for preventive-maintenance work-order generation (Task 21.2, Req 36.1). It
/// periodically asks <see cref="IMaintenanceService.GenerateDuePreventiveWorkOrdersAsync"/> to
/// generate work orders for a bounded batch of active <c>MaintenancePlan</c>s whose next scheduled
/// occurrence has fallen due: each due plan generates ONE work order for the current occurrence and
/// advances its <c>NextDueAtUtc</c> under the plan's <c>RowVersion</c>, so a plan can never
/// double-generate for the same occurrence under concurrent sweeps (a losing sweep → CONFLICT, skipped).
///
/// <para><b>Mirrors the ticket/parcel SLA workers.</b> Same structure as
/// <see cref="TicketSlaWorkerService"/> / <see cref="CustodySlaWorkerService"/> /
/// <see cref="OutboxWorkerService"/>: a singleton hosted service that creates a FRESH DI scope per
/// pass (so the <c>AppDbContext</c> and the scoped <see cref="IMaintenanceService"/> are never a
/// long-lived singleton accumulating tracked entities), runs under the worker's cross-tenant
/// super-admin context (the sweep legitimately spans all communities), keeps draining back-to-back
/// while full batches come back and only sleeps once a short batch signals the backlog is cleared —
/// low latency without unbounded memory.</para>
///
/// <para><b>Bounded + fail-safe.</b> Each pass generates for at most <c>BatchSize</c> plans (the
/// service additionally caps the per-call work), and the service saves per plan so one plan's
/// concurrency loss is isolated. A pass-level failure (e.g. DB unavailable) is logged and backed off
/// — it never kills the worker, and nothing generated this pass stays due and is picked up next time.</para>
/// </summary>
public sealed class PreventiveMaintenanceWorkerService(
    IServiceScopeFactory scopeFactory,
    IOptions<PreventiveMaintenanceWorkerOptions> workerOptions,
    ILogger<PreventiveMaintenanceWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(workerOptions.Value.PollIntervalSeconds);
        logger.LogInformation(
            "Preventive-maintenance worker started (batch size {BatchSize}, poll {PollSeconds}s).",
            workerOptions.Value.BatchSize,
            workerOptions.Value.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            int generated;
            try
            {
                generated = await SweepBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A pass-level failure must not kill the worker. Log and back off; the next pass
                // retries. Nothing generated this pass stays due and is picked up next time.
                logger.LogError(ex, "Preventive-maintenance sweep pass failed; backing off.");
                generated = 0;
            }

            // Only sleep once the backlog is (likely) cleared. A full batch means there may be more
            // due plans, so loop immediately to keep generation latency low under load.
            if (generated < workerOptions.Value.BatchSize)
            {
                await DelaySafelyAsync(pollInterval, stoppingToken);
            }
        }

        logger.LogInformation("Preventive-maintenance worker stopping.");
    }

    /// <summary>
    /// Run one sweep inside a fresh DI scope. Returns the number of work orders generated so the
    /// caller knows whether to keep sweeping or sleep.
    /// </summary>
    private async Task<int> SweepBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<IMaintenanceService>();
        return await maintenance.GenerateDuePreventiveWorkOrdersAsync(workerOptions.Value.BatchSize, cancellationToken);
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
/// Tunables for the <see cref="PreventiveMaintenanceWorkerService"/> (Task 21.2). Bound from the
/// "PreventiveMaintenanceWorker" configuration section (options — no hardcoding, same shape as
/// <c>TicketSlaWorkerOptions</c>). The plan FREQUENCIES/INTERVALS themselves are NOT here — they are
/// resolved per community via Config_Service (<c>maintenance.plan.intervalDays.{frequency}</c>);
/// these only tune how the sweep runs.
/// </summary>
public sealed class PreventiveMaintenanceWorkerOptions
{
    public const string SectionName = "PreventiveMaintenanceWorker";

    /// <summary>How often (seconds) a sweep pass runs when the backlog is cleared. Default 300 (5 min).</summary>
    public int PollIntervalSeconds { get; set; } = 300;

    /// <summary>Max plans processed per pass (the service caps the per-call work on top). Default 100.</summary>
    public int BatchSize { get; set; } = 100;
}
