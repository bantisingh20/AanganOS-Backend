using UrbanNest.Application.Helpdesk;
using Microsoft.Extensions.Options;

namespace UrbanNest.Worker;

/// <summary>
/// Background sweep for the helpdesk ticket SLA-breach escalation (Task 20.2, Req 33.3). It
/// periodically asks <see cref="IHelpdeskService.EscalateBreachedTicketsAsync"/> to escalate a
/// bounded batch of tickets that have passed their per-priority SLA due time while still assigned
/// and unresolved: each gets a one-time escalation stamp (so it is never double-escalated) and an
/// SLA-breach notification to the next responder via the transactional outbox (push + email).
///
/// <para><b>Mirrors the parcel custody-SLA worker.</b> Same structure as
/// <see cref="CustodySlaWorkerService"/> / <see cref="OutboxWorkerService"/>: a singleton hosted
/// service that creates a FRESH DI scope per pass (so the <c>AppDbContext</c> and the scoped
/// <see cref="IHelpdeskService"/> are never a long-lived singleton accumulating tracked entities),
/// runs under the worker's cross-tenant super-admin context (the sweep legitimately spans all
/// communities), keeps draining back-to-back while full batches come back and only sleeps once a
/// short batch signals the backlog is cleared — low latency without unbounded memory.</para>
///
/// <para><b>Bounded + fail-safe.</b> Each pass escalates at most <c>BatchSize</c> tickets (the
/// service additionally caps the per-call work), and the service saves per ticket so one ticket's
/// concurrency loss is isolated. A pass-level failure (e.g. DB unavailable) is logged and backed off
/// — it never kills the worker, and the business side is unaffected because escalation side-effects
/// ride the outbox and are retried on the next pass.</para>
/// </summary>
public sealed class TicketSlaWorkerService(
    IServiceScopeFactory scopeFactory,
    IOptions<TicketSlaWorkerOptions> workerOptions,
    ILogger<TicketSlaWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(workerOptions.Value.PollIntervalSeconds);
        logger.LogInformation(
            "Ticket SLA worker started (batch size {BatchSize}, poll {PollSeconds}s).",
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
                logger.LogError(ex, "Ticket SLA sweep pass failed; backing off.");
                escalated = 0;
            }

            // Only sleep once the backlog is (likely) cleared. A full batch means there may be more
            // breached tickets, so loop immediately to keep escalation latency low under load.
            if (escalated < workerOptions.Value.BatchSize)
            {
                await DelaySafelyAsync(pollInterval, stoppingToken);
            }
        }

        logger.LogInformation("Ticket SLA worker stopping.");
    }

    /// <summary>
    /// Run one sweep inside a fresh DI scope. Returns the number of tickets escalated so the caller
    /// knows whether to keep sweeping or sleep.
    /// </summary>
    private async Task<int> SweepBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var helpdesk = scope.ServiceProvider.GetRequiredService<IHelpdeskService>();
        return await helpdesk.EscalateBreachedTicketsAsync(workerOptions.Value.BatchSize, cancellationToken);
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
/// Tunables for the <see cref="TicketSlaWorkerService"/> (Task 20.2). Bound from the
/// "TicketSlaWorker" configuration section (options — no hardcoding, same shape as
/// <c>CustodySlaWorkerOptions</c>). The SLA DURATIONS themselves are NOT here — they are resolved
/// per community/priority via Config_Service (<c>ticket.sla.hours.{priority}</c>); these only tune
/// how the sweep runs.
/// </summary>
public sealed class TicketSlaWorkerOptions
{
    public const string SectionName = "TicketSlaWorker";

    /// <summary>How often (seconds) a sweep pass runs when the backlog is cleared. Default 300 (5 min).</summary>
    public int PollIntervalSeconds { get; set; } = 300;

    /// <summary>Max tickets escalated per pass (the service caps the per-call work on top). Default 100.</summary>
    public int BatchSize { get; set; } = 100;
}
