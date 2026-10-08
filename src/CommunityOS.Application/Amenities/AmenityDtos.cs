namespace CommunityOS.Application.Amenities;

// =================================================================================================
// Task 23.1 — amenity master + slots + rules + blackout dates (Req 38.1-38.4). The CONFIGURATION
// side of the Amenities module; the booking lifecycle (23.2) and HTTP controllers (24.3) extend the
// same IAmenityService seam later. Shapes mirror the Maintenance DTOs' style.
// =================================================================================================

/// <summary>Read shape for a bookable <c>Amenity</c> (Req 38.1).</summary>
/// <param name="Id">The amenity id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="Name">Human-readable amenity name/label.</param>
/// <param name="Code">Short operator-facing code, unique within the community.</param>
/// <param name="AmenityType">Configurable amenity-type code (Amenity_Type).</param>
/// <param name="Status">Configurable lifecycle-status code (active on creation).</param>
/// <param name="Location">The amenity's physical location, or null.</param>
/// <param name="Description">Optional free-text description, or null.</param>
/// <param name="DefaultCapacity">The amenity-level default concurrent-booking capacity.</param>
/// <param name="CreatedAtUtc">When the amenity was created.</param>
public sealed record AmenityDto(
    Guid Id,
    Guid CommunityId,
    string Name,
    string Code,
    string AmenityType,
    string Status,
    string? Location,
    string? Description,
    int DefaultCapacity,
    DateTime CreatedAtUtc);

/// <summary>
/// Create a bookable <c>Amenity</c> (Req 38.1). The caller supplies the owning community, a name, a
/// community-unique code, a configurable type and the default capacity; the service validates the
/// type against the editable <c>Amenity_Type</c> list, enforces code uniqueness within the community
/// via the DB unique index (a duplicate → CONFLICT, no read-then-write), and sets the status to
/// <c>active</c>.
/// </summary>
/// <param name="CommunityId">The owning community (must be authorized for the caller). Required.</param>
/// <param name="Name">Human-readable amenity name/label. Required.</param>
/// <param name="Code">Short operator-facing code (unique within the community). Required.</param>
/// <param name="AmenityType">Amenity-type code from the Amenity_Type list. Required.</param>
/// <param name="Location">Optional physical location.</param>
/// <param name="Description">Optional description.</param>
/// <param name="DefaultCapacity">Default concurrent-booking capacity (positive).</param>
public sealed record CreateAmenityRequest(
    Guid CommunityId,
    string Name,
    string Code,
    string AmenityType,
    string? Location,
    string? Description,
    int DefaultCapacity);

/// <summary>
/// Update an amenity's editable details and/or lifecycle status (Req 38.1). All fields are optional —
/// a null leaves the corresponding value unchanged. The service validates any supplied type against
/// the editable <c>Amenity_Type</c> list and any supplied <see cref="Status"/> against the editable
/// <c>Amenity_Status</c> list (an unknown code → VALIDATION_ERROR, nothing mutated). 23.1 validates
/// the status as a member of the list; it does not enforce a transition allow-list.
/// </summary>
/// <param name="Name">New name, or null to leave unchanged.</param>
/// <param name="AmenityType">New amenity-type code (from Amenity_Type), or null to leave unchanged.</param>
/// <param name="Status">New lifecycle-status code (from Amenity_Status), or null to leave unchanged.</param>
/// <param name="Location">New location, or null to leave unchanged.</param>
/// <param name="Description">New description, or null to leave unchanged.</param>
/// <param name="DefaultCapacity">New default capacity (positive), or null to leave unchanged.</param>
public sealed record UpdateAmenityRequest(
    string? Name,
    string? AmenityType,
    string? Status,
    string? Location,
    string? Description,
    int? DefaultCapacity);

// --- FacilitySlot (Req 38.2) --------------------------------------------------------------------

/// <summary>Read shape for a bookable <c>FacilitySlot</c> (Req 38.2).</summary>
/// <param name="Id">The slot id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="AmenityId">The amenity this slot belongs to.</param>
/// <param name="Name">Optional slot label, or null.</param>
/// <param name="StartMinutesOfDay">Start of the daily window, minutes since midnight (0..1439).</param>
/// <param name="EndMinutesOfDay">End of the daily window, minutes since midnight (1..1440).</param>
/// <param name="DaysOfWeekMask">Bitmask of weekdays the slot recurs on (bit 0 = Sunday).</param>
/// <param name="Capacity">Concurrent-booking capacity for the slot.</param>
/// <param name="IsActive">Whether the slot is active/offered for booking.</param>
/// <param name="CreatedAtUtc">When the slot was created.</param>
public sealed record FacilitySlotDto(
    Guid Id,
    Guid CommunityId,
    Guid AmenityId,
    string? Name,
    int StartMinutesOfDay,
    int EndMinutesOfDay,
    int DaysOfWeekMask,
    int Capacity,
    bool IsActive,
    DateTime CreatedAtUtc);

/// <summary>
/// Define a bookable <c>FacilitySlot</c> for an amenity (Req 38.2). The caller supplies the recurring
/// daily window (start/end minutes-of-day), the weekday bitmask and the capacity. The service
/// validates the window (end strictly after start, both in range), that at least one weekday bit is
/// set, and a positive capacity. The slot carries the amenity's community.
/// </summary>
/// <param name="AmenityId">The amenity the slot belongs to (must be in an authorized community). Required.</param>
/// <param name="Name">Optional slot label.</param>
/// <param name="StartMinutesOfDay">Start of the daily window, minutes since midnight (0..1439).</param>
/// <param name="EndMinutesOfDay">End of the daily window, minutes since midnight (1..1440, &gt; start).</param>
/// <param name="DaysOfWeekMask">Bitmask of weekdays the slot recurs on (at least one bit set).</param>
/// <param name="Capacity">Concurrent-booking capacity (positive).</param>
public sealed record CreateFacilitySlotRequest(
    Guid AmenityId,
    string? Name,
    int StartMinutesOfDay,
    int EndMinutesOfDay,
    int DaysOfWeekMask,
    int Capacity);

/// <summary>
/// Update a <c>FacilitySlot</c>'s window / capacity / active flag (Req 38.2). All fields are optional —
/// a null leaves the corresponding value unchanged. The service re-validates the window and capacity
/// when supplied.
/// </summary>
/// <param name="Name">New label, or null to leave unchanged.</param>
/// <param name="StartMinutesOfDay">New window start, or null to leave unchanged.</param>
/// <param name="EndMinutesOfDay">New window end, or null to leave unchanged.</param>
/// <param name="DaysOfWeekMask">New weekday bitmask, or null to leave unchanged.</param>
/// <param name="Capacity">New capacity (positive), or null to leave unchanged.</param>
/// <param name="IsActive">New active flag (pause/resume), or null to leave unchanged.</param>
public sealed record UpdateFacilitySlotRequest(
    string? Name,
    int? StartMinutesOfDay,
    int? EndMinutesOfDay,
    int? DaysOfWeekMask,
    int? Capacity,
    bool? IsActive);

// --- BookingRule (Req 38.3) ---------------------------------------------------------------------

/// <summary>Read shape for an amenity's configurable <c>BookingRule</c> set (Req 38.3).</summary>
/// <param name="Id">The rule-set id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="AmenityId">The amenity these rules apply to.</param>
/// <param name="AllowedResidentTypes">Comma-separated eligible Resident_Type codes, or null for all.</param>
/// <param name="AdvanceBookingWindowDays">Advance-booking window in days, or null for no limit.</param>
/// <param name="MinDurationMinutes">Minimum booking duration in minutes, or null.</param>
/// <param name="MaxDurationMinutes">Maximum booking duration in minutes, or null.</param>
/// <param name="MaxBookingsPerResident">Max bookings per resident in the period, or null.</param>
/// <param name="MaxBookingsPeriodDays">The per-resident cap window in days, or null for all time.</param>
/// <param name="RequiresApproval">Whether a booking requires approval before confirmation.</param>
/// <param name="BookingCharge">The booking charge (non-negative).</param>
/// <param name="SecurityDeposit">The refundable deposit (non-negative).</param>
/// <param name="CancellationNoticeHours">Refund-eligible cancellation notice in hours, or null.</param>
/// <param name="RefundPercentOnCancellation">Refund percent (0..100) within notice, or null.</param>
public sealed record BookingRuleDto(
    Guid Id,
    Guid CommunityId,
    Guid AmenityId,
    string? AllowedResidentTypes,
    int? AdvanceBookingWindowDays,
    int? MinDurationMinutes,
    int? MaxDurationMinutes,
    int? MaxBookingsPerResident,
    int? MaxBookingsPeriodDays,
    bool RequiresApproval,
    decimal BookingCharge,
    decimal SecurityDeposit,
    int? CancellationNoticeHours,
    decimal? RefundPercentOnCancellation);

/// <summary>
/// Configure (create or replace) an amenity's <c>BookingRule</c> set (Req 38.3). An amenity has ONE
/// rule-set, so this is an upsert: the service creates the row the first time and updates it
/// thereafter. The service validates each eligible resident-type code against the editable
/// <c>Resident_Type</c> list, the non-negative money fields, the positive/ordered duration bounds and
/// the 0..100 refund percent. Unset numeric fields fall back to the configured community defaults
/// (advance window, per-resident cap) via Config_Service — never a hardcoded value.
/// </summary>
/// <param name="AmenityId">The amenity to configure (must be in an authorized community). Required.</param>
/// <param name="AllowedResidentTypes">Comma-separated eligible Resident_Type codes; null/empty = all eligible.</param>
/// <param name="AdvanceBookingWindowDays">Advance window in days (non-negative); null falls back to the community default.</param>
/// <param name="MinDurationMinutes">Minimum duration in minutes (positive), or null.</param>
/// <param name="MaxDurationMinutes">Maximum duration in minutes (positive, &gt;= min), or null.</param>
/// <param name="MaxBookingsPerResident">Max bookings per resident in the period (positive); null falls back to the community default.</param>
/// <param name="MaxBookingsPeriodDays">The per-resident cap window in days (positive), or null for all time.</param>
/// <param name="RequiresApproval">Whether a booking requires approval before confirmation.</param>
/// <param name="BookingCharge">The booking charge (non-negative).</param>
/// <param name="SecurityDeposit">The refundable deposit (non-negative).</param>
/// <param name="CancellationNoticeHours">Refund-eligible cancellation notice in hours (non-negative), or null.</param>
/// <param name="RefundPercentOnCancellation">Refund percent 0..100 within notice, or null.</param>
public sealed record SetBookingRuleRequest(
    Guid AmenityId,
    string? AllowedResidentTypes,
    int? AdvanceBookingWindowDays,
    int? MinDurationMinutes,
    int? MaxDurationMinutes,
    int? MaxBookingsPerResident,
    int? MaxBookingsPeriodDays,
    bool RequiresApproval,
    decimal BookingCharge,
    decimal SecurityDeposit,
    int? CancellationNoticeHours,
    decimal? RefundPercentOnCancellation);

// --- BlackoutDate (Req 38.4) --------------------------------------------------------------------

/// <summary>Read shape for an amenity <c>BlackoutDate</c> range (Req 38.4).</summary>
/// <param name="Id">The blackout id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="AmenityId">The amenity the blackout applies to.</param>
/// <param name="StartUtc">Start of the unavailable period, inclusive (UTC).</param>
/// <param name="EndUtc">End of the unavailable period, exclusive (UTC).</param>
/// <param name="Reason">Human-readable reason for the blackout.</param>
/// <param name="CreatedAtUtc">When the blackout was created.</param>
public sealed record BlackoutDateDto(
    Guid Id,
    Guid CommunityId,
    Guid AmenityId,
    DateTime StartUtc,
    DateTime EndUtc,
    string Reason,
    DateTime CreatedAtUtc);

/// <summary>
/// Define a <c>BlackoutDate</c> range during which an amenity is unavailable (Req 38.4). The caller
/// supplies the amenity, the half-open UTC range and a reason. The service validates
/// <c>EndUtc &gt; StartUtc</c>. Overlapping ranges are allowed (maintenance windows can stack).
/// </summary>
/// <param name="AmenityId">The amenity to black out (must be in an authorized community). Required.</param>
/// <param name="StartUtc">Start of the unavailable period, inclusive (UTC). Required.</param>
/// <param name="EndUtc">End of the unavailable period, exclusive (UTC, &gt; start). Required.</param>
/// <param name="Reason">Human-readable reason. Required.</param>
public sealed record CreateBlackoutDateRequest(
    Guid AmenityId,
    DateTime StartUtc,
    DateTime EndUtc,
    string Reason);

// =================================================================================================
// Task 23.2 — booking lifecycle + capacity concurrency (Req 39.1-39.5). A resident books a
// FacilitySlot for a specific date; the booking honors the amenity's BookingRule (eligibility /
// advance window / per-resident cap / blackout exclusion / approval flag) and the per-(slot, date)
// capacity is enforced with a DB-level guarded counter so concurrent last-seat requests never
// oversell. Shapes follow the Ticket/Parcel resident-owned record style. Controllers (24.3) wire
// the acting ResidentId / UnitId from the caller's principal.
// =================================================================================================

/// <summary>Read shape for a <c>Booking</c> (Req 39.1).</summary>
/// <param name="Id">The booking id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="UnitId">The booking resident's host unit (the resident self-scope key), or null.</param>
/// <param name="AmenityId">The booked amenity.</param>
/// <param name="FacilitySlotId">The booked facility slot.</param>
/// <param name="ResidentId">The resident who made the booking.</param>
/// <param name="BookingDate">The calendar date (UTC, date-only) the slot is booked for.</param>
/// <param name="Status">Configurable lifecycle-status code (requested / confirmed / cancelled / completed).</param>
/// <param name="RequiresApproval">Whether the booking required approval at create time (snapshotted).</param>
/// <param name="BookingCharge">The booking charge snapshotted from the rule at create time.</param>
/// <param name="SecurityDeposit">The security deposit snapshotted from the rule at create time.</param>
/// <param name="ApprovedByUserId">The user who approved the booking, or null.</param>
/// <param name="ConfirmedAtUtc">When the booking was confirmed, or null.</param>
/// <param name="CancelledAtUtc">When the booking was cancelled, or null.</param>
/// <param name="CreatedAtUtc">When the booking was created.</param>
public sealed record BookingDto(
    Guid Id,
    Guid CommunityId,
    Guid? UnitId,
    Guid AmenityId,
    Guid FacilitySlotId,
    Guid ResidentId,
    DateTime BookingDate,
    string Status,
    bool RequiresApproval,
    decimal BookingCharge,
    decimal SecurityDeposit,
    Guid? ApprovedByUserId,
    DateTime? ConfirmedAtUtc,
    DateTime? CancelledAtUtc,
    DateTime CreatedAtUtc);

/// <summary>
/// Request a <c>Booking</c> of a facility slot for a specific date (Req 39.1). The caller supplies
/// the slot, the booking date, and the acting resident + their host unit (wired by the 24.3 endpoint
/// from the caller's principal). The service validates the amenity is bookable, the slot recurs on
/// the date's weekday, the date is within the advance-booking window, the resident's type is
/// eligible, the date is not blacked out, and the resident is under the per-resident cap; then it
/// reserves a per-(slot, date) capacity seat with the DB-level guarded counter (a full slot →
/// VALIDATION_ERROR, Req 39.2) and creates the booking as <c>requested</c> (approval-required
/// amenity, Req 39.3) or <c>confirmed</c> (auto-confirm).
/// </summary>
/// <param name="FacilitySlotId">The facility slot to book (must be an active slot of a bookable amenity in an authorized community). Required.</param>
/// <param name="BookingDate">The calendar date to book the slot for (UTC; the date component is used). Required.</param>
/// <param name="ResidentId">The acting resident (the eligibility + per-resident-cap subject). Required.</param>
/// <param name="UnitId">The acting resident's host unit (the self-scope key). Required.</param>
public sealed record CreateBookingRequest(
    Guid FacilitySlotId,
    DateTime BookingDate,
    Guid ResidentId,
    Guid UnitId);

// =================================================================================================
// Task 24.2 — resident events + capacity-bounded registration (Req 41.4, 41.5). A resident publishes
// an event with a schedule + capacity, and residents register for it up to that capacity. Capacity
// is enforced with the SAME atomic DB-level guarded counter as the booking lifecycle's
// SlotBookingCount — reject-at-capacity is a DB invariant, never a read-then-write check. The HTTP
// controllers (24.3) wire the acting resident/unit from the caller's self-scoped principal.
// =================================================================================================

/// <summary>Read shape for a <c>ResidentEvent</c> (Req 41.4).</summary>
/// <param name="Id">The event id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="UnitId">The organiser's host unit.</param>
/// <param name="OrganizerResidentId">The resident who published the event.</param>
/// <param name="Title">The event title.</param>
/// <param name="Description">Optional description, or null.</param>
/// <param name="Location">Optional venue/location, or null.</param>
/// <param name="StartsAtUtc">When the event starts.</param>
/// <param name="EndsAtUtc">When the event ends.</param>
/// <param name="Capacity">The maximum number of registrations.</param>
/// <param name="RegisteredCount">The current number of confirmed registrations.</param>
/// <param name="Status">Configurable status code (published / cancelled / completed).</param>
/// <param name="CreatedAtUtc">When the event was published.</param>
public sealed record ResidentEventDto(
    Guid Id,
    Guid CommunityId,
    Guid? UnitId,
    Guid OrganizerResidentId,
    string Title,
    string? Description,
    string? Location,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int Capacity,
    int RegisteredCount,
    string Status,
    DateTime CreatedAtUtc);

/// <summary>
/// Publish a resident event (Req 41.4). The service validates a non-empty title, <c>EndsAtUtc &gt;
/// StartsAtUtc</c> and a positive capacity, and stores it <c>published</c>.
/// </summary>
/// <param name="CommunityId">The owning community (must be authorized for the caller). Required.</param>
/// <param name="OrganizerResidentId">The resident publishing the event. Required.</param>
/// <param name="UnitId">The organiser's host unit (the self-scope key). Required.</param>
/// <param name="Title">The event title. Required.</param>
/// <param name="Description">Optional description.</param>
/// <param name="Location">Optional venue/location.</param>
/// <param name="StartsAtUtc">When the event starts. Required.</param>
/// <param name="EndsAtUtc">When the event ends (after Starts). Required.</param>
/// <param name="Capacity">The maximum number of registrations (positive). Required.</param>
public sealed record PublishResidentEventRequest(
    Guid CommunityId,
    Guid OrganizerResidentId,
    Guid UnitId,
    string Title,
    string? Description,
    string? Location,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int Capacity);

/// <summary>Read shape for a <c>ResidentEventRegistration</c> (Req 41.5).</summary>
/// <param name="Id">The registration id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="ResidentEventId">The event registered for.</param>
/// <param name="ResidentId">The registering resident.</param>
/// <param name="UnitId">The registering resident's unit.</param>
/// <param name="RegisteredAtUtc">When the registration was recorded.</param>
/// <param name="IsCancelled">Whether the registration was cancelled (its seat released).</param>
public sealed record ResidentEventRegistrationDto(
    Guid Id,
    Guid CommunityId,
    Guid ResidentEventId,
    Guid ResidentId,
    Guid? UnitId,
    DateTime RegisteredAtUtc,
    bool IsCancelled);

/// <summary>
/// Register a resident for a published event (Req 41.5). The service claims a seat with the atomic
/// guarded counter (reject-at-capacity) and records one registration per resident per event.
/// </summary>
/// <param name="ResidentEventId">The event to register for (must be published, in an authorized community). Required.</param>
/// <param name="ResidentId">The registering resident (the notification recipient). Required.</param>
/// <param name="UnitId">The registering resident's unit (the self-scope key). Required.</param>
public sealed record RegisterForResidentEventRequest(
    Guid ResidentEventId,
    Guid ResidentId,
    Guid UnitId);
