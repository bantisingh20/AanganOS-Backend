using UrbanNest.Application.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace UrbanNest.Infrastructure.Persistence;

/// <summary>
/// Maps a database unique-constraint violation surfaced by <see cref="DbUpdateException"/> to the
/// application's <see cref="ConflictException"/> (CONFLICT envelope, Req 3.3, 3.6). This is how the
/// Identity services stay <b>concurrency-safe</b>: they do NOT pre-check "does a role with this name
/// already exist?" with a read-then-write (which races two concurrent requests into a duplicate);
/// instead they let the filtered unique index be the single source of truth and translate the race
/// loser's violation into a conflict (steering: concurrency safety for uniqueness via DB-level
/// guarantees, not an application read-then-write gap).
///
/// <para>Provider-agnostic by design: it does not match a SQL Server error number (2601/2627) or a
/// SQLite result code directly — a <see cref="DbUpdateException"/> raised while saving is, for the
/// INSERT/UPDATE paths these services run, a uniqueness collision (the only unique indexes on these
/// tables are the ones Task 5.1 declared). Matching on the exception type keeps the SQL Server
/// production path and the SQLite test path on the same code. ponytail: if a future table grows a
/// second class of constraint whose violation must map elsewhere, inspect the inner exception's
/// message/number here rather than at every call site.</para>
/// </summary>
internal static class UniqueConstraintGuard
{
    /// <summary>
    /// Run <paramref name="save"/> (a <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>
    /// call), translating a unique-constraint violation into a <see cref="ConflictException"/> with
    /// <paramref name="conflictMessage"/>.
    /// </summary>
    public static async Task SaveTranslatingUniqueViolationAsync(
        Func<Task> save,
        string conflictMessage)
    {
        try
        {
            await save();
        }
        catch (DbUpdateException ex) when (!IsConcurrency(ex))
        {
            throw new ConflictException(conflictMessage);
        }
    }

    // A DbUpdateConcurrencyException (row-version mismatch) is also a conflict, but a distinct one
    // these create/assign paths don't trigger; let it fall through to the global handler, which
    // already maps concurrency to CONFLICT. We only translate the "constraint" subclass here.
    private static bool IsConcurrency(DbUpdateException ex) => ex is DbUpdateConcurrencyException;
}
