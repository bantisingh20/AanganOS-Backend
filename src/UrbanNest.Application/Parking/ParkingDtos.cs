namespace UrbanNest.Application.Parking;

// =================================================================================================
// Task 24.1 — parking layout + assignment + violations + occupancy (Req 40.1-40.5). The full
// parking module built on the minimal Phase 2 ParkingSlot association (Task 12.5). Guest parking /
// EV reservations (24.2) and the HTTP controllers (24.3) extend the same IParkingService seam later.
// Shapes mirror the Amenity / Maintenance DTO style. Controllers (24.3) wire acting ids from the
// caller's principal and upload violation evidence through File_Service before calling the service.
// =================================================================================================

// --- ParkingZone (Req 40.1) ---------------------------------------------------------------------

/// <summary>Read shape for a <c>ParkingZone</c> (Req 40.1).</summary>
/// <param name="Id">The zone id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="Code">Zone code/label, unique within the community.</param>
/// <param name="Name">Human-readable zone name.</param>
/// <param name="ZoneType">Configurable zone-type code (Parking_Zone_Type).</param>
/// <param name="Level">Optional level/floor, or null.</param>
public sealed record ParkingZoneDto(
    Guid Id,
    Guid CommunityId,
    string Code,
    string Name,
    string ZoneType,
    string? Level);

/// <summary>
/// Create a <c>ParkingZone</c> in the layout (Req 40.1). The service validates the type against the
/// editable <c>Parking_Zone_Type</c> list and enforces code uniqueness within the community via the
/// DB unique index (a duplicate → CONFLICT, no read-then-write).
/// </summary>
/// <param name="CommunityId">The owning community (must be authorized for the caller). Required.</param>
/// <param name="Code">Zone code/label (unique within the community). Required.</param>
/// <param name="Name">Human-readable zone name. Required.</param>
/// <param name="ZoneType">Zone-type code from the Parking_Zone_Type list. Required.</param>
/// <param name="Level">Optional level/floor.</param>
public sealed record CreateParkingZoneRequest(
    Guid CommunityId,
    string Code,
    string Name,
    string ZoneType,
    string? Level);

// --- ParkingSlot (Req 40.1) ---------------------------------------------------------------------

/// <summary>Read shape for a layout <c>ParkingSlot</c> with its current assignment (Req 40.1, 40.2).</summary>
/// <param name="Id">The slot id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="Code">Slot code/label, unique within the community.</param>
/// <param name="ZoneId">The zone the slot belongs to, or null.</param>
/// <param name="Level">Optional level/floor, or null.</param>
/// <param name="SlotNumber">Optional number within the zone/level, or null.</param>
/// <param name="SlotType">Configurable slot-type code (Parking_Slot_Type).</param>
/// <param name="IsActive">Whether the slot is active in the layout.</param>
/// <param name="IsOccupied">Whether the slot currently has an active assignment (Req 40.5).</param>
/// <param name="AssignedUnitId">The unit of the current active assignment, or null (Req 40.2).</param>
/// <param name="AssignedVisitorId">The visitor of the current active assignment, or null (Req 40.2).</param>
public sealed record ParkingSlotDetailDto(
    Guid Id,
    Guid CommunityId,
    string Code,
    Guid? ZoneId,
    string? Level,
    string? SlotNumber,
    string SlotType,
    bool IsActive,
    bool IsOccupied,
    Guid? AssignedUnitId,
    Guid? AssignedVisitorId);

/// <summary>
/// Define a <c>ParkingSlot</c> in the layout (Req 40.1). The slot starts unassigned and active. The
/// service validates the type against the editable <c>Parking_Slot_Type</c> list, the zone (when
/// supplied) belongs to the same community, and enforces code uniqueness within the community via the
/// DB unique index (a duplicate → CONFLICT, no read-then-write).
/// </summary>
/// <param name="CommunityId">The owning community (must be authorized for the caller). Required.</param>
/// <param name="Code">Slot code/label (unique within the community). Required.</param>
/// <param name="ZoneId">The zone the slot belongs to (must be in the same community), or null.</param>
/// <param name="Level">Optional level/floor label.</param>
/// <param name="SlotNumber">Optional number within the zone/level.</param>
/// <param name="SlotType">Slot-type code from the Parking_Slot_Type list. Required.</param>
public sealed record CreateParkingSlotLayoutRequest(
    Guid CommunityId,
    string Code,
    Guid? ZoneId,
    string? Level,
    string? SlotNumber,
    string SlotType);

/// <summary>
/// Update a slot's layout attributes (Req 40.1). All fields are optional — a null leaves the
/// corresponding value unchanged. The service validates any supplied type against the editable
/// <c>Parking_Slot_Type</c> list and any supplied zone belongs to the same community.
/// </summary>
/// <param name="ZoneId">New zone (same community), or null to leave unchanged. Use <see cref="ClearZone"/> to detach.</param>
/// <param name="ClearZone">When true, detaches the slot from any zone (overrides <see cref="ZoneId"/>).</param>
/// <param name="Level">New level, or null to leave unchanged.</param>
/// <param name="SlotNumber">New slot number, or null to leave unchanged.</param>
/// <param name="SlotType">New slot-type code (from Parking_Slot_Type), or null to leave unchanged.</param>
/// <param name="IsActive">New active flag (retire/restore), or null to leave unchanged.</param>
public sealed record UpdateParkingSlotLayoutRequest(
    Guid? ZoneId,
    bool ClearZone,
    string? Level,
    string? SlotNumber,
    string? SlotType,
    bool? IsActive);

// --- ParkingAssignment (Req 40.2, 40.3) ---------------------------------------------------------

/// <summary>Read shape for a <c>ParkingAssignment</c> entry (Req 40.2).</summary>
/// <param name="Id">The assignment id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="ParkingSlotId">The slot this assignment is for.</param>
/// <param name="UnitId">The assigned resident unit, or null for a visitor assignment.</param>
/// <param name="VisitorId">The assigned visitor, or null for a resident assignment.</param>
/// <param name="AssignmentType">Configurable assignment-type code (permanent / temporary / visitor).</param>
/// <param name="StartedAtUtc">When this assignment began.</param>
/// <param name="EndedAtUtc">When this assignment ended, or null while active.</param>
public sealed record ParkingAssignmentDto(
    Guid Id,
    Guid CommunityId,
    Guid ParkingSlotId,
    Guid? UnitId,
    Guid? VisitorId,
    string AssignmentType,
    DateTime StartedAtUtc,
    DateTime? EndedAtUtc);

/// <summary>
/// Assign a slot to a unit or visitor (Req 40.2, 40.3). The service closes the slot's prior ACTIVE
/// assignment (if any) and appends the new one in one transaction, so a slot never has two active
/// assignments; two concurrent assignments of the same slot race to the DB-level filtered unique
/// index and exactly one wins (the loser → CONFLICT). Exactly one of <see cref="UnitId"/> /
/// <see cref="VisitorId"/> must be supplied; the type is validated against the editable
/// <c>Parking_Assignment_Type</c> list and the subject must be in the slot's community.
/// </summary>
/// <param name="ParkingSlotId">The slot to assign (must be an active slot in an authorized community). Required.</param>
/// <param name="UnitId">The resident unit to assign to (permanent/temporary), or null for a visitor assignment.</param>
/// <param name="VisitorId">The visitor to assign to (visitor parking), or null for a resident assignment.</param>
/// <param name="AssignmentType">Assignment-type code from the Parking_Assignment_Type list. Required.</param>
public sealed record AssignParkingSlotRequest(
    Guid ParkingSlotId,
    Guid? UnitId,
    Guid? VisitorId,
    string AssignmentType);

// --- ParkingViolation (Req 40.4) ----------------------------------------------------------------

/// <summary>Read shape for a <c>ParkingViolation</c> (Req 40.4).</summary>
/// <param name="Id">The violation id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="ParkingSlotId">The slot the violation occurred at.</param>
/// <param name="VehicleId">The offending registered vehicle, or null.</param>
/// <param name="UnitId">The responsible resident unit (from the slot's active assignment), or null.</param>
/// <param name="ViolationType">Configurable violation-type code (Parking_Violation_Type).</param>
/// <param name="Description">Free-text description of the violation.</param>
/// <param name="EvidenceFileId">Opaque File_Service evidence reference, or null.</param>
/// <param name="Status">Configurable lifecycle-status code (open / resolved / waived).</param>
/// <param name="RecordedAtUtc">When the violation was recorded.</param>
public sealed record ParkingViolationDto(
    Guid Id,
    Guid CommunityId,
    Guid ParkingSlotId,
    Guid? VehicleId,
    Guid? UnitId,
    string ViolationType,
    string Description,
    Guid? EvidenceFileId,
    string Status,
    DateTime RecordedAtUtc);

/// <summary>
/// Record a <c>ParkingViolation</c> against a slot/vehicle (Req 40.4). The service validates the type
/// against the editable <c>Parking_Violation_Type</c> list, resolves the responsible resident unit
/// from the slot's active assignment, stores the optional File_Service evidence reference, and
/// notifies the responsible unit's residents via the transactional outbox. Starts <c>open</c>.
/// </summary>
/// <param name="ParkingSlotId">The slot the violation occurred at (must be in an authorized community). Required.</param>
/// <param name="VehicleId">The offending registered vehicle, or null when unregistered/unknown.</param>
/// <param name="ViolationType">Violation-type code from the Parking_Violation_Type list. Required.</param>
/// <param name="Description">Free-text description. Required.</param>
/// <param name="EvidenceFileId">Opaque File_Service reference to the evidence already uploaded by the caller, or null.</param>
public sealed record RecordParkingViolationRequest(
    Guid ParkingSlotId,
    Guid? VehicleId,
    string ViolationType,
    string Description,
    Guid? EvidenceFileId);

/// <summary>
/// Update a parking violation's lifecycle status (Req 40.4). The service validates the new status
/// against the editable <c>Parking_Violation_Status</c> list and records under the violation
/// RowVersion (a concurrency loser → CONFLICT).
/// </summary>
/// <param name="Status">New status code from the Parking_Violation_Status list. Required.</param>
public sealed record UpdateParkingViolationStatusRequest(string Status);

// --- Occupancy dashboard (Req 40.5) -------------------------------------------------------------

/// <summary>
/// One occupancy bucket in the parking dashboard (Req 40.5): the free/occupied counts for a
/// zone/level grouping, computed in the database. Totals exclude inactive slots.
/// </summary>
/// <param name="ZoneId">The zone this bucket is for, or null for slots not grouped under a zone.</param>
/// <param name="Level">The level/floor this bucket is for, or null when unset.</param>
/// <param name="TotalSlots">Total active slots in the bucket.</param>
/// <param name="OccupiedSlots">Active slots with an active assignment.</param>
/// <param name="FreeSlots">Active slots with no active assignment (<c>TotalSlots - OccupiedSlots</c>).</param>
public sealed record ParkingOccupancyBucketDto(
    Guid? ZoneId,
    string? Level,
    int TotalSlots,
    int OccupiedSlots,
    int FreeSlots);

/// <summary>
/// The parking occupancy dashboard for a community (Req 40.5): the overall free/occupied totals plus
/// the per-(zone, level) buckets, all computed in the database and scoped to the caller's authorized
/// communities.
/// </summary>
/// <param name="CommunityId">The community the occupancy is reported for.</param>
/// <param name="TotalSlots">Total active slots in the community.</param>
/// <param name="OccupiedSlots">Active slots with an active assignment.</param>
/// <param name="FreeSlots">Active slots with no active assignment.</param>
/// <param name="Buckets">The per-(zone, level) breakdown.</param>
public sealed record ParkingOccupancyDto(
    Guid CommunityId,
    int TotalSlots,
    int OccupiedSlots,
    int FreeSlots,
    IReadOnlyList<ParkingOccupancyBucketDto> Buckets);

// =================================================================================================
// Task 24.2 — guest parking (Req 41.1), EV charging reservations (Req 41.2, 41.3). Guest parking is
// a visitor-type ParkingAssignment on the existing one-active-per-slot register (no new entity); the
// request below is a thin, resident-facing shape over AssignParkingSlotRequest with the time-bound +
// configurable guest rules applied. EV reservations are a dedicated EvReservation with the SAME
// DB-level one-active-per-slot guarantee as parking assignment. The HTTP controllers (24.3) wire the
// acting resident/unit from the caller's self-scoped principal before calling the service.
// =================================================================================================

// --- Guest parking (Req 41.1) -------------------------------------------------------------------

/// <summary>
/// Request guest parking for an APPROVED visitor (Req 41.1). The service validates the slot is an
/// active <c>visitor</c>-type slot and currently free, the visitor is in the slot's community and
/// APPROVED (admittable), and the unit is within its configurable concurrent-guest-slot cap; it then
/// appends a <c>visitor</c>-type <c>ParkingAssignment</c> on the one-active-per-slot register (the
/// loser of a concurrent grab → CONFLICT) with a time-bound end from the configurable max guest
/// duration, and notifies the requesting resident.
/// </summary>
/// <param name="ParkingSlotId">The visitor-type parking slot to assign (must be active + free, in an authorized community). Required.</param>
/// <param name="VisitorId">The approved visitor the guest slot is for (must be same-community + approved). Required.</param>
/// <param name="RequestedByResidentId">The resident requesting the guest slot (the notification recipient + per-unit cap subject). Required.</param>
/// <param name="UnitId">The requesting resident's unit (the concurrent-guest-slot cap scope). Required.</param>
public sealed record RequestGuestParkingRequest(
    Guid ParkingSlotId,
    Guid VisitorId,
    Guid RequestedByResidentId,
    Guid UnitId);

// --- EV charging reservations (Req 41.2, 41.3) --------------------------------------------------

/// <summary>Read shape for an <c>EvReservation</c> (Req 41.2).</summary>
/// <param name="Id">The reservation id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="ParkingSlotId">The EV-charging slot reserved.</param>
/// <param name="ResidentId">The reserving resident.</param>
/// <param name="UnitId">The reserving resident's unit.</param>
/// <param name="ReservedFromUtc">When the reserved window begins.</param>
/// <param name="ReservedUntilUtc">When the reserved window ends.</param>
/// <param name="EndedAtUtc">When the reservation was released/completed, or null while active.</param>
/// <param name="Status">Configurable status code (reserved / charging / completed / cancelled).</param>
/// <param name="EnergyKwh">The captured charging consumption in kWh, or null.</param>
public sealed record EvReservationDto(
    Guid Id,
    Guid CommunityId,
    Guid ParkingSlotId,
    Guid ResidentId,
    Guid UnitId,
    DateTime ReservedFromUtc,
    DateTime ReservedUntilUtc,
    DateTime? EndedAtUtc,
    string Status,
    decimal? EnergyKwh);

/// <summary>
/// Reserve an EV-charging slot for a time window (Req 41.2, 41.3). The service validates the slot is
/// an active <c>ev</c>-type slot in an authorized community, the window is non-empty and within the
/// configurable max session duration, and the unit is within its configurable concurrent-reservation
/// cap; it then inserts the active <c>EvReservation</c> — the DB-level filtered unique index on the
/// active row makes two concurrent reservations of the SAME slot resolve to exactly one winner (the
/// loser → CONFLICT, no read-then-write), and notifies the reserving resident.
/// </summary>
/// <param name="ParkingSlotId">The EV-charging slot to reserve (must be an active ev-type slot in an authorized community). Required.</param>
/// <param name="ResidentId">The reserving resident (the notification recipient + per-unit cap subject). Required.</param>
/// <param name="UnitId">The reserving resident's unit (the concurrent-reservation cap scope). Required.</param>
/// <param name="ReservedFromUtc">When the reserved charging window begins. Required.</param>
/// <param name="ReservedUntilUtc">When the reserved charging window ends (after From, within the max duration). Required.</param>
public sealed record ReserveEvSlotRequest(
    Guid ParkingSlotId,
    Guid ResidentId,
    Guid UnitId,
    DateTime ReservedFromUtc,
    DateTime ReservedUntilUtc);

/// <summary>
/// Complete or cancel an active EV reservation (Req 41.2), closing its active row (the EV slot becomes
/// free) and optionally capturing the session consumption. The service records exactly once under the
/// reservation RowVersion (a concurrent double-close loser → CONFLICT).
/// </summary>
/// <param name="Status">Terminal status to close with — <c>completed</c> or <c>cancelled</c> (from the Ev_Reservation_Status list). Required.</param>
/// <param name="EnergyKwh">The captured charging consumption in kWh, or null when not metered. Non-negative.</param>
public sealed record CloseEvReservationRequest(
    string Status,
    decimal? EnergyKwh);
