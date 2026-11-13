using System.Text.Json;
using App.Application.Abstractions;
using App.Application.Abstractions.Idempotency;
using App.Application.Exceptions;
using App.Domain.Idempotency;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Idempotency;

/// <summary>
/// EF-backed <see cref="IIdempotencyService"/> — the reusable at-most-once primitive (Req 30.5,
/// 44.2, 63.3). Later payment (26.x) and parcel-handover (18.x) handlers call
/// <see cref="ExecuteAsync{TResult}"/> to get exactly-once-under-retries semantics.
///
/// <para><b>At-most-once without a read-then-write gap.</b> The method does NOT check "has this key
/// run?" and then conditionally execute — two concurrent requests would both pass that check and
/// both execute. Instead it <i>reserves</i> the key by INSERTing the row first, relying on the
/// <c>Unique(KeyValue, Endpoint)</c> index to let exactly one insert succeed. The winner executes
/// the operation and writes the response snapshot; the loser's insert fails the unique constraint,
/// so it re-reads the winner's row and replays the stored result. The database — not application
/// code — decides the winner, so retries and genuine concurrency both collapse to one execution
/// (steering: concurrency safety via DB-level guarantees).</para>
///
/// <para>The optimistic read at the top is purely a fast-path for the common replay case (a later
/// retry after the first call already finished); correctness never depends on it, only on the
/// unique-constraint-guarded insert.</para>
/// </summary>
public sealed class IdempotencyService(
    AppDbContext db,
    IClock clock,
    IOptions<IdempotencyOptions> options) : IIdempotencyService
{
    private readonly IdempotencyOptions _options = options.Value;

    public async Task<IdempotencyExecutionResult<TResult>> ExecuteAsync<TResult>(
        string key,
        string endpoint,
        string requestHash,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentNullException.ThrowIfNull(operation);
        requestHash ??= string.Empty;

        // Fast path: a prior execution already finalized → replay without touching the operation.
        var existing = await FindAsync(key, endpoint, cancellationToken);
        if (existing is { ResponseSnapshot: not null })
        {
            return Replay<TResult>(existing, requestHash);
        }

        // Reserve the key. The Unique(KeyValue, Endpoint) index is the gate: exactly one concurrent
        // insert wins; everyone else lands in the catch below and replays the winner's result.
        var reservation = new IdempotencyKey
        {
            KeyValue = key,
            Endpoint = endpoint,
            RequestHash = requestHash,
            ResponseSnapshot = null,
            CreatedAtUtc = clock.UtcNow,
        };
        db.IdempotencyKeys.Add(reservation);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost the reservation race on the Unique(KeyValue, Endpoint) index (or this is a retry
            // after a prior reservation). The reservation insert is a brand-new row with no
            // concurrency token, so a DbUpdateException here is a uniqueness collision, never a
            // row-version conflict. Detach our failed add so the context is clean, then replay the
            // winner's stored result.
            db.Entry(reservation).State = EntityState.Detached;
            var winner = await WaitForFinalizedAsync(key, endpoint, cancellationToken);
            return Replay<TResult>(winner, requestHash);
        }

        // We own the key: run the operation exactly once, then persist its result snapshot so every
        // later retry/replay returns this first execution's result.
        var result = await operation(cancellationToken);
        reservation.ResponseSnapshot = JsonSerializer.Serialize(result);
        await db.SaveChangesAsync(cancellationToken);

        return new IdempotencyExecutionResult<TResult>(result, WasReplayed: false);
    }

    private Task<IdempotencyKey?> FindAsync(string key, string endpoint, CancellationToken ct) =>
        db.IdempotencyKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyValue == key && k.Endpoint == endpoint, ct);

    /// <summary>
    /// The reservation winner may still be executing when the loser arrives. Re-read its row until
    /// the response snapshot is finalized, bounded by configured attempts so a crashed winner can
    /// never hang the loser forever.
    /// </summary>
    private async Task<IdempotencyKey> WaitForFinalizedAsync(string key, string endpoint, CancellationToken ct)
    {
        for (var attempt = 0; attempt < _options.ReplayWaitAttempts; attempt++)
        {
            var row = await FindAsync(key, endpoint, ct);
            if (row is { ResponseSnapshot: not null })
            {
                return row;
            }

            await Task.Delay(_options.ReplayWaitDelayMs, ct);
        }

        // Winner never finalized within the window (e.g. it faulted). Surface a retryable conflict
        // rather than block or fabricate a result.
        throw new ConflictException("A concurrent idempotent operation is still in progress; please retry.");
    }

    private static IdempotencyExecutionResult<TResult> Replay<TResult>(IdempotencyKey stored, string requestHash)
    {
        // Same key + endpoint reused for a DIFFERENT payload: never replay the wrong result.
        if (!string.Equals(stored.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new IdempotencyKeyReuseException();
        }

        var result = stored.ResponseSnapshot is null
            ? default!
            : JsonSerializer.Deserialize<TResult>(stored.ResponseSnapshot)!;

        return new IdempotencyExecutionResult<TResult>(result, WasReplayed: true);
    }
}
