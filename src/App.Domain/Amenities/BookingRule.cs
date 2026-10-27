using App.Domain.Common;

namespace App.Domain.Amenities;

/// <summary>
/// The configurable booking-rule set for an <see cref="Amenity"/> (Req 38.3): the eligibility,
/// charge, approval, cancellation, refund and deposit rules a Facility Manager configures so the
/// booking lifecycle (23.2) can decide who may book, at what cost and under what conditions. A
/// tenant-owned child of an amenity carrying the SAME <see cref="TenantOwnedEntity.CommunityId"/> as
/// its parent, NOT <see cref="IUnitScoped"/> — rule configuration is community infrastructure.
///
/// <para><b>One rule-set per amenity (singleton child).</b> The requirement is ONE set of
/// configurable rule values per amenity, so this is modeled as a per-amenity singleton via
/// <c>Unique(AmenityId)</c> rather than a list of rules — the amenity's booking policy. The service
/// upserts it (create-or-update), so configuring an amenity's rules is idempotent.</para>
///
/// <para><b>Nothing hardcoded (steering, Req 38.3).</b> Every value here is stored configuration a
/// Facility Manager sets per amenity. <see cref="AllowedResidentTypes"/> is a comma-separated list
/// of <c>Resident_Type</c> codes (eligibility) — null/empty means "all resident types eligible", a
/// deliberate open default. The numeric windows/limits are the booking policy the 23.2 lifecycle
/// enforces; the money fields are decimals (never floats). None of these are compiled constants —
/// the 23.2 lifecycle reads them off this row at decision time.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token (so a rule edit records exactly once, Req 22.4), audit columns.</para>
/// </summary>
public class BookingRule : TenantOwnedEntity
{
    /// <summary>The amenity these rules apply to (Req 38.3). Required and unique (<c>Unique(AmenityId)</c>) — one rule-set per amenity.</summary>
    public Guid AmenityId { get; set; }

    // --- Eligibility (Req 38.3) ------------------------------------------------------------------

    /// <summary>
    /// Comma-separated list of <c>Resident_Type</c> codes eligible to book the amenity (Req 38.3), or
    /// null/empty for "all resident types eligible". Each code is validated against the editable
    /// <c>Resident_Type</c> master-data list by the service — never a hardcoded eligibility rule.
    /// </summary>
    public string? AllowedResidentTypes { get; set; }

    /// <summary>
    /// Maximum number of advance days a resident may book ahead (Req 38.3): the advance-booking
    /// window. Null means "no advance-window limit". Non-negative when set.
    /// </summary>
    public int? AdvanceBookingWindowDays { get; set; }

    /// <summary>
    /// Minimum booking duration in minutes (Req 38.3), or null for "no minimum". Positive when set.
    /// The 23.2 lifecycle rejects a booking shorter than this.
    /// </summary>
    public int? MinDurationMinutes { get; set; }

    /// <summary>
    /// Maximum booking duration in minutes (Req 38.3), or null for "no maximum". Positive and, when
    /// both are set, not less than <see cref="MinDurationMinutes"/> (the service validates this).
    /// </summary>
    public int? MaxDurationMinutes { get; set; }

    /// <summary>
    /// Maximum number of bookings a single resident may hold within the configured
    /// <see cref="MaxBookingsPeriodDays"/> window (Req 38.3), or null for "no per-resident cap".
    /// Positive when set. The 23.2 lifecycle enforces this against the resident's existing bookings.
    /// </summary>
    public int? MaxBookingsPerResident { get; set; }

    /// <summary>
    /// The rolling window in days over which <see cref="MaxBookingsPerResident"/> is counted
    /// (Req 38.3), or null to count over all time. Positive when set.
    /// </summary>
    public int? MaxBookingsPeriodDays { get; set; }

    // --- Approval (Req 38.3) ---------------------------------------------------------------------

    /// <summary>
    /// Whether a booking for this amenity requires approval before it is confirmed (Req 38.3). When
    /// true the 23.2 lifecycle sets a new booking to pending until a user with the Approve permission
    /// approves it; when false bookings are auto-confirmed. Defaults to false.
    /// </summary>
    public bool RequiresApproval { get; set; }

    // --- Charges / deposit / refund (Req 38.3) ---------------------------------------------------

    /// <summary>The booking charge for the amenity (Req 38.3), a non-negative decimal (money precision). Zero means free.</summary>
    public decimal BookingCharge { get; set; }

    /// <summary>The refundable security deposit required to book (Req 38.3), a non-negative decimal. Zero means none.</summary>
    public decimal SecurityDeposit { get; set; }

    /// <summary>
    /// Minimum notice in hours before the slot start a resident must cancel by to be eligible for a
    /// refund (Req 38.3), or null for "no cancellation-notice rule". Non-negative when set. The 23.2
    /// lifecycle uses this with <see cref="RefundPercentOnCancellation"/> to decide the refund.
    /// </summary>
    public int? CancellationNoticeHours { get; set; }

    /// <summary>
    /// The percentage of the booking charge refunded when a resident cancels within the allowed
    /// notice (Req 38.3), 0..100. Null means "no refund rule configured" (the lifecycle treats it as
    /// 0). The service validates the 0..100 range when set.
    /// </summary>
    public decimal? RefundPercentOnCancellation { get; set; }
}
