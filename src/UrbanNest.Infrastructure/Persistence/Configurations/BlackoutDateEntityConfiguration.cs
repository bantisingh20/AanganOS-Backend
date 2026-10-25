using UrbanNest.Domain.Amenities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="BlackoutDate"/> table (Req 38.4) — a date/period an amenity is unavailable
/// for booking. As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and
/// the rowversion mapping from <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped and a
/// lifted (soft-deleted) blackout stays traceable but is hidden from ordinary reads. It is
/// deliberately NOT <c>IUnitScoped</c> — blackout configuration is community infrastructure.
///
/// <para><b>Blackout window index (Req 38.4).</b> <c>(CommunityId, AmenityId, StartUtc)</c> backs
/// both the "an amenity's blackout ranges, ordered by start" read and the 23.2 lifecycle's
/// range-overlap check (which blackouts cover a candidate booking instant) as a single ordered
/// indexed read (no scan, no N+1). No uniqueness is placed on the range — overlapping maintenance
/// windows are deliberately allowed. The amenity FK is <c>Restrict</c> on delete; deletes are soft
/// deletes anyway.</para>
/// </summary>
public sealed class BlackoutDateEntityConfiguration : IEntityTypeConfiguration<BlackoutDate>
{
    public void Configure(EntityTypeBuilder<BlackoutDate> builder)
    {
        builder.ToTable("BlackoutDates");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Reason).IsRequired().HasMaxLength(500);

        // Link to the owning amenity (Req 38.4). Restrict on delete; deletes are soft deletes anyway.
        builder.HasOne<Amenity>()
            .WithMany()
            .HasForeignKey(b => b.AmenityId)
            .OnDelete(DeleteBehavior.Restrict);

        // The amenity's blackout ranges ordered by start + the lifecycle overlap check (Req 38.4) as
        // a single ordered indexed read.
        builder.HasIndex(b => new { b.CommunityId, b.AmenityId, b.StartUtc })
            .HasDatabaseName("IX_BlackoutDate_CommunityId_AmenityId_StartUtc");
    }
}
