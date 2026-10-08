namespace CommunityOS.Domain.Idempotency;

/// <summary>
/// A record of one at-most-once operation keyed by a client-supplied <c>Idempotency-Key</c> header
/// scoped to a logical <see cref="Endpoint"/> (design "IdempotencyKey" table, Req 30.5, 44.2, 63.3).
/// The store carries a <b>Unique(<see cref="KeyValue"/>, <see cref="Endpoint"/>)</b> index: the row
/// is inserted <i>before</i> the operation runs, so two concurrent retries of the same key race on
/// the insert — exactly one wins and executes, the loser is rejected by the unique constraint and
/// replays the winner's stored result. At-most-once is therefore a database guarantee, never an
/// application read-then-write check (steering: concurrency safety via DB-level guarantees).
///
/// <para>Not a <c>TenantOwnedEntity</c>/<c>PlatformEntity</c>: an idempotency row is immutable
/// bookkeeping, never soft-deleted or tenant-filtered (the key value itself is the only lookup
/// axis, together with the endpoint), so it carries only its own columns.</para>
/// </summary>
public class IdempotencyKey
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The client-supplied <c>Idempotency-Key</c> header value. Part of the unique index.</summary>
    public string KeyValue { get; set; } = string.Empty;

    /// <summary>
    /// Logical operation the key scopes (e.g. <c>POST /api/v1/payments</c>). The same key under a
    /// different endpoint is an independent operation. Part of the unique index.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Hash of the original request payload. On replay a differing hash means the same key was
    /// reused for a different request — a client error surfaced as a conflict, never a silent
    /// replay of the wrong result.
    /// </summary>
    public string RequestHash { get; set; } = string.Empty;

    /// <summary>
    /// Serialized snapshot of the original successful response, returned verbatim on replay so the
    /// caller receives the first execution's result (Req 63.3). Null until the operation completes
    /// (the row is reserved at the start of execution and finalized on success).
    /// </summary>
    public string? ResponseSnapshot { get; set; }

    /// <summary>When the key was first reserved (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; }
}
