using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Amenities;

/// <summary>
/// The per-(<see cref="FacilitySlotId"/>, <see cref="BookingDate"/>) seat ledger (Req 39.2, 39.5):
/// the SINGLE authoritative count of confirmed/held seats for one slot on one calendar date, and the
/// row the booking lifecycle reserves a seat against. A tenant-owned child of a slot carrying the
/// SAME <see cref="TenantOwnedEntity.CommunityId"/> as its amenity/slot, NOT <see cref="IUnitScoped"/>
/// — it is capacity infrastructure shared across all residents booking the slot, not resident
/// self-view data.
///
/// <para><b>Capacity concurrency guarantee (the headline, Req 39.5).</b> A seat is claimed with an
/// atomic, DB-level guarded conditional UPDATE —
/// <c>UPDATE … SET BookedCount = BookedCount + 1 WHERE Id = @id AND BookedCount &lt; Capacity</c>
/// (EF Core <c>ExecuteUpdateAsync</c> with the <c>BookedCount &lt; Capacity</c> guard in the WHERE).
/// The database evaluates the guard and applies the increment as one statement, so two requests
/// racing the last remaining seat each hit the row serialized by the database: exactly one sees
/// <c>BookedCount &lt; Capacity</c> and increments (affected rows = 1 → seat granted), the other
/// finds the row already full (affected rows = 0 → "slot full", Req 39.2). There is NO read-then-write
/// gap in application code — the count is never read into memory, compared, then written back
/// (steering: concurrency safety via DB-level guarantees). A cancellation releases the seat with the
/// symmetric guarded decrement (<c>BookedCount &gt; 0</c>).</para>
///
/// <para><b>One counter row per (slot, date).</b> The <c>Unique(FacilitySlotId, BookingDate)</c>
/// index (in the entity configuration) makes "one seat ledger per slot per date" a DB guarantee, so
/// the lifecycle's lazy create-on-first-booking is race-safe: two first bookings both try to insert
/// the counter, the database lets exactly one succeed and the loser re-reads the winner's row — then
/// both contend on the guarded increment above. No read-then-write over the existence check.</para>
///
/// <para><b>Capacity snapshot.</b> <see cref="Capacity"/> is copied from the <see cref="FacilitySlot"/>
/// when the counter row is created so the guarded UPDATE compares against a stable per-date ceiling
/// (a later slot-capacity edit does not silently change a date already being booked). ponytail: an
/// admin lowering a slot's capacity does not retro-shrink an already-created date counter — the new
/// ceiling applies to dates whose counter is first created after the edit; a bulk re-cap of existing
/// future dates is a separate admin action, not part of 39.x.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token, audit columns. The guarded increment is the capacity gate; the RowVersion
/// still guards any tracked edit of the row.</para>
/// </summary>
public class SlotBookingCount : TenantOwnedEntity
{
    /// <summary>The amenity the counted slot belongs to (Req 39.2). Denormalized for tenant-scoped lookups.</summary>
    public Guid AmenityId { get; set; }

    /// <summary>The facility slot this counter counts seats for (Req 39.2). Required; part of <c>Unique(FacilitySlotId, BookingDate)</c>.</summary>
    public Guid FacilitySlotId { get; set; }

    /// <summary>
    /// The calendar date (UTC date, time component zero) this counter counts seats for (Req 39.2).
    /// Part of <c>Unique(FacilitySlotId, BookingDate)</c> — one ledger row per slot per date.
    /// </summary>
    public DateTime BookingDate { get; set; }

    /// <summary>
    /// The number of seats currently held for the slot on the date (Req 39.2, 39.5). Mutated ONLY via
    /// the atomic guarded UPDATE/decrement described on the type; never read-modified-written in
    /// application memory. Starts 0.
    /// </summary>
    public int BookedCount { get; set; }

    /// <summary>
    /// The capacity ceiling for the slot on the date (Req 39.2), snapshotted from the
    /// <see cref="FacilitySlot.Capacity"/> when the row is created. The guarded increment permits a
    /// seat only while <c>BookedCount &lt; Capacity</c>. Always positive.
    /// </summary>
    public int Capacity { get; set; }
}
