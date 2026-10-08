using System.ComponentModel.DataAnnotations;

namespace CommunityOS.Application.Notifications;

/// <summary>
/// Drain-loop tunables for the Task 7.2 outbox worker, bound from the <c>NotificationWorker</c>
/// configuration section (options pattern — nothing hardcoded, steering). Separate from
/// <see cref="NotificationOptions"/> (which owns provider secrets + the retry policy) so the worker's
/// scheduling knobs live with the worker concern. The retry policy itself (max attempts, backoff)
/// still comes from <see cref="NotificationOptions"/> (Req 11.4).
/// </summary>
public sealed class NotificationWorkerOptions
{
    public const string SectionName = "NotificationWorker";

    /// <summary>
    /// Seconds the worker sleeps between drain passes when there is nothing (more) to do. Keeps the
    /// loop bounded and off the CPU; a lower value lowers delivery latency at the cost of more polls.
    /// Default 10.
    /// </summary>
    [Range(1, 3600)]
    public int PollIntervalSeconds { get; init; } = 10;

    /// <summary>
    /// Max outbox rows drained per pass (bounded work, Req 65.4 — never load the whole table). The
    /// loop keeps draining back-to-back while a full batch comes back, so throughput is not capped by
    /// the poll interval under load; it only sleeps once a short batch signals the queue is drained.
    /// Default 100.
    /// </summary>
    [Range(1, 10000)]
    public int BatchSize { get; init; } = 100;
}
