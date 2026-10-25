using UrbanNest.Domain.Amenities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="SlotBookingCount"/> table (Task 23.2, Req 39.2, 39.5) — the per-(slot, date)
/// seat ledger the booking lifecycle reserves capacity against. As a <c>TenantOwnedEntity</c> it
/// inherits the tenant + soft-delete global filter and the rowversion mapping from
/// <c>AppDbContext.OnModelCreating</c>; it is deliberately NOT <c>IUnitScoped</c> — the counter is
/// capacity infrastructure shared across all residents booking the slot, not resident self-view data.
///
/// <para><b>One ledger row per (slot, date) — the capacity concurrency anchor (Req 39.5).</b> The
/// <c>Unique(FacilitySlotId, BookingDate)</c> index makes "one seat counter per slot per date" a DB
/// guarantee. The lifecycle creates the row lazily on the first booking for a (slot, date); two
/// concurrent first bookings both attempt the insert and the database lets exactly ONE succeed (the
/// loser's duplicate insert → CONFLICT via <c>UniqueConstraintGuard</c>, re-read + retry). All
/// subsequent seat claims run the atomic guarded UPDATE
/// (<c>SET BookedCount = BookedCount + 1 WHERE BookedCount &lt; Capacity</c>) so two requests racing
/// the last seat serialize at the row — exactly one increments, no oversell, no read-then-write gap
/// (steering: concurrency safety via DB-level guarantees). The unique index is also the single
/// indexed read the lifecycle uses to resolve a (slot, date)'s counter.</para>
/// </summary>
public sealed class SlotBookingCountEntityConfiguration : IEntityTypeConfiguration<SlotBookingCount>
{
    public void Configure(EntityTypeBuilder<SlotBookingCount> builder)
    {
        builder.ToTable("SlotBookingCounts");
        builder.HasKey(c => c.Id);

        // Link to the owning amenity + slot (Req 39.2). Restrict on delete; deletes are soft deletes.
        builder.HasOne<Amenity>()
            .WithMany()
            .HasForeignKey(c => c.AmenityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FacilitySlot>()
            .WithMany()
            .HasForeignKey(c => c.FacilitySlotId)
            .OnDelete(DeleteBehavior.Restrict);

        // One seat ledger per slot per date (Req 39.5): the DB-level source of truth behind the
        // lazy create-on-first-booking, so a concurrent duplicate insert maps to CONFLICT rather than
        // a read-then-write existence check — and the atomic guarded increment then enforces capacity.
        builder.HasIndex(c => new { c.FacilitySlotId, c.BookingDate })
            .IsUnique()
            .HasDatabaseName("UX_SlotBookingCount_FacilitySlotId_BookingDate");
    }
}
