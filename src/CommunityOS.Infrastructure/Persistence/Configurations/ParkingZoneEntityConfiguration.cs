using CommunityOS.Domain.Parking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunityOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="ParkingZone"/> table (Req 40.1) — a named grouping of parking slots in the
/// layout. As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the
/// rowversion mapping from <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped (Req 5.2)
/// and edits are concurrency-checked — the same shape as <see cref="CustodyLocationEntityConfiguration"/>.
///
/// <para>Unique <c>(CommunityId, Code)</c> keeps zone codes distinct within a community (a duplicate →
/// CONFLICT via <c>UniqueConstraintGuard</c>, no read-then-write). <c>ZoneType</c> is the configurable
/// layout classification; <c>Name</c>/<c>Level</c> are the human-facing layout labels.</para>
/// </summary>
public sealed class ParkingZoneEntityConfiguration : IEntityTypeConfiguration<ParkingZone>
{
    public void Configure(EntityTypeBuilder<ParkingZone> builder)
    {
        builder.ToTable("ParkingZones");
        builder.HasKey(z => z.Id);

        builder.Property(z => z.Code).IsRequired().HasMaxLength(100);
        builder.Property(z => z.Name).IsRequired().HasMaxLength(200);
        builder.Property(z => z.ZoneType).IsRequired().HasMaxLength(100);
        builder.Property(z => z.Level).HasMaxLength(100);

        builder.HasIndex(z => new { z.CommunityId, z.Code })
            .IsUnique()
            .HasDatabaseName("UX_ParkingZone_CommunityId_Code");
    }
}
