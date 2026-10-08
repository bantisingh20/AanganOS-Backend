using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Residents;

/// <summary>
/// A parking slot in the community's parking layout master (Req 19.4, 40.1). Originally the
/// <b>minimal Phase 2</b> association (a labelled slot plus an optional owning
/// <see cref="AssignedUnitId"/>); Phase 6 (Task 24.1) <b>evolves</b> the same row into the richer
/// layout entry — a zone/level/number, a configurable <see cref="SlotType"/> and an active flag —
/// and moves the authoritative assignment onto the append-only <c>ParkingAssignment</c> history with
/// a DB-level "one active assignment per slot" guarantee (Req 40.3). The entity is extended, not
/// duplicated: the Phase 2 fields stay, so <c>ResidentAssetsService</c> keeps working unchanged.
///
/// <para><b>Layout (Req 40.1).</b> A slot optionally belongs to a <c>ParkingZone</c>
/// (<see cref="ZoneId"/>) and carries a free-text <see cref="Level"/> (e.g. "B1", "Ground"), a
/// <see cref="SlotNumber"/> within its zone/level, and a configurable <see cref="SlotType"/> resolved
/// against the editable <c>Parking_Slot_Type</c> list (covered / open / visitor / ev / accessible,
/// ...) — never a frozen enum. <see cref="Code"/> remains the community-unique operator-facing label.
/// <see cref="IsActive"/> lets an operator retire a slot from occupancy/assignment without deleting
/// it.</para>
///
/// <para><b>Assignment moved off this row (Req 40.2, 40.3).</b> <see cref="AssignedUnitId"/> is the
/// LEGACY Phase 2 single-column association the Assign operation set directly. The Phase 6 module no
/// longer treats it as the source of truth: the authoritative current assignment is the open
/// <c>ParkingAssignment</c> row (one active per slot, enforced by a filtered unique index on SQL
/// Server). The column is retained for backward compatibility (the Phase 2 reassignable-slot flow and
/// its reads) — ponytail: a slot therefore carries BOTH the legacy pointer and the new history; the
/// parking module reads/writes the history, the Phase 2 service reads/writes the pointer, and they
/// do not fight because the module never writes the legacy column.</para>
///
/// <para>Not <see cref="IUnitScoped"/>: a slot is community infrastructure an admin manages, not
/// resident self-view data pinned to one unit (same stance as <c>Asset</c>/<c>CustodyLocation</c>/the
/// ownership registers). Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global
/// filter, rowversion concurrency token, audit columns.</para>
/// </summary>
public class ParkingSlot : TenantOwnedEntity
{
    /// <summary>Human-readable slot code/label (e.g. "P1-23", "Basement B-07"). Required; unique within the community.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// The unit this slot is currently assigned to under the LEGACY Phase 2 single-column
    /// association, or null (Req 19.4). Retained for backward compatibility with the Phase 2
    /// reassignable-slot flow; the Phase 6 authoritative assignment lives on <c>ParkingAssignment</c>.
    /// </summary>
    public Guid? AssignedUnitId { get; set; }

    /// <summary>
    /// The parking zone this slot belongs to (Req 40.1), or null for a slot not grouped under a zone.
    /// Set at layout time; the zone anchors the slot's physical grouping for the occupancy dashboard.
    /// </summary>
    public Guid? ZoneId { get; set; }

    /// <summary>
    /// Free-text level/floor label within the layout (e.g. "B1", "B2", "Ground", "P3") (Req 40.1).
    /// Optional; combined with <see cref="ZoneId"/> and <see cref="SlotNumber"/> it describes where the
    /// slot physically is and groups the occupancy counts by level.
    /// </summary>
    public string? Level { get; set; }

    /// <summary>
    /// The slot's number within its zone/level (e.g. "23", "A-07") (Req 40.1). Optional free text —
    /// the community-unique identity is <see cref="Code"/>; this is the human-facing position label.
    /// </summary>
    public string? SlotNumber { get; set; }

    /// <summary>
    /// Configurable slot-type code resolved against the <c>Parking_Slot_Type</c> master-data list
    /// (covered / open / visitor / ev / accessible, ...) (Req 40.1). Stored as the stable code, never
    /// a frozen enum. Defaults to <see cref="ParkingSlotTypes.Open"/> on a layout slot.
    /// </summary>
    public string SlotType { get; set; } = ParkingSlotTypes.Open;

    /// <summary>
    /// Whether the slot is active in the layout (Req 40.1). An inactive slot is retained (traceable)
    /// but excluded from new assignments and from the occupancy dashboard's free/occupied counts.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Well-known default <c>Parking_Slot_Type</c> codes (Req 40.1). Seeded master-data codes kept as
/// constants so the service sets/compares the slot type without a magic string. <b>Data</b>, not a
/// behavioural enum — the type list is editable per community; these constants only name the seeded
/// defaults.
/// </summary>
public static class ParkingSlotTypes
{
    /// <summary>Default: an open (uncovered) slot.</summary>
    public const string Open = "open";

    /// <summary>A covered slot.</summary>
    public const string Covered = "covered";

    /// <summary>A visitor/guest slot (the pool guest parking draws from, Task 24.2).</summary>
    public const string Visitor = "visitor";

    /// <summary>An EV-charging slot (the EV reservation module keys off this type, Task 24.2).</summary>
    public const string Ev = "ev";

    /// <summary>An accessible / differently-abled reserved slot.</summary>
    public const string Accessible = "accessible";
}
