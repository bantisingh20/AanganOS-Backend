using App.Domain.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="InspectionChecklist"/> table and its owned <see cref="InspectionChecklistItem"/>
/// child rows (Req 37.1). As <c>TenantOwnedEntity</c>s both inherit the tenant + soft-delete global
/// filter and the rowversion mapping from <c>AppDbContext.OnModelCreating</c>, so reads are
/// tenant-scoped and record exactly once under concurrency. Neither is <c>IUnitScoped</c> — community
/// infrastructure (same stance as <see cref="WorkOrderEntityConfiguration"/>).
///
/// <para><b>Indexes (Req 37.1).</b> <c>(CommunityId, AssetId, InspectedAtUtc)</c> backs an asset's
/// inspections in chronological order as a single indexed read; <c>(CommunityId, InspectionChecklistId)</c>
/// backs loading a checklist's items. The asset / work-order FKs are <c>Restrict</c> on delete;
/// deletes are soft deletes anyway. The item→checklist FK <c>Cascade</c>s so items follow their parent.</para>
/// </summary>
public sealed class InspectionChecklistEntityConfiguration : IEntityTypeConfiguration<InspectionChecklist>
{
    public void Configure(EntityTypeBuilder<InspectionChecklist> builder)
    {
        builder.ToTable("InspectionChecklists");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Name).IsRequired().HasMaxLength(200);
        builder.Property(i => i.OverallResult).IsRequired().HasMaxLength(50);
        builder.Property(i => i.InspectedByUserId).HasMaxLength(200);
        builder.Property(i => i.Notes).HasMaxLength(2000);

        // Link to the asset (Req 37.1). Restrict on delete so a referenced asset can't be hard-deleted
        // out from under its inspections; deletes are soft deletes anyway.
        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(i => i.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional link to the work order the inspection was performed under (Req 37.1).
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasForeignKey(i => i.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Owned items collection (Req 37.1).
        builder.HasMany(i => i.Items)
            .WithOne()
            .HasForeignKey(it => it.InspectionChecklistId)
            .OnDelete(DeleteBehavior.Cascade);

        // Chronological per-asset read (Req 37.1).
        builder.HasIndex(i => new { i.CommunityId, i.AssetId, i.InspectedAtUtc })
            .HasDatabaseName("IX_InspectionChecklist_CommunityId_AssetId_InspectedAtUtc");
    }
}

/// <summary>
/// Maps the <see cref="InspectionChecklistItem"/> child rows (Req 37.1). Carries the parent's
/// community; the <c>(CommunityId, InspectionChecklistId)</c> index backs loading a checklist's items
/// in a single read.
/// </summary>
public sealed class InspectionChecklistItemEntityConfiguration : IEntityTypeConfiguration<InspectionChecklistItem>
{
    public void Configure(EntityTypeBuilder<InspectionChecklistItem> builder)
    {
        builder.ToTable("InspectionChecklistItems");
        builder.HasKey(it => it.Id);

        builder.Property(it => it.ItemText).IsRequired().HasMaxLength(400);
        builder.Property(it => it.Result).IsRequired().HasMaxLength(50);
        builder.Property(it => it.Remark).HasMaxLength(1000);

        builder.HasIndex(it => new { it.CommunityId, it.InspectionChecklistId })
            .HasDatabaseName("IX_InspectionChecklistItem_CommunityId_InspectionChecklistId");
    }
}
