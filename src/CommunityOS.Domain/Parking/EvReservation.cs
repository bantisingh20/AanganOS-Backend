using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Parking;

/// <summary>
/// A resident's reservation of an EV-charging <c>ParkingSlot</c> (<c>SlotType == "ev"</c>) for a
/// charging session (Req 41.2, 41.3, Task 24.2). Each row records that an EV slot
/// (<see cref="ParkingSlotId"/>) was reserved by a resident (<see cref="ResidentId"/> for their
/// <see cref="UnitId"/>) from <see cref="ReservedFromUtc"/> until <see cref="ReservedUntilUtc"/>,
/// optionally capturing the actual charging session (<see cref="SessionStartedAtUtc"/> /
/// <see cref="SessionEndedAtUtc"/> / <see cref="EnergyKwh"/>).
///
/// <para><b>One active reservation per EV slot (Req 41.3 — the headline invariant).</b> A reservation
/// is append-only and carries an <see cref="EndedAtUtc"/> that is null <i>while this is the current/
/// active</i> reservation and is stamped (never cleared) when the reservation is released or
/// completed. This is the SAME one-active-row model as <see cref="ParkingAssignment"/>: on SQL Server
/// a filtered unique index on <c>(ParkingSlotId) WHERE EndedAtUtc IS NULL</c> (added in
/// <c>AppDbContext.OnModelCreating</c>, where the provider is known) makes "a given EV slot has at
/// most one active reservation at a time" a hard DB guarantee — two concurrent reservations of the
/// SAME EV slot race to that index and exactly ONE wins; the loser's unique violation maps to CONFLICT
/// via <c>UniqueConstraintGuard</c>. The database decides the winner, never a read-then-write check.
/// ponytail: the SQLite test provider has no filtered-index equivalent, so under that provider the
/// invariant rests on the single-transaction insert (the service inserts the active row inside one
/// SaveChanges) rather than a DB constraint — acceptable for the test path; production (SQL Server)
/// gets the index. Same approach as the one-open-owner <c>OwnershipHistory</c> / one-active-custody-
/// location <c>Parcel</c> / one-active-assignment <see cref="ParkingAssignment"/> indexes.</para>
///
/// <para><b>Configurable status (steering: no hardcoding, Req 41.2).</b> <see cref="Status"/> resolves
/// against the editable <c>Ev_Reservation_Status</c> master-data list (reserved / charging / completed
/// / cancelled) and starts <see cref="EvReservationStatuses.Reserved"/> — a stable code, never a
/// frozen enum.</para>
///
/// <para>Not <see cref="IUnitScoped"/>: the EV reservation register is an operations record of a
/// shared community charger's bookings (an admin/operator reads them across the community), not
/// resident self-view data pinned behind the self-scope filter — same stance as
/// <see cref="ParkingAssignment"/>. The <see cref="ResidentId"/>/<see cref="UnitId"/> record WHO
/// reserved so the resident can be notified and per-unit guest/EV limits can be counted. Derives from
/// <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion concurrency token,
/// audit columns.</para>
/// </summary>
public class EvReservation : TenantOwnedEntity
{
    /// <summary>The EV-charging parking slot this reservation is for (Req 41.2). Required; must be a <c>SlotType == "ev"</c> active slot.</summary>
    public Guid ParkingSlotId { get; set; }

    /// <summary>The resident who reserved the charger (Req 41.2): the reservation subject + notification recipient. Required.</summary>
    public Guid ResidentId { get; set; }

    /// <summary>The resident's unit at reservation time (Req 41.2): the per-unit concurrent-reservation-limit scope. Required.</summary>
    public Guid UnitId { get; set; }

    /// <summary>When the reserved charging window begins (Req 41.2). Set on reserve.</summary>
    public DateTime ReservedFromUtc { get; set; }

    /// <summary>When the reserved charging window ends (Req 41.2). Bounded by the configurable max session duration.</summary>
    public DateTime ReservedUntilUtc { get; set; }

    /// <summary>
    /// When this reservation ended (released/completed), or null while it is the current/ACTIVE
    /// reservation (Req 41.3). Set (never cleared) when the reservation is released or completed. The
    /// null value is what the filtered unique index keys the one-active-per-EV-slot invariant on.
    /// </summary>
    public DateTime? EndedAtUtc { get; set; }

    /// <summary>
    /// Configurable status code resolved against the <c>Ev_Reservation_Status</c> master-data list
    /// (Req 41.2). Starts <see cref="EvReservationStatuses.Reserved"/>. Stored as the stable code,
    /// never a frozen enum.
    /// </summary>
    public string Status { get; set; } = EvReservationStatuses.Reserved;

    /// <summary>When the actual charging session started (Req 41.2, optional consumption record), or null when not yet started/captured.</summary>
    public DateTime? SessionStartedAtUtc { get; set; }

    /// <summary>When the actual charging session ended (Req 41.2, optional consumption record), or null when not yet ended/captured.</summary>
    public DateTime? SessionEndedAtUtc { get; set; }

    /// <summary>
    /// The energy drawn by the charging session in kWh (Req 41.2, optional consumption record), or
    /// null when not captured. Non-negative decimal (metered value, never a float).
    /// </summary>
    public decimal? EnergyKwh { get; set; }
}

/// <summary>
/// Well-known default <c>Ev_Reservation_Status</c> codes (Req 41.2). Seeded master-data codes kept as
/// constants so the service sets/compares status without a magic string. <b>Data</b>, not a
/// behavioural enum — the list is editable per community; these constants only name the seeded
/// defaults.
/// </summary>
public static class EvReservationStatuses
{
    /// <summary>Initial status on reserve (Req 41.2): the EV slot is held for the reserved window.</summary>
    public const string Reserved = "reserved";

    /// <summary>The charging session is in progress (Req 41.2).</summary>
    public const string Charging = "charging";

    /// <summary>The reservation/session completed (Req 41.2): the active row is closed (EndedAtUtc stamped).</summary>
    public const string Completed = "completed";

    /// <summary>The reservation was cancelled before/without charging (Req 41.2): the active row is closed.</summary>
    public const string Cancelled = "cancelled";
}
