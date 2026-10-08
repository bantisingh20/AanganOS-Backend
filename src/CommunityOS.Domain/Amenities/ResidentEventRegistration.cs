using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Amenities;

/// <summary>
/// One resident's registration for a <see cref="ResidentEvent"/> (Req 41.5, Task 24.2): the record
/// that <see cref="ResidentId"/> (for their <see cref="UnitId"/>) claimed a seat on
/// <see cref="ResidentEventId"/>, and whether that seat has been released again
/// (<see cref="IsCancelled"/>).
///
/// <para><b>One registration per resident per event.</b> The <c>Unique(ResidentEventId, ResidentId)</c>
/// index (in the entity configuration) makes "a resident registers for an event at most once" a DB
/// guarantee — a concurrent duplicate registration maps to CONFLICT via <c>UniqueConstraintGuard</c>,
/// never a read-then-write existence check. The actual at-capacity gate lives on the event's
/// <see cref="ResidentEvent.RegisteredCount"/> guarded counter (Req 41.5); this row records WHO holds
/// a seat so a cancellation can release exactly one.</para>
///
/// <para><b>Seat release exactly once.</b> <see cref="IsCancelled"/> + the inherited RowVersion make a
/// cancellation release the event seat exactly once: the guarded decrement on the event counter runs
/// only when the flag flips, and the RowVersion makes the cancel record once under concurrency (a
/// double-cancel loser → CONFLICT), so the counter can never be decremented twice for one
/// registration (mirrors <c>Booking.SeatReleased</c>).</para>
///
/// <para><b>Resident self-scope (Req 5.4).</b> Implements <see cref="IUnitScoped"/> via its non-null
/// <see cref="UnitId"/> (the registering resident's unit), so the resident self-scope filter restricts
/// a resident principal to their OWN registrations (same stance as <c>Booking</c>). Derives from
/// <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion concurrency token,
/// audit columns.</para>
/// </summary>
public class ResidentEventRegistration : TenantOwnedEntity, IUnitScoped
{
    /// <summary>
    /// The registering resident's unit (Req 41.5). Non-null in practice; typed nullable to satisfy
    /// <see cref="IUnitScoped"/>, which the resident self-scope filter keys on.
    /// </summary>
    public Guid? UnitId { get; set; }

    /// <summary>The event registered for (Req 41.5). Required; part of <c>Unique(ResidentEventId, ResidentId)</c>.</summary>
    public Guid ResidentEventId { get; set; }

    /// <summary>The resident who registered (Req 41.5). Required; part of <c>Unique(ResidentEventId, ResidentId)</c>.</summary>
    public Guid ResidentId { get; set; }

    /// <summary>When the registration was recorded (Req 41.5).</summary>
    public DateTime RegisteredAtUtc { get; set; }

    /// <summary>
    /// Whether this registration has been cancelled and its event seat released back to the counter
    /// (Req 41.5). Set true on cancel so the freed seat is returned to the
    /// <see cref="ResidentEvent.RegisteredCount"/> exactly once (the inherited RowVersion makes the
    /// cancel record once, so the decrement cannot run twice for one registration).
    /// </summary>
    public bool IsCancelled { get; set; }
}
