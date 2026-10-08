using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Parking;

/// <summary>
/// A recorded parking violation against a slot/vehicle (Req 40.4): an operator notes that a vehicle
/// was parked in breach of the parking rules — wrong slot, overstay, blocking, no permit — with a
/// configurable <see cref="ViolationType"/>, a free-text description, optional photo evidence (a
/// File_Service reference) and a lifecycle <see cref="Status"/>. A community-scoped operations record.
///
/// <para><b>Configurable type + status (steering: no hardcoding, Req 40.4).</b>
/// <see cref="ViolationType"/> resolves against the editable <c>Parking_Violation_Type</c> list
/// (wrong_slot / overstay / blocking / no_permit / unauthorized, ...) and <see cref="Status"/>
/// against <c>Parking_Violation_Status</c> (open / resolved / waived), starting
/// <see cref="ParkingViolationStatuses.Open"/> — both stable codes, never frozen enums.</para>
///
/// <para><b>Evidence via File_Service (Req 40.4, 7.x).</b> <see cref="EvidenceFileId"/> is an opaque
/// reference to a <c>StoredFile</c> the caller uploaded through File_Service (the same opaque-
/// reference pattern as <c>TicketAttachment.FileId</c>); the bytes live in the blob store and are
/// streamed only through the authorized File_Service download path, never a public URL. Optional —
/// a violation can be recorded without a photo.</para>
///
/// <para><b>Subject (Req 40.4).</b> A violation always names its <see cref="ParkingSlotId"/> and may
/// name the offending <see cref="VehicleId"/> (when the vehicle is a registered one) and/or the
/// resident <see cref="UnitId"/> the slot is assigned to (resolved from the slot's active assignment
/// at record time) so the resident can be notified.</para>
///
/// <para>Not <see cref="IUnitScoped"/>: a violation is a community operations record (an operator
/// records it, a resident is notified of it), not resident self-view data gated by the self-scope
/// filter — same stance as <c>EntryExitEvent</c> / <c>Incident</c>. Derives from
/// <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion concurrency token,
/// audit columns.</para>
/// </summary>
public class ParkingViolation : TenantOwnedEntity
{
    /// <summary>The parking slot the violation occurred at (Req 40.4). Required.</summary>
    public Guid ParkingSlotId { get; set; }

    /// <summary>
    /// The offending vehicle, when it is a registered <c>Vehicle</c> (Req 40.4), or null when the
    /// vehicle is unregistered/unknown (the description then carries the plate).
    /// </summary>
    public Guid? VehicleId { get; set; }

    /// <summary>
    /// The resident unit the slot was assigned to at record time (Req 40.4), or null when the slot
    /// was unassigned. Resolved from the slot's active <c>ParkingAssignment</c> so the responsible
    /// resident can be notified.
    /// </summary>
    public Guid? UnitId { get; set; }

    /// <summary>
    /// Configurable violation-type code resolved against the <c>Parking_Violation_Type</c>
    /// master-data list (Req 40.4). Stored as the stable code, never a frozen enum.
    /// </summary>
    public string ViolationType { get; set; } = string.Empty;

    /// <summary>Free-text description of the violation (Req 40.4). Required.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Opaque File_Service reference to the photo/document evidence (Req 40.4, 7.1), or null when no
    /// evidence was attached. The bytes are reached only through the authorized File_Service download.
    /// </summary>
    public Guid? EvidenceFileId { get; set; }

    /// <summary>
    /// Configurable lifecycle-status code resolved against the <c>Parking_Violation_Status</c>
    /// master-data list (Req 40.4). Starts <see cref="ParkingViolationStatuses.Open"/>. Stored as the
    /// stable code, never a frozen enum.
    /// </summary>
    public string Status { get; set; } = ParkingViolationStatuses.Open;

    /// <summary>When the violation was recorded (Req 40.4).</summary>
    public DateTime RecordedAtUtc { get; set; }
}

/// <summary>
/// Well-known default <c>Parking_Violation_Status</c> codes (Req 40.4). Seeded master-data codes kept
/// as constants so the service sets/compares status without a magic string. <b>Data</b>, not a
/// behavioural enum — the list is editable per community; these constants only name the seeded
/// defaults.
/// </summary>
public static class ParkingViolationStatuses
{
    /// <summary>Initial status on record (Req 40.4): the violation is open/unresolved.</summary>
    public const string Open = "open";

    /// <summary>The violation was resolved (vehicle moved / penalty applied).</summary>
    public const string Resolved = "resolved";

    /// <summary>The violation was waived (recorded in error / excused).</summary>
    public const string Waived = "waived";
}
