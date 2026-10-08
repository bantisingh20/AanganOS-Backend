using CommunityOS.Application.Abstractions;
using CommunityOS.Application.Audit;
using CommunityOS.Application.Exceptions;
using CommunityOS.Application.Residents;
using CommunityOS.Domain.Residents;
using CommunityOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommunityOS.Infrastructure.Residents;

/// <summary>
/// EF-backed <see cref="IUnitHistoryService"/> (Req 16.1–16.4).
///
/// <para><b>Append-only, retain prior (Req 16.1–16.3).</b> No path deletes or overwrites a prior
/// history row. Assigning a new owner <i>appends</i> a new open row and <i>closes</i> the prior open
/// row by stamping its <c>EndDate</c>; ending a tenancy stamps the matching open row's
/// <c>EndDate</c>. Prior rows remain, so the full history stays traceable.</para>
///
/// <para><b>At-most-one-open-owner under concurrency (Req 16.3 invariant).</b> The close+insert for
/// an ownership transfer are a single <c>SaveChangesAsync</c> (one transaction): either both the
/// prior-owner close and the new-owner insert commit, or neither does. On SQL Server a filtered
/// unique index on <c>(UnitId) WHERE EndDate IS NULL</c> (added in <c>AppDbContext.OnModelCreating</c>)
/// makes "one open owner per unit" a hard DB guarantee — two concurrent assignments race to the index
/// and the loser's violation is translated to CONFLICT via <see cref="UniqueConstraintGuard"/> (no
/// read-then-write pre-check). ponytail: the SQLite test provider has no filtered-index equivalent,
/// so under that provider the invariant rests on the single-transaction close+insert rather than a DB
/// constraint — acceptable for the test path; production (SQL Server) gets the index.</para>
///
/// <para><b>Same-community + tenant scope (Req 16.1, 16.2, 5.2).</b> The unit's and resident's
/// communities are loaded tenant-scoped (the global filter already hides foreign-tenant rows) and a
/// cross-community pairing is rejected with a validation error — the same convention
/// <see cref="ResidentService"/> uses. Reads use <c>AsNoTracking</c> and the
/// <c>(CommunityId, UnitId, StartDate)</c> index for ordered, N+1-free retrieval. Every mutation
/// appends an <see cref="AuditEntry"/> (Req 6.1).</para>
/// </summary>
public sealed class UnitHistoryService(
    AppDbContext db,
    IAuditService audit,
    IClock clock)
    : IUnitHistoryService
{
    private const string DuplicateOpenOwnerMessage =
        "Ownership of this unit was changed by another request. Reload and try again.";

    public async Task<OwnershipHistoryDto> AssignOwnershipAsync(AssignOwnershipRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var communityId = await RequireUnitAndResidentInSameCommunityAsync(
            request.UnitId, request.ResidentId, cancellationToken);

        var startDate = request.StartDate ?? clock.UtcNow;

        // Close the current open owner (if any) and append the new owner in ONE transaction so the
        // unit is never left with two open owners (Req 16.3 invariant). The prior row is retained —
        // only its EndDate is stamped, never deleted (Req 16.3). Tracked load (not AsNoTracking)
        // because we mutate the prior row.
        var openOwner = await db.OwnershipHistories
            .FirstOrDefaultAsync(o => o.UnitId == request.UnitId && o.EndDate == null, cancellationToken);
        if (openOwner is not null)
        {
            // Close at the new owner's start instant; guard against a start before the prior start.
            openOwner.EndDate = startDate < openOwner.StartDate ? openOwner.StartDate : startDate;
        }

        var entry = new OwnershipHistory
        {
            CommunityId = communityId,
            UnitId = request.UnitId,
            ResidentId = request.ResidentId,
            StartDate = startDate,
            EndDate = null,
        };
        db.OwnershipHistories.Add(entry);

        // One SaveChanges = one transaction: close + insert commit together. On SQL Server a race
        // loser trips the filtered unique index → CONFLICT; a concurrent EndDate stamp losing the
        // rowversion check is mapped to CONFLICT centrally in AppDbContext.SaveChangesAsync.
        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), DuplicateOpenOwnerMessage);

        await audit.AppendAsync(
            new AuditEntry("Assign", "OwnershipHistory", entry.Id.ToString(), entry.CommunityId, Detail: null),
            cancellationToken);

        return ToDto(entry);
    }

    public async Task<TenancyHistoryDto> BeginTenancyAsync(BeginTenancyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var communityId = await RequireUnitAndResidentInSameCommunityAsync(
            request.UnitId, request.ResidentId, cancellationToken);

        var entry = new TenancyHistory
        {
            CommunityId = communityId,
            UnitId = request.UnitId,
            ResidentId = request.ResidentId,
            StartDate = request.StartDate ?? clock.UtcNow,
            EndDate = null,
        };

        db.TenancyHistories.Add(entry);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("BeginTenancy", "TenancyHistory", entry.Id.ToString(), entry.CommunityId, Detail: null),
            cancellationToken);

        return ToDto(entry);
    }

    public async Task<TenancyHistoryDto> EndTenancyAsync(EndTenancyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Target this resident's open tenancy for the unit (a unit may have several open tenancies).
        // Tracked load because we stamp EndDate. The global tenant filter scopes it to the caller.
        var open = await db.TenancyHistories
            .Where(t => t.UnitId == request.UnitId && t.ResidentId == request.ResidentId && t.EndDate == null)
            .OrderBy(t => t.StartDate)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("No active tenancy found for this resident and unit.");

        var endDate = request.EndDate ?? clock.UtcNow;
        // An end before the start is nonsensical; clamp to the start so the row stays coherent.
        open.EndDate = endDate < open.StartDate ? open.StartDate : endDate;

        // A concurrent end losing the rowversion check → CONFLICT (central mapping in SaveChangesAsync).
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("EndTenancy", "TenancyHistory", open.Id.ToString(), open.CommunityId, Detail: null),
            cancellationToken);

        return ToDto(open);
    }

    public async Task<UnitHistoryDto> GetUnitHistoryAsync(Guid unitId, CancellationToken cancellationToken = default)
    {
        // Confirm the unit is visible to the caller (tenant-scoped) so a cross-tenant/unknown id gets
        // a not-found rather than two empty lists.
        var unitExists = await db.Units.AsNoTracking().AnyAsync(u => u.Id == unitId, cancellationToken);
        if (!unitExists)
        {
            throw new NotFoundException("Unit not found.");
        }

        // Chronological order (Req 16.4), oldest first; Id as a stable tie-break for equal start
        // dates. Both reads ride the (CommunityId, UnitId, StartDate) index — single ordered scans,
        // no N+1.
        var ownership = await db.OwnershipHistories.AsNoTracking()
            .Where(o => o.UnitId == unitId)
            .OrderBy(o => o.StartDate).ThenBy(o => o.Id)
            .Select(o => new OwnershipHistoryDto(o.Id, o.CommunityId, o.UnitId, o.ResidentId, o.StartDate, o.EndDate))
            .ToListAsync(cancellationToken);

        var tenancy = await db.TenancyHistories.AsNoTracking()
            .Where(t => t.UnitId == unitId)
            .OrderBy(t => t.StartDate).ThenBy(t => t.Id)
            .Select(t => new TenancyHistoryDto(t.Id, t.CommunityId, t.UnitId, t.ResidentId, t.StartDate, t.EndDate))
            .ToListAsync(cancellationToken);

        return new UnitHistoryDto(unitId, ownership, tenancy);
    }

    private static OwnershipHistoryDto ToDto(OwnershipHistory o) =>
        new(o.Id, o.CommunityId, o.UnitId, o.ResidentId, o.StartDate, o.EndDate);

    private static TenancyHistoryDto ToDto(TenancyHistory t) =>
        new(t.Id, t.CommunityId, t.UnitId, t.ResidentId, t.StartDate, t.EndDate);

    /// <summary>
    /// Load the unit's and resident's communities (tenant-scoped) and require they match (Req 16.1,
    /// 16.2). Returns the shared community id for the new history row. Unknown/foreign-tenant unit or
    /// resident → not found; a cross-community pairing → validation error.
    /// </summary>
    private async Task<Guid> RequireUnitAndResidentInSameCommunityAsync(
        Guid unitId, Guid residentId, CancellationToken cancellationToken)
    {
        Guid unitCommunityId = await db.Units.AsNoTracking()
            .Where(u => u.Id == unitId)
            .Select(u => (Guid?)u.CommunityId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Unit not found.");

        Guid residentCommunityId = await db.Residents.AsNoTracking()
            .Where(r => r.Id == residentId)
            .Select(r => (Guid?)r.CommunityId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Resident not found.");

        if (unitCommunityId != residentCommunityId)
        {
            throw new ValidationException("residentId", "The resident must belong to the same community as the unit.");
        }

        return unitCommunityId;
    }
}
