using App.Domain.Common;

namespace App.Domain.Community;

/// <summary>
/// A single managed community — the tenant root of all child data (Req 12.1). Its <see cref="Id"/>
/// IS the <see cref="CommunityId"/> used everywhere: when a Community is created its generated Id
/// becomes the CommunityId stamped on every Unit, Resident, Ticket, ... beneath it, so one row
/// anchors a whole tenant.
///
/// <para><b>Tenant-modeling note.</b> Community derives from <see cref="TenantOwnedEntity"/> and
/// <see cref="TenantOwnedEntity.CommunityId"/> is set equal to its own <see cref="Id"/> on create.
/// That makes the Community row itself fall inside the standard tenant global query filter
/// (<c>CommunityId ∈ authorized</c>, Req 5.2): a community admin naturally sees exactly their own
/// Community row, a super admin sees all, and no bespoke filtering is needed. The create path is a
/// platform super-admin action (Req 12.1), which bypasses the tenant predicate, so the first write
/// of a brand-new CommunityId is not blocked by the filter.</para>
///
/// <para><b>Configurable type (Req 12.2).</b> <see cref="CommunityType"/> is a master-data code
/// (apartment society / gated community / villa-bungalow / townhouse / mixed), validated at runtime
/// against the editable <c>Community_Type</c> list — NOT a compiled behaviour-gating enum, so an
/// operator can add or retire a type without a code change (steering: no hardcoding).</para>
///
/// <para>A Community may contain Units of more than one Unit_Type (Req 12.3) — nothing on this row
/// constrains the child Units' types; the allowance is realized in Unit management (Task 11.2).</para>
///
/// <para>Delete is a soft delete (Req 12.5): the archived Community stays physically present and
/// traceable via <c>IgnoreQueryFilters</c>.</para>
/// </summary>
public class Community : TenantOwnedEntity
{
    /// <summary>Owning <see cref="Organization"/> (Req 12.1). Required.</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>Human-readable community name. Required.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Configurable community-type code resolved against the <c>Community_Type</c> master-data list
    /// (Req 12.2). Stored as the stable machine code (e.g. <c>apartment_society</c>), never an enum.
    /// </summary>
    public string CommunityType { get; set; } = string.Empty;
}
