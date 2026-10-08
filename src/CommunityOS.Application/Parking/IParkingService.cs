namespace CommunityOS.Application.Parking;

/// <summary>
/// Parking_Service (Task 24.1, Req 40.1–40.5): the cohesive seam for the parking module — the parking
/// LAYOUT (<c>ParkingZone</c>s + <c>ParkingSlot</c>s), slot ASSIGNMENT with a DB-level one-active-
/// assignment-per-slot guarantee, parking VIOLATIONS (with File_Service evidence + a resident notice),
/// and the tenant-scoped OCCUPANCY dashboard. Built on the minimal Phase 2 <c>ParkingSlot</c>
/// association (Task 12.5), which it evolves rather than duplicates. Guest parking / EV reservations /
/// resident events (Task 24.2) and the HTTP controllers (Task 24.3) extend this same interface later
/// (mirrors how <c>IAmenityService</c> / <c>IMaintenanceService</c> grow across their tasks).
///
/// <para><b>Layout (Req 40.1).</b> A user with <c>Create</c> on <c>ParkingZone</c> / <c>ParkingSlot</c>
/// defines zones and slots for an authorized community: types validate against the editable
/// <c>Parking_Zone_Type</c> / <c>Parking_Slot_Type</c> lists (no frozen enums), codes are unique
/// within the community (a duplicate → CONFLICT via the DB unique index, no read-then-write). Reads
/// are tenant-scoped (NOT <c>IUnitScoped</c> — community infrastructure).</para>
///
/// <para><b>Assignment, one-active-per-slot (Req 40.2, 40.3 — the headline invariant).</b>
/// <see cref="AssignSlotAsync"/> appends a <c>ParkingAssignment</c> (permanent / temporary / visitor)
/// and closes the slot's prior active row in ONE transaction, so a slot never has two active
/// assignments. Two concurrent assignments of the SAME slot race to the DB-level filtered unique index
/// on the active row and exactly one wins — the loser maps to CONFLICT via <c>UniqueConstraintGuard</c>,
/// never a read-then-write check. <see cref="ReleaseSlotAsync"/> closes the active row (the slot
/// becomes free). Requires <c>Assign</c> on <c>ParkingAssignment</c>.</para>
///
/// <para><b>Violations (Req 40.4).</b> <see cref="RecordViolationAsync"/> stores a violation with its
/// slot, optional vehicle, configurable type, description and an OPTIONAL File_Service evidence
/// reference (the opaque-reference pattern — the bytes are reached only through File_Service), resolves
/// the responsible resident unit from the slot's active assignment, and notifies that unit's residents
/// via the Notification_Service transactional outbox. Requires <c>Create</c> on <c>ParkingViolation</c>.
/// All mutations are audited.</para>
///
/// <para><b>Occupancy (Req 40.5).</b> <see cref="GetOccupancyAsync"/> returns free/occupied counts
/// (overall + per zone/level) computed in the DATABASE (a grouped projection over active slots and
/// their active assignments — no in-memory table scan), scoped to the caller's authorized communities.
/// Requires <c>View</c> on <c>ParkingSlot</c>.</para>
/// </summary>
public interface IParkingService
{
    // --- Layout: zones (Req 40.1) ----------------------------------------------------------------

    /// <summary>
    /// Define a parking zone (Req 40.1). Requires <c>Create</c> on <c>ParkingZone</c> + an authorized
    /// community; validates the type against the configurable <c>Parking_Zone_Type</c> list; enforces
    /// code uniqueness within the community via the DB unique index (a duplicate → CONFLICT, no
    /// read-then-write). Returns the created zone. Audited.
    /// </summary>
    Task<ParkingZoneDto> CreateZoneAsync(CreateParkingZoneRequest request, CancellationToken cancellationToken = default);

    /// <summary>List a community's parking zones (Req 40.1). Requires <c>View</c> on <c>ParkingZone</c>; tenant-scoped <c>AsNoTracking</c> read ordered by code; an unknown/foreign community → empty list.</summary>
    Task<IReadOnlyList<ParkingZoneDto>> ListZonesAsync(Guid communityId, CancellationToken cancellationToken = default);

    // --- Layout: slots (Req 40.1) ----------------------------------------------------------------

    /// <summary>
    /// Define a parking slot in the layout (Req 40.1), initially unassigned and active. Requires
    /// <c>Create</c> on <c>ParkingSlot</c> + an authorized community; validates the type against the
    /// configurable <c>Parking_Slot_Type</c> list and that the zone (when supplied) is in the same
    /// community; enforces code uniqueness within the community via the DB unique index (a duplicate →
    /// CONFLICT, no read-then-write). Returns the created slot. Audited.
    /// </summary>
    Task<ParkingSlotDetailDto> CreateSlotAsync(CreateParkingSlotLayoutRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a slot's layout attributes (zone / level / number / type / active flag) (Req 40.1).
    /// Requires <c>Edit</c> on <c>ParkingSlot</c> + an authorized community. Validates any supplied
    /// type/zone; records exactly once under the slot <c>RowVersion</c> (a loser → CONFLICT). Returns
    /// the updated slot (with its current assignment). An unknown/foreign id → not found. Audited.
    /// </summary>
    Task<ParkingSlotDetailDto> UpdateSlotAsync(Guid parkingSlotId, UpdateParkingSlotLayoutRequest request, CancellationToken cancellationToken = default);

    /// <summary>Fetch one slot by id with its current assignment (Req 40.1, 40.2), tenant-scoped; an unknown/foreign/soft-deleted id → null.</summary>
    Task<ParkingSlotDetailDto?> GetSlotAsync(Guid parkingSlotId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List a community's parking slots with their current assignment (Req 40.1, 40.2). Requires
    /// <c>View</c> on <c>ParkingSlot</c>; tenant-scoped <c>AsNoTracking</c> read riding the layout
    /// index, ordered by zone/level/code; an unknown/foreign community → empty list.
    /// </summary>
    Task<IReadOnlyList<ParkingSlotDetailDto>> ListSlotsAsync(Guid communityId, CancellationToken cancellationToken = default);

    // --- Assignment: one-active-per-slot (Req 40.2, 40.3) ----------------------------------------

    /// <summary>
    /// Assign a slot to a unit or visitor (Req 40.2, 40.3). Requires <c>Assign</c> on
    /// <c>ParkingAssignment</c> + an authorized community (the slot must be in it). Validates the type
    /// against the configurable <c>Parking_Assignment_Type</c> list, that exactly one of unit/visitor
    /// is supplied and the subject is in the slot's community, and that the slot is active. Closes the
    /// slot's prior ACTIVE assignment (if any) and appends the new one in one transaction; the DB-level
    /// filtered unique index on the active row makes two concurrent assignments of the same slot
    /// resolve to exactly one winner (the loser → CONFLICT, no read-then-write). Returns the new
    /// active assignment. Audited.
    /// </summary>
    Task<ParkingAssignmentDto> AssignSlotAsync(AssignParkingSlotRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Release a slot's current assignment (Req 40.2, 40.3). Requires <c>Assign</c> on
    /// <c>ParkingAssignment</c> + an authorized community. Closes the slot's active assignment row
    /// (stamps its EndedAtUtc) so the slot becomes free; records exactly once under the assignment
    /// <c>RowVersion</c> (a concurrent double-release loser → CONFLICT). An unknown slot or a slot with
    /// no active assignment → not found. Audited.
    /// </summary>
    Task ReleaseSlotAsync(Guid parkingSlotId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List a slot's assignment history in chronological order (Req 40.2). Requires <c>View</c> on
    /// <c>ParkingAssignment</c>; tenant-scoped <c>AsNoTracking</c> read riding the per-slot index; an
    /// unknown/foreign slot → empty list.
    /// </summary>
    Task<IReadOnlyList<ParkingAssignmentDto>> ListAssignmentsForSlotAsync(Guid parkingSlotId, CancellationToken cancellationToken = default);

    // --- Violations (Req 40.4) -------------------------------------------------------------------

    /// <summary>
    /// Record a parking violation (Req 40.4). Requires <c>Create</c> on <c>ParkingViolation</c> + an
    /// authorized community (the slot must be in it). Validates the type against the configurable
    /// <c>Parking_Violation_Type</c> list, resolves the responsible resident unit from the slot's
    /// active assignment, stores the optional File_Service evidence reference, starts the violation
    /// <c>open</c>, and notifies the responsible unit's residents via the transactional outbox.
    /// Returns the created violation. Audited.
    /// </summary>
    Task<ParkingViolationDto> RecordViolationAsync(RecordParkingViolationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a parking violation's lifecycle status (Req 40.4). Requires <c>Edit</c> on
    /// <c>ParkingViolation</c> + an authorized community. Validates the new status against the
    /// configurable <c>Parking_Violation_Status</c> list; records exactly once under the violation
    /// <c>RowVersion</c> (a loser → CONFLICT). An unknown/foreign id → not found. Audited.
    /// </summary>
    Task<ParkingViolationDto> UpdateViolationStatusAsync(Guid violationId, UpdateParkingViolationStatusRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// List a slot's violations in reverse-chronological order (Req 40.4). Requires <c>View</c> on
    /// <c>ParkingViolation</c>; tenant-scoped <c>AsNoTracking</c> read riding the per-slot index; an
    /// unknown/foreign slot → empty list.
    /// </summary>
    Task<IReadOnlyList<ParkingViolationDto>> ListViolationsForSlotAsync(Guid parkingSlotId, CancellationToken cancellationToken = default);

    // --- Occupancy dashboard (Req 40.5) ----------------------------------------------------------

    /// <summary>
    /// Return the parking occupancy dashboard for a community (Req 40.5). Requires <c>View</c> on
    /// <c>ParkingSlot</c> + an authorized community. The free/occupied counts (overall + per
    /// zone/level) are computed in the database over active slots and their active assignments — no
    /// in-memory scan — and are scoped to the caller's authorized communities by the global tenant
    /// filter.
    /// </summary>
    Task<ParkingOccupancyDto> GetOccupancyAsync(Guid communityId, CancellationToken cancellationToken = default);

    // --- Guest parking (Task 24.2, Req 41.1) -----------------------------------------------------

    /// <summary>
    /// Request guest parking for an APPROVED visitor (Req 41.1). Requires <c>Assign</c> on
    /// <c>ParkingAssignment</c> (guest parking IS a visitor-type assignment — it reuses the same
    /// grant + register, not a new resource) + an authorized community (the slot must be in it).
    /// Validates the slot is an ACTIVE <c>visitor</c>-type slot and currently FREE, the visitor is in
    /// the slot's community and APPROVED (admittable, Req 24.3), and the unit is within its
    /// configurable concurrent-guest-slot cap (<c>parking.guest.maxConcurrentPerUnit</c>, platform
    /// fallback). Appends a <c>visitor</c>-type <c>ParkingAssignment</c> with a time-bound end from the
    /// configurable max guest duration (<c>parking.guest.maxDurationHours</c>); the DB-level filtered
    /// unique index on the active row makes two concurrent grabs of the same slot resolve to exactly
    /// one winner (the loser → CONFLICT, no read-then-write). Notifies the requesting resident via the
    /// transactional outbox. Returns the new active assignment. Audited.
    /// </summary>
    Task<ParkingAssignmentDto> RequestGuestParkingAsync(RequestGuestParkingRequest request, CancellationToken cancellationToken = default);

    // --- EV charging reservations (Task 24.2, Req 41.2, 41.3) ------------------------------------

    /// <summary>
    /// Reserve an EV-charging slot for a time window (Req 41.2, 41.3). Requires <c>Create</c> on
    /// <c>EvReservation</c> + an authorized community (the slot must be in it). Validates the slot is
    /// an ACTIVE <c>ev</c>-type slot, the window is non-empty and within the configurable max session
    /// duration (<c>parking.ev.maxReservationHours</c>, platform fallback), and the unit is within its
    /// configurable concurrent-reservation cap (<c>parking.ev.maxConcurrentPerUnit</c>). Inserts the
    /// active reservation; the DB-level filtered unique index on <c>(ParkingSlotId) WHERE EndedAtUtc
    /// IS NULL</c> makes two concurrent reservations of the SAME slot resolve to exactly one winner —
    /// the loser's unique violation → CONFLICT via <c>UniqueConstraintGuard</c>, never a read-then-write
    /// check (Req 41.3). Notifies the reserving resident via the transactional outbox. Returns the
    /// created reservation. Audited.
    /// </summary>
    Task<EvReservationDto> ReserveEvSlotAsync(ReserveEvSlotRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Complete or cancel an active EV reservation (Req 41.2). Requires <c>Edit</c> on
    /// <c>EvReservation</c> + an authorized community. Validates the terminal status against the
    /// editable <c>Ev_Reservation_Status</c> list, closes the active row (stamps <c>EndedAtUtc</c> so
    /// the EV slot becomes free), and optionally captures the session consumption; records exactly
    /// once under the reservation <c>RowVersion</c> (a concurrent double-close loser → CONFLICT). An
    /// unknown/foreign/already-closed id → not found. Returns the closed reservation. Audited.
    /// </summary>
    Task<EvReservationDto> CloseEvReservationAsync(Guid reservationId, CloseEvReservationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// List an EV slot's reservation history in reverse-chronological order (Req 41.2). Requires
    /// <c>View</c> on <c>EvReservation</c>; tenant-scoped <c>AsNoTracking</c> read riding the per-slot
    /// index; an unknown/foreign slot → empty list.
    /// </summary>
    Task<IReadOnlyList<EvReservationDto>> ListEvReservationsForSlotAsync(Guid parkingSlotId, CancellationToken cancellationToken = default);
}
