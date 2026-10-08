using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Residents;

/// <summary>
/// A person associated with a community as an owner, co-owner, tenant or family member (Req 15.1).
/// Carries its owning <see cref="TenantOwnedEntity.CommunityId"/> and an optional link to a login
/// <see cref="UserId"/> (a resident may or may not have a platform account).
///
/// <para><b>Configurable type (Req 15.1).</b> <see cref="ResidentType"/> is stored as a stable
/// master-data code resolved at runtime against the editable <c>Resident_Type</c> list
/// (owner / co_owner / tenant / family_member) — NOT a frozen enum — so an operator can add or
/// retire a type without a code change (steering: no hardcoding), exactly like <c>Unit_Type</c>.</para>
///
/// <para><b>Verification status (Req 15.1, 17.x).</b> <see cref="VerificationStatus"/> is a stable
/// code (pending / verified / rejected) that defaults to <c>pending</c> on create. Only the field
/// and its default live here; the verification/approval WORKFLOW (approve-once, approver/timestamp,
/// rejection reason, resident notification) is Task 12.3. <see cref="RejectionReason"/> is populated
/// by that later workflow when a resident is rejected.</para>
///
/// <para><b>Resident self-view (Req 5.4, 66.3).</b> A <c>Resident</c> is deliberately NOT
/// <see cref="IUnitScoped"/> — a person can be associated with units over time and is not pinned to
/// a single unit. Resident self-scoping is realized on <see cref="HouseholdMember"/> (which carries
/// a <c>UnitId</c>): a resident principal sees only their own household rows, and reaches their own
/// profile through those rows.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>, so the global tenant + soft-delete filter
/// scopes every read to the caller's authorized communities and delete is a soft delete that stays
/// traceable (Req 15.5). The inherited <c>RowVersion</c> gives optimistic concurrency on edit — the
/// token the Task 12.3 approve-once workflow will rely on.</para>
/// </summary>
public class Resident : TenantOwnedEntity
{
    /// <summary>
    /// Optional link to a login <c>User</c> (Identity). Null when the resident has no platform
    /// account yet. Kept as a loose id (not a hard FK navigation) because <c>User</c> is a
    /// platform-scoped entity in a different module; the association is validated/managed by the
    /// resident/user workflows, not by a cascade.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>Resident's full name. Required.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional contact email.</summary>
    public string? Email { get; set; }

    /// <summary>Optional contact phone.</summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Configurable resident-type code resolved against the <c>Resident_Type</c> master-data list
    /// (owner / co_owner / tenant / family_member, Req 15.1). Stored as the stable code, never a
    /// frozen enum.
    /// </summary>
    public string ResidentType { get; set; } = string.Empty;

    /// <summary>
    /// Verification status code (pending / verified / rejected). Defaults to <c>pending</c> on
    /// create (Req 15.1); transitions are owned by the Task 12.3 verification workflow.
    /// </summary>
    public string VerificationStatus { get; set; } = ResidentVerificationStatuses.Pending;

    /// <summary>
    /// Reason recorded when a resident is rejected (Req 17.3). Null while pending/verified; set by
    /// the Task 12.3 workflow.
    /// </summary>
    public string? RejectionReason { get; set; }

    /// <summary>Household associations of this resident with units (Req 15.2).</summary>
    public ICollection<HouseholdMember> HouseholdMembers { get; set; } = new List<HouseholdMember>();

    /// <summary>Emergency contacts for this resident (Req 15.3). One-to-many.</summary>
    public ICollection<EmergencyContact> EmergencyContacts { get; set; } = new List<EmergencyContact>();
}

/// <summary>
/// Well-known default verification-status codes (Req 15.1, 17.x). These are the seeded
/// <c>Resident_Verification_Status</c> master-data codes, kept as constants so the service can set
/// the create-time default (<see cref="Pending"/>) without a magic string. They are <b>data</b>, not
/// a behavioural enum — the status list is editable; this is only a convenience for the known codes.
/// </summary>
public static class ResidentVerificationStatuses
{
    /// <summary>Initial status on create (Req 15.1).</summary>
    public const string Pending = "pending";

    /// <summary>Set by the Task 12.3 approval workflow when a resident is verified.</summary>
    public const string Verified = "verified";

    /// <summary>Set by the Task 12.3 workflow when a resident is rejected, alongside a reason.</summary>
    public const string Rejected = "rejected";
}
