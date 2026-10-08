using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Community;

/// <summary>
/// A single addressable unit within a community — a flat/apartment, villa, shop, office, … (Req 14.1).
/// Carries its owning <see cref="TenantOwnedEntity.CommunityId"/> and sits optionally under a
/// <see cref="HierarchyNodeId"/> (the Floor/Building/Block node it belongs to, Req 13.x/14.1).
///
/// <para><b>Configurable type/status (Req 14.2, 14.3).</b> <see cref="UnitType"/> and
/// <see cref="UnitStatus"/> are stored as stable master-data codes resolved at runtime against the
/// editable <c>Unit_Type</c> / <c>Unit_Status</c> lists — NOT frozen enums — so an operator can add
/// or retire a type/status (apartment, villa, shop …; available/occupied/vacant/under_maintenance/
/// blocked) without a code change (steering: no hardcoding).</para>
///
/// <para><b>Free-form attributes (Req 14.1).</b> <see cref="CustomAttributes"/> holds arbitrary
/// community-specific key/value data (e.g. "facing": "east", "carpet_area_unit": "sqft") persisted
/// as a JSON column, so a community can capture its own fields without a schema change.</para>
///
/// <para><b>Concurrency-safe uniqueness (Req 14.4, 22.1).</b> <c>(CommunityId, UnitNumber)</c> is a
/// DB-level unique index: two concurrent creates of the same number race to the index and exactly
/// one wins; the loser's violation is translated to a CONFLICT (no application read-then-write
/// pre-check). The inherited <see cref="TenantOwnedEntity.RowVersion"/> gives optimistic concurrency
/// on edit (Req 22.4). Delete is a soft delete that stays traceable (Req 14.6).</para>
/// </summary>
public class Unit : TenantOwnedEntity
{
    /// <summary>
    /// Optional link to the hierarchy node (Floor/Building/Block/…) the unit sits under (Req 14.1).
    /// Null when the community does not model where the unit lives. When set it must reference a node
    /// in the SAME community — enforced in the service before persist.
    /// </summary>
    public Guid? HierarchyNodeId { get; set; }

    /// <summary>Human-readable unit number/label (e.g. "A-101", "Villa 7"). Required; unique within the community (Req 14.4).</summary>
    public string UnitNumber { get; set; } = string.Empty;

    /// <summary>
    /// Configurable unit-type code resolved against the <c>Unit_Type</c> master-data list (Req 14.2).
    /// Stored as the stable code, never a frozen enum.
    /// </summary>
    public string UnitType { get; set; } = string.Empty;

    /// <summary>Optional floor area. Nullable — not every community records it.</summary>
    public decimal? Area { get; set; }

    /// <summary>Optional free-text floor designation (e.g. "3", "Ground", "Mezzanine"). Nullable.</summary>
    public string? Floor { get; set; }

    /// <summary>Optional bedroom count. Nullable.</summary>
    public int? Bedrooms { get; set; }

    /// <summary>
    /// Configurable occupancy/availability status resolved against the <c>Unit_Status</c> list
    /// (available/occupied/vacant/under_maintenance/blocked, Req 14.3). Stored as the stable code.
    /// </summary>
    public string UnitStatus { get; set; } = string.Empty;

    /// <summary>
    /// Arbitrary community-specific attributes (Req 14.1), persisted as a JSON column. Free-form:
    /// the key/value set is not constrained by the schema so a community can capture its own fields.
    /// Never null; an empty map means "no custom attributes".
    /// </summary>
    public IDictionary<string, string> CustomAttributes { get; set; } = new Dictionary<string, string>();
}

/// <summary>
/// Well-known default <c>Unit_Status</c> codes (Req 14.3, 18.1, 18.3). These are the seeded
/// <c>Unit_Status</c> master-data codes, kept as constants so the move-in/move-out workflow
/// (Task 12.4) can set occupancy without a magic string. Like
/// <see cref="CommunityOS.Domain.Residents.ResidentVerificationStatuses"/> they are <b>data</b>, not
/// a behavioural enum — the status list stays editable; this is only a convenience for the known
/// codes the workflow sets (steering: no hardcoded literals).
/// </summary>
public static class UnitStatuses
{
    /// <summary>Default on a vacant-but-ready unit (Req 14.3).</summary>
    public const string Available = "available";

    /// <summary>Set by the Task 12.4 move-in workflow when a resident moves in (Req 18.1).</summary>
    public const string Occupied = "occupied";

    /// <summary>Set by the Task 12.4 move-out workflow when the last occupant leaves (Req 18.3).</summary>
    public const string Vacant = "vacant";

    /// <summary>Unit temporarily out of service (Req 14.3).</summary>
    public const string UnderMaintenance = "under_maintenance";

    /// <summary>Unit administratively blocked (Req 14.3).</summary>
    public const string Blocked = "blocked";
}
