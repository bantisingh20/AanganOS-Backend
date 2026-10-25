using UrbanNest.Domain.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="MeterReading"/> table (Req 37.4, 37.5). As a <c>TenantOwnedEntity</c> it
/// inherits the tenant + soft-delete global filter and the rowversion mapping from
/// <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped. NOT <c>IUnitScoped</c> — community
/// infrastructure (same stance as <see cref="WorkOrderEntityConfiguration"/>).
///
/// <para><b>Chronological / latest-reading index (Req 37.4, 37.5).</b>
/// <c>(CommunityId, AssetId, MeterType, ReadingAtUtc)</c> backs both the chronological per-asset-meter
/// read AND the "latest recorded reading for this asset + meter" lookup the service uses to enforce
/// the non-decreasing (monotonic) rule — a single ordered indexed read (no scan, no N+1). The asset
/// FK is <c>Restrict</c> on delete; deletes are soft deletes anyway.</para>
/// </summary>
public sealed class MeterReadingEntityConfiguration : IEntityTypeConfiguration<MeterReading>
{
    public void Configure(EntityTypeBuilder<MeterReading> builder)
    {
        builder.ToTable("MeterReadings");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.MeterType).IsRequired().HasMaxLength(50);
        builder.Property(m => m.Unit).IsRequired().HasMaxLength(50);
        builder.Property(m => m.Value).HasPrecision(18, 2);
        builder.Property(m => m.RecordedByUserId).HasMaxLength(200);
        builder.Property(m => m.Notes).HasMaxLength(1000);

        // Link to the asset (Req 37.4). Restrict on delete so a referenced asset can't be hard-deleted
        // out from under its readings; deletes are soft deletes anyway.
        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(m => m.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chronological per-asset-meter read + latest-reading (monotonic guard) lookup (Req 37.4, 37.5).
        builder.HasIndex(m => new { m.CommunityId, m.AssetId, m.MeterType, m.ReadingAtUtc })
            .HasDatabaseName("IX_MeterReading_CommunityId_AssetId_MeterType_ReadingAtUtc");
    }
}
