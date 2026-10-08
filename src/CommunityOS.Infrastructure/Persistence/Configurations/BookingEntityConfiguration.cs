using CommunityOS.Domain.Amenities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunityOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="Booking"/> table (Task 23.2, Req 39.1-39.5) — a resident's reservation of a
/// <see cref="FacilitySlot"/> for a specific date. As a <c>TenantOwnedEntity</c> it inherits the
/// tenant + soft-delete global filter and the rowversion mapping from
/// <c>AppDbContext.OnModelCreating</c>; because it implements <c>IUnitScoped</c> the DbContext also
/// applies the resident self-scope filter (keyed on <see cref="Booking.UnitId"/>), so a resident
/// principal reaches only their OWN unit's bookings — a resident books/cancels only their own
/// bookings with no bespoke predicate (same stance as <c>Ticket</c> / <c>ParcelAuthorization</c>).
///
/// <para><b>Indexes (steering: no N+1, scalable reads).</b>
/// <c>(CommunityId, FacilitySlotId, BookingDate)</c> backs the "a slot's bookings for a date" read
/// (and keeps a staff booking list ordered/filterable without a scan).
/// <c>(CommunityId, ResidentId, BookingDate)</c> backs the per-resident cap count (how many bookings
/// a resident holds in the rolling window, Req 38.3) and the resident's own booking history as a
/// single indexed read. Money fields are decimals with explicit precision, never floats. The amenity
/// / slot FKs are <c>Restrict</c> on delete (deletes are soft deletes anyway).</para>
/// </summary>
public sealed class BookingEntityConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("Bookings");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Status).IsRequired().HasMaxLength(50);
        builder.Property(b => b.BookingCharge).HasPrecision(18, 2);
        builder.Property(b => b.SecurityDeposit).HasPrecision(18, 2);

        // Link to the owning amenity + slot (Req 39.1). Restrict on delete; deletes are soft deletes.
        builder.HasOne<Amenity>()
            .WithMany()
            .HasForeignKey(b => b.AmenityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FacilitySlot>()
            .WithMany()
            .HasForeignKey(b => b.FacilitySlotId)
            .OnDelete(DeleteBehavior.Restrict);

        // A slot's bookings for a date (Req 39.1) as a single ordered indexed read (no N+1/scan).
        builder.HasIndex(b => new { b.CommunityId, b.FacilitySlotId, b.BookingDate })
            .HasDatabaseName("IX_Booking_CommunityId_FacilitySlotId_BookingDate");

        // The per-resident cap count + the resident's own booking history (Req 38.3) as a single
        // indexed read — so the cap check never scans the table.
        builder.HasIndex(b => new { b.CommunityId, b.ResidentId, b.BookingDate })
            .HasDatabaseName("IX_Booking_CommunityId_ResidentId_BookingDate");
    }
}
