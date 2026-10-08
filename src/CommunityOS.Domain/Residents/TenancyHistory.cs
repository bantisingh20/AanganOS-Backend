using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Residents;

/// <summary>
/// One entry in a Unit's chronological tenancy record (Req 16.2, 16.3). Each row names the
/// <see cref="ResidentId"/> who rented the <see cref="UnitId"/> from <see cref="StartDate"/> until
/// <see cref="EndDate"/> (null while the tenancy is <b>active</b>).
///
/// <para><b>Append/close, retain prior (Req 16.2, 16.3).</b> Beginning a tenancy <i>appends</i> a new
/// open row; ending a tenancy <i>closes</i> the matching open row by stamping its
/// <see cref="EndDate"/>. A prior entry is never deleted or overwritten, so the full tenancy history
/// stays traceable. Unlike ownership, a unit may legitimately have more than one open tenancy at once
/// (e.g. co-tenants), so there is no at-most-one-open constraint here — ending a tenancy targets a
/// specific resident's open row.</para>
///
/// <para><b>Same-community (Req 16.2).</b> The tenant <see cref="ResidentId"/> and the
/// <see cref="UnitId"/> must belong to the same community — validated in the service before persist.
/// The row carries its own <see cref="TenantOwnedEntity.CommunityId"/> for tenant isolation.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token, audit columns. Not <c>IUnitScoped</c> (admin register, same rationale as
/// <see cref="OwnershipHistory"/>).</para>
/// </summary>
public class TenancyHistory : TenantOwnedEntity
{
    /// <summary>The unit this tenancy entry is for (Req 16.2). Required.</summary>
    public Guid UnitId { get; set; }

    /// <summary>The resident renting (or who rented) the unit — the Tenant (Req 16.2). Required.</summary>
    public Guid ResidentId { get; set; }

    /// <summary>Navigation to the tenant resident.</summary>
    public Resident? Resident { get; set; }

    /// <summary>When this tenancy began (Req 16.2). Set on begin.</summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// When this tenancy ended, or null while active (Req 16.2). Set (never cleared) when the tenancy
    /// ends, closing the entry while retaining it (Req 16.3).
    /// </summary>
    public DateTime? EndDate { get; set; }
}
