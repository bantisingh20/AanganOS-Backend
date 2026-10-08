using CommunityOS.Domain.Community;
using CommunityOS.Domain.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunityOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="Asset"/> table (Req 35.1-35.4) — the maintenance asset register. As a
/// <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped (Req 35.3) and a
/// soft-deleted/archived asset stays in the table but is hidden from ordinary reads (Req 35.4). It
/// is deliberately NOT <c>IUnitScoped</c> — an asset is community infrastructure (same stance as
/// <see cref="CustodyLocationEntityConfiguration"/> / ParkingSlot / StorageLocation), even when its
/// optional <c>UnitId</c> ties it to a unit.
///
/// <para><b>Indexed opaque-token lookup (Req 35.2) — same pattern as <c>VisitPass</c> /
/// <c>ParcelAuthorization</c>.</b> The unique index on <c>(CommunityId, QrToken)</c> is both the
/// community-uniqueness guarantee for the scannable token and the single indexed read a QR scan
/// resolves through. <c>(CommunityId, SerialNumber)</c> is the DB-enforced serial-number uniqueness
/// within a community (Req 35.1).</para>
///
/// <para>The <c>(CommunityId, Status, AssetType)</c> index backs the register's status/type-filtered
/// list (Req 35.3) as a single indexed read (no N+1). The optional unit FK is <c>Restrict</c> on
/// delete; deletes are soft deletes anyway.</para>
/// </summary>
public sealed class AssetEntityConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("Assets");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
        builder.Property(a => a.SerialNumber).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Location).IsRequired().HasMaxLength(300);
        builder.Property(a => a.AssetType).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Category).HasMaxLength(100);
        builder.Property(a => a.Status).IsRequired().HasMaxLength(50);
        builder.Property(a => a.QrToken).IsRequired().HasMaxLength(128);
        builder.Property(a => a.WarrantyProvider).HasMaxLength(200);
        builder.Property(a => a.WarrantyReference).HasMaxLength(200);
        builder.Property(a => a.Notes).HasMaxLength(2000);

        // Optional unit the asset is tied to (Req 35.1). Restrict on delete so a unit referenced by
        // an asset can't be hard-deleted out from under it; deletes are soft deletes anyway.
        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(a => a.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Opaque-token uniqueness within a community + the QR-scan lookup path (Req 35.2): a scan
        // resolves to exactly one asset via this index.
        builder.HasIndex(a => new { a.CommunityId, a.QrToken })
            .IsUnique()
            .HasDatabaseName("UX_Asset_CommunityId_QrToken");

        // Serial-number uniqueness within a community (Req 35.1): the DB-level source of truth, so a
        // duplicate maps to CONFLICT via UniqueConstraintGuard rather than a read-then-write pre-check.
        builder.HasIndex(a => new { a.CommunityId, a.SerialNumber })
            .IsUnique()
            .HasDatabaseName("UX_Asset_CommunityId_SerialNumber");

        // The register's status/type-filtered list (Req 35.3) as a single indexed read, no N+1.
        builder.HasIndex(a => new { a.CommunityId, a.Status, a.AssetType })
            .HasDatabaseName("IX_Asset_CommunityId_Status_AssetType");
    }
}
