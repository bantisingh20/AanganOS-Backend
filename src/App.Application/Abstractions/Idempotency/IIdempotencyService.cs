namespace App.Application.Abstractions.Idempotency;

/// <summary>
/// Reusable at-most-once primitive (Req 30.5, 44.2, 63.3). Later payment (26.x) and parcel-handover
/// (18.x) endpoints wrap their handler in <see cref="ExecuteAsync{TResult}"/> so a retried or
/// concurrent request carrying the same <c>Idempotency-Key</c> runs the operation at most once and
/// every subsequent call returns the first execution's result.
///
/// <para><b>How at-most-once is guaranteed.</b> The implementation reserves the key with an INSERT
/// guarded by the <c>Unique(KeyValue, Endpoint)</c> index <i>before</i> running the operation.
/// Two concurrent requests with the same key both attempt the insert; the database lets exactly one
/// succeed and the other fails the unique constraint — so only the winner executes
/// <paramref name="operation"/>, and the loser (and any later retry) replays the stored result.
/// There is no read-then-write gap in application code (steering: DB-level guarantees, not
/// read-then-write).</para>
/// </summary>
public interface IIdempotencyService
{
    /// <summary>
    /// Run <paramref name="operation"/> at most once for the pair (<paramref name="key"/>,
    /// <paramref name="endpoint"/>).
    /// <list type="bullet">
    /// <item>First call: reserve the key, execute, persist the serialized result, return it with
    /// <see cref="IdempotencyExecutionResult{TResult}.WasReplayed"/> = false.</item>
    /// <item>Replay (same key + endpoint, matching request hash): return the stored result with
    /// <see cref="IdempotencyExecutionResult{TResult}.WasReplayed"/> = true, WITHOUT re-executing.</item>
    /// <item>Same key + endpoint but a different <paramref name="requestHash"/>: the key was reused
    /// for a different payload — the implementation throws a conflict rather than returning the
    /// wrong result.</item>
    /// </list>
    /// </summary>
    /// <param name="key">The client-supplied <c>Idempotency-Key</c> header value (required).</param>
    /// <param name="endpoint">Logical operation the key scopes (e.g. <c>POST /api/v1/payments</c>).</param>
    /// <param name="requestHash">
    /// Stable hash of the request payload, used to detect a key reused with a different body.
    /// </param>
    /// <param name="operation">The side-effecting operation to run at most once.</param>
    Task<IdempotencyExecutionResult<TResult>> ExecuteAsync<TResult>(
        string key,
        string endpoint,
        string requestHash,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of <see cref="IIdempotencyService.ExecuteAsync{TResult}"/>: the operation result plus
/// whether it came from a replay of a prior execution (so the API can map a replay to the
/// IDEMPOTENT_REPLAY code while still returning the original result at HTTP 200).
/// </summary>
public sealed record IdempotencyExecutionResult<TResult>(TResult Result, bool WasReplayed);
