using App.Domain.Common;

namespace App.Domain.Amenities;

/// <summary>
/// A bookable time slot for an <see cref="Amenity"/> (Req 38.2): a recurring daily window (e.g.
/// "Pool 06:00–08:00") with a capacity that residents book against. A tenant-owned child of an
/// amenity carrying the SAME <see cref="TenantOwnedEntity.CommunityId"/> as its parent (so the
/// tenant global filter bounds it to the caller's communities with no bespoke predicate), NOT
/// <see cref="IUnitScoped"/> — slot configuration is community infrastructure, not resident
/// self-view (same stance as <c>WorkOrderPart</c> under its work order).
///
/// <para><b>Recurring daily window as minutes-from-midnight (Req 38.2).</b> A slot is a recurring
/// window keyed by its day-of-week set and a start/end time, stored as
/// <see cref="StartMinutesOfDay"/> / <see cref="EndMinutesOfDay"/> (minutes since 00:00, so a
/// provider-agnostic integer rather than a DB <c>time</c> type) plus a <see cref="DaysOfWeekMask"/>
/// bitmask of the weekdays it applies to. The booking lifecycle (23.2) materializes a concrete
/// bookable instance for a given calendar date from (date matches the mask) + (window) and enforces
/// <see cref="Capacity"/>. 23.1 only STORES the window + capacity; it does not create bookings.</para>
///
/// <para><b>Capacity + concurrency token (Req 38.2, design "RowVersion on FacilitySlot").</b>
/// <see cref="Capacity"/> is the number of concurrent bookings the slot permits. The inherited
/// <c>RowVersion</c> is the optimistic-concurrency token the 23.2 booking lifecycle increments a
/// per-date BookedCount under so two requests racing the last remaining capacity can't both succeed
/// — 23.1 provisions the capacity + the token; the counter itself lives on the 23.2 booking
/// aggregate. 23.1 keeps the slot a pure configuration row.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter (a retired
/// slot stays traceable but is hidden from ordinary reads), rowversion concurrency token (Req 22.4),
/// audit columns.</para>
/// </summary>
public class FacilitySlot : TenantOwnedEntity
{
    /// <summary>The amenity this slot belongs to (Req 38.2). Required.</summary>
    public Guid AmenityId { get; set; }

    /// <summary>Optional human-readable slot label (e.g. "Morning", "Evening Peak"). Null falls back to the window text in the UI.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Start of the recurring daily window in minutes since midnight (Req 38.2), 0..1439. Validated
    /// strictly less than <see cref="EndMinutesOfDay"/> by the service.
    /// </summary>
    public int StartMinutesOfDay { get; set; }

    /// <summary>
    /// End of the recurring daily window in minutes since midnight (Req 38.2), 1..1440 (1440 == end
    /// of day). Validated strictly greater than <see cref="StartMinutesOfDay"/> by the service.
    /// </summary>
    public int EndMinutesOfDay { get; set; }

    /// <summary>
    /// Bitmask of the weekdays this slot recurs on (Req 38.2): bit 0 = Sunday … bit 6 = Saturday
    /// (matching <see cref="System.DayOfWeek"/>). A value with no bits set is rejected by the service
    /// (a slot must apply to at least one day). The 23.2 lifecycle only offers the slot on a date
    /// whose weekday bit is set.
    /// </summary>
    public int DaysOfWeekMask { get; set; }

    /// <summary>
    /// The number of concurrent bookings this slot permits (Req 38.2). Always positive (the service
    /// rejects a non-positive capacity). The 23.2 booking lifecycle enforces at-capacity against this.
    /// </summary>
    public int Capacity { get; set; } = 1;

    /// <summary>
    /// Whether the slot is active and offered for booking (Req 38.2). A paused slot (<c>false</c>) is
    /// left in the table (traceable) but not bookable. Defaults to active on creation.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether <paramref name="dayOfWeek"/> is one of the weekdays this slot recurs on (Req 38.2):
    /// the bit for the day is set in <see cref="DaysOfWeekMask"/>. Pure helper the 23.2 lifecycle
    /// uses to decide whether a calendar date offers this slot; no I/O.
    /// </summary>
    public bool RecursOn(DayOfWeek dayOfWeek) => (DaysOfWeekMask & (1 << (int)dayOfWeek)) != 0;
}
