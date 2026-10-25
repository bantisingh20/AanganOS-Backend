using UrbanNest.Domain.Amenities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="Amenity"/> table (Req 38.1) — the bookable-amenity master. As a
/// <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped and a soft-deleted/
/// retired amenity stays traceable but is hidden from ordinary reads. It is deliberately NOT
/// <c>IUnitScoped</c> — an amenity is community infrastructure (same stance as
/// <see cref="AssetEntityConfiguration"/>).
///
/// <para><b>Community-unique code (Req 38.1).</b> The unique index on <c>(CommunityId, Code)</c> is
/// the DB-enforced identity of an amenity within a community, so a duplicate maps to CONFLICT via
/// <c>UniqueConstraintGuard</c> rather than a read-then-write pre-check. The
/// <c>(CommunityId, Status, AmenityType)</c> index backs the register's status/type-filtered list
/// (Req 38.1) as a single indexed read (no N+1).</para>
/// </summary>
public sealed class AmenityEntityConfiguration : IEntityTypeConfiguration<Amenity>
{
    public void Configure(EntityTypeBuilder<Amenity> builder)
    {
        builder.ToTable("Amenities");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Code).IsRequired().HasMaxLength(100);
        builder.Property(a => a.AmenityType).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Status).IsRequired().HasMaxLength(50);
        builder.Property(a => a.Location).HasMaxLength(300);
        builder.Property(a => a.Description).HasMaxLength(2000);

        // Code uniqueness within a community (Req 38.1): the DB-level source of truth, so a duplicate
        // maps to CONFLICT via UniqueConstraintGuard rather than a read-then-write pre-check.
        builder.HasIndex(a => new { a.CommunityId, a.Code })
            .IsUnique()
            .HasDatabaseName("UX_Amenity_CommunityId_Code");

        // The register's status/type-filtered list (Req 38.1) as a single indexed read, no N+1.
        builder.HasIndex(a => new { a.CommunityId, a.Status, a.AmenityType })
            .HasDatabaseName("IX_Amenity_CommunityId_Status_AmenityType");
    }
}
