namespace CommunityOS.Application.Amenities;

/// <summary>
/// Amenity_Service (Task 23.1, Req 38.1–38.4): the CONFIGURATION side of the Amenities module — the
/// bookable <c>Amenity</c> master (configurable type/status, capacity, location), its bookable
/// <c>FacilitySlot</c> entries (time window + capacity), the per-amenity configurable
/// <c>BookingRule</c> set (eligibility / charge / approval / cancellation / refund / deposit) and
/// its <c>BlackoutDate</c> ranges — plus the tenant-scoped read surface. This is the single cohesive
/// seam for the Amenities area: the booking lifecycle with capacity concurrency (Task 23.2) and the
/// HTTP controllers (Task 24.3) extend this same interface in later tasks (mirrors how
/// <c>IMaintenanceService</c> grows across its tasks). 23.1 does NOT create bookings or enforce
/// capacity — it provisions the configuration the lifecycle rides.
///
/// <para><b>Amenity master (Req 38.1).</b> A Facility Manager with <c>Create</c> on <c>Amenity</c>
/// defines an amenity for an authorized community: the type/status validate against the editable
/// <c>Amenity_Type</c> / <c>Amenity_Status</c> lists (no frozen enums), the code is unique within the
/// community (a duplicate → CONFLICT via the DB unique index, no read-then-write), the status starts
/// <c>active</c>. Edit drives the editable details / lifecycle status (validated as an active status
/// code; no transition allow-list in 23.1). Reads are tenant-scoped (NOT <c>IUnitScoped</c> —
/// community infrastructure); the list rides the shared allow-listed query config + <c>AsNoTracking</c>;
/// a delete is a Soft_Delete. Mutations are audited.</para>
///
/// <para><b>Slots / rules / blackout dates (Req 38.2-38.4).</b> Slots, the booking-rule set and
/// blackout ranges are tenant-owned children of an amenity carrying the parent's community. A slot
/// stores a recurring daily window + capacity + the RowVersion the 23.2 lifecycle books under. The
/// booking-rule set is a per-amenity SINGLETON (<c>Unique(AmenityId)</c>) upserted via
/// <see cref="SetBookingRuleAsync"/>, with every value configurable (eligibility codes validated
/// against <c>Resident_Type</c>; numeric defaults from Config_Service — no hardcoding). A blackout
/// range is a half-open UTC period that makes the amenity unavailable; overlapping ranges are
/// allowed. All mutations are audited and tenant-scoped.</para>
/// </summary>
public interface IAmenityService
{
    // --- Amenity master (Req 38.1) ---------------------------------------------------------------

    /// <summary>
    /// Define a bookable amenity (Req 38.1). Requires <c>Create</c> on <c>Amenity</c> + an authorized
    /// community; validates the type against the configurable <c>Amenity_Type</c> list; enforces
    /// code uniqueness within the community via the DB unique index (a duplicate → CONFLICT, no
    /// read-then-write); sets the status to <c>active</c>; and validates a positive default capacity.
    /// Returns the created amenity. Audited.
    /// </summary>
    Task<AmenityDto> CreateAmenityAsync(CreateAmenityRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an amenity's editable details and/or lifecycle status (Req 38.1). Requires <c>Edit</c>
    /// on <c>Amenity</c> + an authorized community. Validates any supplied type/status against the
    /// configurable lists (an unknown code → VALIDATION_ERROR, nothing mutated). Records exactly once
    /// under the amenity <c>RowVersion</c> (a loser → CONFLICT). Returns the updated amenity. Audited.
    /// </summary>
    Task<AmenityDto> UpdateAmenityAsync(Guid amenityId, UpdateAmenityRequest request, CancellationToken cancellationToken = default);

    /// <summary>Fetch one amenity by id, tenant-scoped; unknown/foreign/soft-deleted id → null (Req 38.1).</summary>
    Task<AmenityDto?> GetAmenityAsync(Guid amenityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated/filtered/sorted/searchable amenity list (Req 38.1). Requires <c>View</c> on
    /// <c>Amenity</c>. Tenant-scoped by the global filter (soft-deleted amenities hidden); the
    /// allow-listed <c>ListQueryConfiguration</c> fixes the sort/filter/search surface;
    /// <c>AsNoTracking</c> projection, page size clamped by the shared policy — mirrors
    /// <c>ListAssetsAsync</c>.
    /// </summary>
    Task<CommunityOS.Contracts.Envelope.PagedData<AmenityDto>> ListAmenitiesAsync(
        CommunityOS.Application.Querying.ListQueryParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-delete (archive) an amenity (Req 38.1). Requires <c>Delete</c> on <c>Amenity</c> + an
    /// authorized community. The amenity is marked deleted (the row stays traceable, hidden from
    /// ordinary reads); the delete records under the amenity <c>RowVersion</c> (a loser → CONFLICT).
    /// An unknown/foreign id → not found. Audited.
    /// </summary>
    Task DeleteAmenityAsync(Guid amenityId, CancellationToken cancellationToken = default);

    // --- FacilitySlot (Req 38.2) -----------------------------------------------------------------

    /// <summary>
    /// Define a bookable facility slot for an amenity (Req 38.2). Requires <c>Create</c> on
    /// <c>FacilitySlot</c> + an authorized community (the amenity must be in it). Validates the daily
    /// window (end strictly after start, both in range), at least one weekday bit, and a positive
    /// capacity. The slot carries the amenity's community. Returns the created slot. Audited.
    /// </summary>
    Task<FacilitySlotDto> CreateFacilitySlotAsync(CreateFacilitySlotRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a facility slot's window / capacity / active flag (Req 38.2). Requires <c>Edit</c> on
    /// <c>FacilitySlot</c>. Re-validates the window and capacity when supplied; records exactly once
    /// under the slot <c>RowVersion</c> (a loser → CONFLICT). Returns the updated slot. Audited.
    /// </summary>
    Task<FacilitySlotDto> UpdateFacilitySlotAsync(Guid slotId, UpdateFacilitySlotRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// List an amenity's facility slots (Req 38.2). Requires <c>View</c> on <c>FacilitySlot</c>.
    /// Tenant-scoped; <c>AsNoTracking</c>; indexed read ordered by window start; an unknown/foreign
    /// amenity id → empty list.
    /// </summary>
    Task<IReadOnlyList<FacilitySlotDto>> GetAmenitySlotsAsync(Guid amenityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-delete (retire) a facility slot (Req 38.2). Requires <c>Delete</c> on <c>FacilitySlot</c>.
    /// The row stays traceable but is hidden from ordinary reads; the delete records under the slot
    /// <c>RowVersion</c> (a loser → CONFLICT). An unknown/foreign id → not found. Audited.
    /// </summary>
    Task DeleteFacilitySlotAsync(Guid slotId, CancellationToken cancellationToken = default);

    // --- BookingRule (Req 38.3) ------------------------------------------------------------------

    /// <summary>
    /// Configure (create or replace) an amenity's booking-rule set (Req 38.3). An amenity has ONE
    /// rule-set, so this is an upsert keyed on <c>Unique(AmenityId)</c>. Requires <c>Edit</c> on
    /// <c>Amenity</c> (rules are managed via the amenity's Edit grant) + an authorized community (the
    /// amenity must be in it). Validates each eligible resident-type code against the editable
    /// <c>Resident_Type</c> list, the non-negative money fields, the positive/ordered duration bounds
    /// and the 0..100 refund percent; unset advance-window / per-resident-cap fall back to the
    /// configured community defaults (never hardcoded). Records under the rule-set <c>RowVersion</c>
    /// (a loser → CONFLICT). Returns the stored rule-set. Audited.
    /// </summary>
    Task<BookingRuleDto> SetBookingRuleAsync(SetBookingRuleRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch an amenity's booking-rule set (Req 38.3). Requires <c>View</c> on <c>Amenity</c>.
    /// Tenant-scoped <c>AsNoTracking</c> read; an amenity with no configured rule-set (or an
    /// unknown/foreign id) → null.
    /// </summary>
    Task<BookingRuleDto?> GetBookingRuleAsync(Guid amenityId, CancellationToken cancellationToken = default);

    // --- BlackoutDate (Req 38.4) -----------------------------------------------------------------

    /// <summary>
    /// Define a blackout range making an amenity unavailable for booking (Req 38.4). Requires
    /// <c>Create</c> on <c>BlackoutDate</c> + an authorized community (the amenity must be in it).
    /// Validates <c>EndUtc &gt; StartUtc</c> and a non-empty reason. Overlapping ranges are allowed.
    /// The blackout carries the amenity's community. Returns the created blackout. Audited.
    /// </summary>
    Task<BlackoutDateDto> CreateBlackoutDateAsync(CreateBlackoutDateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// List an amenity's blackout ranges (Req 38.4). Requires <c>View</c> on <c>BlackoutDate</c>.
    /// Tenant-scoped; <c>AsNoTracking</c>; indexed read ordered by start instant; an unknown/foreign
    /// amenity id → empty list. The 23.2 lifecycle reads this set to reject bookings within a range.
    /// </summary>
    Task<IReadOnlyList<BlackoutDateDto>> GetAmenityBlackoutDatesAsync(Guid amenityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-delete (lift) a blackout range (Req 38.4). Requires <c>Delete</c> on <c>BlackoutDate</c>.
    /// The row stays traceable but is hidden from ordinary reads so the amenity becomes bookable in
    /// that window again; the delete records under the blackout <c>RowVersion</c> (a loser →
    /// CONFLICT). An unknown/foreign id → not found. Audited.
    /// </summary>
    Task DeleteBlackoutDateAsync(Guid blackoutId, CancellationToken cancellationToken = default);

    // --- Booking lifecycle + capacity concurrency (Task 23.2, Req 39.1-39.5) ---------------------

    /// <summary>
    /// Book a facility slot for a specific date on behalf of a resident (Req 39.1). Requires
    /// <c>Create</c> on <c>Booking</c> + an authorized community (the slot's amenity must be in it).
    /// Enforces the amenity's configurable <c>BookingRule</c>: the amenity must be bookable (active),
    /// the slot active and recurring on the date's weekday, the date within the advance-booking window
    /// (and not in the past), the resident's <c>Resident_Type</c> eligible, the date NOT within any
    /// active blackout range (Req 38.4 -> a blacked-out date is rejected), and the resident under the
    /// per-resident cap over the configured period.
    ///
    /// <para><b>Capacity concurrency (Req 39.2, 39.5).</b> A per-(slot, date) <c>SlotBookingCount</c>
    /// seat is claimed with an atomic DB-level guarded UPDATE
    /// (<c>SET BookedCount = BookedCount + 1 WHERE BookedCount &lt; Capacity</c>): exactly one of two
    /// requests racing the last seat wins; the loser gets a clean "slot full" VALIDATION_ERROR. No
    /// read-then-write over the count. The booking is then created <c>requested</c> (approval-required,
    /// Req 39.3) or <c>confirmed</c> (auto-confirm); if that save fails the claimed seat is released.</para>
    ///
    /// <para>Notifies the booking resident of the create/confirmation and, when approval is required,
    /// the amenity's approvers (Req 39.4) via Notification_Service (transactional outbox). Audited.</para>
    /// </summary>
    Task<BookingDto> CreateBookingAsync(CreateBookingRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approve a pending (<c>requested</c>) booking (Req 39.3). Requires <c>Approve</c> on
    /// <c>Booking</c> + an authorized community. Moves the booking <c>requested -&gt; confirmed</c>
    /// through the editable <c>Booking_Status_Transition</c> allow-list (an illegal move ->
    /// VALIDATION_ERROR, nothing mutated), stamps the approver + confirmation time, and records under
    /// the booking <c>RowVersion</c> (a double-approve loser -> CONFLICT). Notifies the booking
    /// resident of the confirmation (Req 39.4). An unknown/foreign id -> not found. Audited.
    /// </summary>
    Task<BookingDto> ApproveBookingAsync(Guid bookingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancel a booking (Req 39.4). A resident cancels only their OWN booking (resident self-scope
    /// filter); a Facility-Manager with the permission cancels any booking in their communities.
    /// Requires <c>Edit</c> on <c>Booking</c> + an authorized community. Enforces the rule's
    /// cancellation-notice window (too late -> VALIDATION_ERROR). Moves the booking to <c>cancelled</c>
    /// through the editable <c>Booking_Status_Transition</c> allow-list, RELEASES the claimed
    /// per-(slot, date) capacity seat back to the counter exactly once (guarded decrement under the
    /// booking <c>RowVersion</c> - a double-cancel loser -> CONFLICT), and notifies the booking
    /// resident (Req 39.4). An unknown/foreign id -> not found. Audited.
    /// </summary>
    Task<BookingDto> CancelBookingAsync(Guid bookingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch one booking by id (Req 39.1), tenant + resident-self-scoped; an unknown/foreign/
    /// out-of-scope/soft-deleted id -> null. A resident resolves only their own unit's bookings.
    /// </summary>
    Task<BookingDto?> GetBookingAsync(Guid bookingId, CancellationToken cancellationToken = default);

    // --- Resident events + capacity-bounded registration (Task 24.2, Req 41.4, 41.5) -------------

    /// <summary>
    /// Publish a resident event with a schedule and a capacity (Req 41.4). Requires <c>Create</c> on
    /// <c>ResidentEvent</c> + an authorized community. Validates a non-empty title, <c>EndsAtUtc &gt;
    /// StartsAtUtc</c> and a positive capacity; stores the event <c>published</c> with its organiser +
    /// host unit. Returns the created event. Audited.
    /// </summary>
    Task<ResidentEventDto> PublishResidentEventAsync(PublishResidentEventRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Register a resident for a published event when capacity remains (Req 41.5). Requires
    /// <c>Create</c> on <c>ResidentEvent</c> + an authorized community (the event must be in it). The
    /// event must be <c>published</c>. A seat is claimed with an atomic DB-level guarded UPDATE
    /// (<c>SET RegisteredCount = RegisteredCount + 1 WHERE RegisteredCount &lt; Capacity</c>): exactly
    /// one of two residents racing the last seat wins; once capacity is reached the registration is
    /// rejected with a clean VALIDATION_ERROR (Req 41.5) — no read-then-write over the count. The
    /// per-resident registration is unique (<c>Unique(ResidentEventId, ResidentId)</c>) so a duplicate
    /// → CONFLICT (and the claimed seat is released). Notifies the registering resident via the
    /// transactional outbox. Returns the registration. Audited.
    /// </summary>
    Task<ResidentEventRegistrationDto> RegisterForResidentEventAsync(RegisterForResidentEventRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancel a resident's event registration (Req 41.5), releasing its seat back to the event counter
    /// EXACTLY ONCE (guarded decrement under the registration <c>RowVersion</c> — a double-cancel loser
    /// → CONFLICT). Requires <c>Create</c> on <c>ResidentEvent</c> + an authorized community. A
    /// resident cancels only their OWN registration (resident self-scope filter). An unknown/foreign id
    /// → not found. Audited.
    /// </summary>
    Task CancelResidentEventRegistrationAsync(Guid registrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch one resident event by id (Req 41.4), tenant-scoped; an unknown/foreign/soft-deleted id →
    /// null.
    /// </summary>
    Task<ResidentEventDto?> GetResidentEventAsync(Guid residentEventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List a community's upcoming/published resident events (Req 41.4). Requires <c>View</c> on
    /// <c>ResidentEvent</c>; tenant-scoped <c>AsNoTracking</c> read riding the (CommunityId,
    /// StartsAtUtc) index, ordered by start; an unknown/foreign community → empty list.
    /// </summary>
    Task<IReadOnlyList<ResidentEventDto>> ListResidentEventsAsync(Guid communityId, CancellationToken cancellationToken = default);
}
