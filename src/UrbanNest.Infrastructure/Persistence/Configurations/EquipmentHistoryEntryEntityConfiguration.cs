using UrbanNest.Domain.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the append-only <see cref="EquipmentHistoryEntry"/> register (Req 36.5). As a
/// <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>, so a history read is tenant-scoped with no
/// bespoke predicate. The <c>AuditSaveChangesInterceptor</c> independently rejects any
/// Modified/Deleted state on this type, making the register append-only at the data layer (Req 36.5)
/// — the same guard it applies to <c>AuditLog</c> / <c>CustodyTimelineEntry</c>.
///
/// <para><b>Chronological-read index (Req 36.5).</b> <c>(CommunityId, AssetId, OccurredAtUtc)</c>
/// backs "an asset's equipment history in chronological order" as a single ordered indexed read (no
/// N+1), bounded by the tenant filter.</para>
///
/// <para>The asset / work-order FKs are <c>Restrict</c> on delete so neither can be hard-deleted out
/// from under its history; rows are retained, never physically removed (Req 36.5). No inverse
/// navigation is declared to keep those entities untouched.</para>
/// </summary>
public sealed class EquipmentHistoryEntryEntityConfiguration : IEntityTypeConfiguration<EquipmentHistoryEntry>
{
    public void Configure(EntityTypeBuilder<EquipmentHistoryEntry> builder)
    {
        builder.ToTable("EquipmentHistoryEntries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Summary).IsRequired().HasMaxLength(400);
        builder.Property(e => e.Detail).HasMaxLength(2000);
        builder.Property(e => e.ActingUserId).HasMaxLength(200);

        // Link to the asset (Req 36.5). Restrict on delete so the history stays traceable (soft
        // delete archives the asset without removing its history).
        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(e => e.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional link to the completed work order (Req 36.5). Restrict so a work order isn't
        // hard-deleted out from under the history entry it produced.
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasForeignKey(e => e.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chronological per-asset read (Req 36.5).
        builder.HasIndex(e => new { e.CommunityId, e.AssetId, e.OccurredAtUtc })
            .HasDatabaseName("IX_EquipmentHistoryEntry_CommunityId_AssetId_OccurredAtUtc");
    }
}
