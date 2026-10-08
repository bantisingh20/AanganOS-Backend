namespace CommunityOS.Domain.Notifications;

/// <summary>
/// Lifecycle of a <see cref="NotificationOutboxMessage"/> row. Task 7.1 only ever writes
/// <see cref="Pending"/> rows inside the business transaction; the Task 7.2 worker drains them and
/// transitions them to <see cref="Sent"/> or, after exhausting the configured retry policy,
/// <see cref="Failed"/> (Req 11.4). <see cref="Suppressed"/> records a row that was intentionally
/// not dispatched (e.g. the resident opted out of an optional category, Req 20.3) so the decision
/// is auditable rather than silently dropped.
/// </summary>
public enum OutboxStatus
{
    /// <summary>Queued, awaiting dispatch by the worker (the only status Task 7.1 writes).</summary>
    Pending = 0,

    /// <summary>Successfully dispatched to the provider (set by the Task 7.2 worker).</summary>
    Sent = 1,

    /// <summary>Dispatch exhausted the configured retry policy (set by the Task 7.2 worker, Req 11.4).</summary>
    Failed = 2,

    /// <summary>Not dispatched by design (e.g. optional category the resident opted out of, Req 20.3).</summary>
    Suppressed = 3,
}
