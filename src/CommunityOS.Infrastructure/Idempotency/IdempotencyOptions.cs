using System.ComponentModel.DataAnnotations;

namespace CommunityOS.Infrastructure.Idempotency;

/// <summary>
/// Idempotency-store tunables bound from the <c>Idempotency</c> configuration section (options
/// pattern — nothing hardcoded, Req 8/66.4). Retention governs how long a stored key/result is
/// kept before a future cleanup job may purge it; the in-flight replay-wait bounds how long a
/// concurrent duplicate waits for the winning request to finalize its snapshot before returning.
/// </summary>
public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    /// <summary>How long a stored key + response is retained (days) before it may be purged.</summary>
    [Range(1, 3650)]
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// How many times a concurrent duplicate re-reads the winner's row while it is still executing,
    /// waiting for the response snapshot to be finalized before replaying it.
    /// </summary>
    [Range(1, 100)]
    public int ReplayWaitAttempts { get; set; } = 20;

    /// <summary>Delay between replay-wait re-reads (milliseconds).</summary>
    [Range(1, 5000)]
    public int ReplayWaitDelayMs { get; set; } = 50;
}
