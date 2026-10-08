using CommunityOS.Domain.Amenities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunityOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="FacilitySlot"/> table (Req 38.2) — a bookable recurring time slot for an
/// amenity. As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the
/// rowversion mapping from <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped and a
/// retired slot stays traceable but is hidden from ordinary reads. It is deliberately NOT
/// <c>IUnitScoped</c> — slot configuration is community infrastructure (same stance as
/// <see cref="AmenityEntityConfiguration"/>). The inherited <c>RowVersion</c> is the concurrency
/// token the 23.2 booking lifecycle rides when it books against the slot's capacity.
///
/// <para><b>Amenity slots index (Req 38.2).</b> <c>(CommunityId, AmenityId, StartMinutesOfDay)</c>
/// backs the "an amenity's slots, ordered by window start" read as a single ordered indexed read
/// (no N+1). The amenity FK is <c>Restrict</c> on delete so an amenity can't be hard-deleted out
/// from under its slots; deletes are soft deletes anyway.</para>
/// </summary>
public sealed class FacilitySlotEntityConfiguration : IEntityTypeConfiguration<FacilitySlot>
{
    public void Configure(EntityTypeBuilder<FacilitySlot> builder)
    {
        builder.ToTable("FacilitySlots");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).HasMaxLength(200);

        // Link to the owning amenity (Req 38.2). Restrict on delete; deletes are soft deletes anyway.
        builder.HasOne<Amenity>()
            .WithMany()
            .HasForeignKey(s => s.AmenityId)
            .OnDelete(DeleteBehavior.Restrict);

        // The amenity's slots list (Req 38.2) ordered by window start as a single indexed read.
        builder.HasIndex(s => new { s.CommunityId, s.AmenityId, s.StartMinutesOfDay })
            .HasDatabaseName("IX_FacilitySlot_CommunityId_AmenityId_StartMinutesOfDay");
    }
}
