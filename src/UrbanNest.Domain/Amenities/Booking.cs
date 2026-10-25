using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Amenities;

/// <summary>
/// A resident's booking of a <see cref="FacilitySlot"/> for a specific calendar date (Req 39.1): the
/// RESERVATION the 23.2 lifecycle creates, approves, confirms, cancels and completes. A tenant-owned
/// record carrying its amenity/slot's <see cref="TenantOwnedEntity.CommunityId"/> and the booking
/// resident's host <see cref="UnitId"/>.
///
/// <para><b>Resident self-scope (Req 5.4, 39.1).</b> Implements <see cref="IUnitScoped"/> via its
/// non-null <see cref="UnitId"/> (the resident's host unit at booking time), so the DbContext
/// resident self-scope filter restricts a resident principal to their OWN unit's bookings — a
/// resident books/cancels only their own bookings with no bespoke predicate in the service (same
/// stance as <c>Ticket</c> / <c>ParcelAuthorization</c>). A Facility-Manager with the View/Approve
/// permission sees all bookings in their authorized communities (the tenant filter). The
/// <see cref="ResidentId"/> records WHO booked (the eligibility + per-resident-cap subject); the
/// unit is the self-scope key.</para>
///
/// <para><b>Configurable status + allow-listed transitions (steering: no hardcoding, Req 39.3).</b>
/// <see cref="Status"/> resolves against the editable <c>Booking_Status</c> master-data list and
/// starts <see cref="BookingStatuses.Requested"/> (approval-required amenity) or
/// <see cref="BookingStatuses.Confirmed"/> (auto-confirm) on creation. Every later move
/// (approve → confirmed, cancel → cancelled, complete → completed) is validated against the editable
/// <c>Booking_Status_Transition</c> allow-list exactly like <c>Ticket_Status_Transition</c> /
/// <c>Parcel_Status_Transition</c> — no hardcoded transition table. The <see cref="BookingStatuses"/>
/// constants only name the seeded default codes.</para>
///
/// <para><b>Capacity is NOT counted on this row.</b> The at-capacity guarantee (Req 39.2, 39.5)
/// lives on the per-(slot, date) <see cref="SlotBookingCount"/> counter, which the lifecycle
/// increments with an atomic DB-level guarded update — never a read-then-write over these booking
/// rows. This row records the reservation; the counter is the authoritative seat ledger.</para>
///
/// <para><b>Snapshotted charge/approval (Req 38.3, 39.3).</b> <see cref="RequiresApproval"/>,
/// <see cref="BookingCharge"/> and <see cref="SecurityDeposit"/> are snapshotted from the amenity's
/// <see cref="BookingRule"/> at create time so a later rule edit does not retroactively change a
/// standing booking's money/approval terms. Money fields are decimals, never floats.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token (the approve/cancel/complete transitions record exactly once, Req 22.4), audit
/// columns.</para>
/// </summary>
public class Booking : TenantOwnedEntity, IUnitScoped
{
    /// <summary>
    /// The booking resident's host unit at booking time (Req 39.1). Non-null in practice; typed
    /// nullable to satisfy <see cref="IUnitScoped"/>, which the resident self-scope filter keys on so
    /// a resident reaches only their own unit's bookings.
    /// </summary>
    public Guid? UnitId { get; set; }

    /// <summary>The amenity being booked (Req 39.1). Required; denormalized from the slot for eligibility/notification lookups and the per-resident cap scope.</summary>
    public Guid AmenityId { get; set; }

    /// <summary>The facility slot being booked (Req 39.1). Required; the capacity owner (via its per-date <see cref="SlotBookingCount"/>).</summary>
    public Guid FacilitySlotId { get; set; }

    /// <summary>The resident who made the booking (Req 39.1): the eligibility + per-resident-cap subject. Required.</summary>
    public Guid ResidentId { get; set; }

    /// <summary>
    /// The calendar date (UTC date, time component zero) the slot is booked for (Req 39.1). Combined
    /// with <see cref="FacilitySlotId"/> it keys the per-date capacity counter and the resident's
    /// booking history for the cap/blackout checks.
    /// </summary>
    public DateTime BookingDate { get; set; }

    /// <summary>
    /// Configurable status code resolved against the <c>Booking_Status</c> master-data list (Req
    /// 39.3). Starts <see cref="BookingStatuses.Requested"/> when the amenity requires approval, else
    /// <see cref="BookingStatuses.Confirmed"/>. Stored as the stable code, never a frozen enum; later
    /// moves ride the editable <c>Booking_Status_Transition</c> allow-list.
    /// </summary>
    public string Status { get; set; } = BookingStatuses.Requested;

    /// <summary>Whether this booking required approval at create time (Req 39.3), snapshotted from the amenity's <see cref="BookingRule"/>.</summary>
    public bool RequiresApproval { get; set; }

    /// <summary>The booking charge snapshotted from the <see cref="BookingRule"/> at create time (Req 38.3). Non-negative decimal (money precision).</summary>
    public decimal BookingCharge { get; set; }

    /// <summary>The refundable security deposit snapshotted from the <see cref="BookingRule"/> at create time (Req 38.3). Non-negative decimal.</summary>
    public decimal SecurityDeposit { get; set; }

    /// <summary>
    /// The user who approved the booking (Req 39.3), or null while it has not been approved. Stamped
    /// on the transition into <see cref="BookingStatuses.Confirmed"/> via approval.
    /// </summary>
    public Guid? ApprovedByUserId { get; set; }

    /// <summary>When the booking was approved/confirmed (Req 39.3), or null while requested/not yet confirmed.</summary>
    public DateTime? ConfirmedAtUtc { get; set; }

    /// <summary>When the booking was cancelled (Req 39.4), or null while it has not been cancelled.</summary>
    public DateTime? CancelledAtUtc { get; set; }

    /// <summary>
    /// Whether the per-(slot, date) capacity seat claimed for this booking has been RELEASED back to
    /// the counter (Req 39.2, 39.5). Set true when the booking is cancelled so the freed seat is
    /// returned to the <see cref="SlotBookingCount"/> exactly once (the inherited <c>RowVersion</c>
    /// makes the cancel record once, so the decrement cannot run twice for one booking).
    /// </summary>
    public bool SeatReleased { get; set; }
}

/// <summary>
/// Well-known default <c>Booking_Status</c> codes (Req 39.3). Seeded master-data codes kept as
/// constants so the service sets/compares status without a magic string. <b>Data</b>, not a
/// behavioural enum — the status list AND the allowed transitions (<c>Booking_Status_Transition</c>)
/// are editable master data; these constants only name the seeded defaults.
/// </summary>
public static class BookingStatuses
{
    /// <summary>Initial status for an approval-required amenity (Req 39.3): pending an Approve-permission user.</summary>
    public const string Requested = "requested";

    /// <summary>The booking is confirmed/active (Req 39.1): the create-time status for an auto-confirm amenity, or set when an approver confirms a requested booking.</summary>
    public const string Confirmed = "confirmed";

    /// <summary>The booking was cancelled (Req 39.4): its capacity seat is released back to the slot/date counter.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>The booking's slot has elapsed and it was completed (Req 39.1). Terminal.</summary>
    public const string Completed = "completed";
}
