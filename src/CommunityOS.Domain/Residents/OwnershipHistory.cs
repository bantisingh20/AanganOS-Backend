using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Residents;

/// <summary>
/// One entry in a Unit's chronological ownership record (Req 16.1, 16.3). Each row names the
/// <see cref="ResidentId"/> who owned the <see cref="UnitId"/> from <see cref="StartDate"/> until
/// <see cref="EndDate"/> (null while that resident is the <b>current</b> owner).
///
/// <para><b>Append-only history (Req 16.1, 16.3).</b> Ownership is never edited in place: assigning
/// or transferring ownership <i>appends</i> a new row for the new owner and <i>closes</i> the prior
/// open row by stamping its <see cref="EndDate"/> — it never deletes or overwrites a prior row, so
/// the full history stays traceable. The close+insert happen in a single transaction so a unit never
/// has two open (<see cref="EndDate"/> == null) owners (the at-most-one-open invariant,
/// enforced by <c>UnitHistoryService</c>; on SQL Server also backed by a filtered unique index on
/// <c>(UnitId) WHERE EndDate IS NULL</c>).</para>
///
/// <para><b>Same-community (Req 16.1).</b> The owner <see cref="ResidentId"/> and the
/// <see cref="UnitId"/> must belong to the same community — validated in the service before persist,
/// the same convention HouseholdMember/Unit use. The row carries its own
/// <see cref="TenantOwnedEntity.CommunityId"/> for tenant isolation.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token, audit columns. It is NOT <c>IUnitScoped</c> — ownership records are an admin
/// register, not resident self-view data; a resident's own household view is realized on
/// <see cref="HouseholdMember"/>.</para>
/// </summary>
public class OwnershipHistory : TenantOwnedEntity
{
    /// <summary>The unit this ownership entry is for (Req 16.1). Required.</summary>
    public Guid UnitId { get; set; }

    /// <summary>The resident who holds (or held) ownership of the unit — the Owner (Req 16.1). Required.</summary>
    public Guid ResidentId { get; set; }

    /// <summary>Navigation to the owning resident.</summary>
    public Resident? Resident { get; set; }

    /// <summary>When this ownership began (Req 16.1). Set on assign/transfer.</summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// When this ownership ended, or null while this resident is the current owner (Req 16.1). Set
    /// (never cleared) when a new owner supersedes this one, closing the prior entry (Req 16.3).
    /// </summary>
    public DateTime? EndDate { get; set; }
}
