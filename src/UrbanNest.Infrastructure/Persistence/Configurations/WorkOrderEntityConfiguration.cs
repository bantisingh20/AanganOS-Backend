using UrbanNest.Domain.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="WorkOrder"/> table (Req 36.1-36.5) — preventive/corrective maintenance work
/// orders. As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the
/// rowversion mapping from <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped and the
/// assignment / status transitions record exactly once under concurrency. It is deliberately NOT
/// <c>IUnitScoped</c> — a work order is community infrastructure (same stance as
/// <see cref="AssetEntityConfiguration"/> / <see cref="MaintenancePlanEntityConfiguration"/>).
///
/// <para><b>List indexes (Req 36.2, 36.3).</b> <c>(CommunityId, Status, Priority)</c> backs the
/// status/priority-filtered work-order list; <c>(CommunityId, AssetId)</c> backs an asset's work
/// orders; <c>(CommunityId, AssignedToUserId)</c> backs "my assigned work orders" — each a single
/// indexed read (no N+1). <c>(CommunityId, MaintenancePlanId, ScheduledForUtc)</c> lets the preventive
/// sweep check whether an occurrence already produced a work order. The asset / plan FKs are
/// <c>Restrict</c> on delete; deletes are soft deletes anyway.</para>
/// </summary>
public sealed class WorkOrderEntityConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> builder)
    {
        builder.ToTable("WorkOrders");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Title).IsRequired().HasMaxLength(200);
        builder.Property(w => w.Description).IsRequired().HasMaxLength(4000);
        builder.Property(w => w.WorkOrderType).IsRequired().HasMaxLength(50);
        builder.Property(w => w.Priority).IsRequired().HasMaxLength(50);
        builder.Property(w => w.Status).IsRequired().HasMaxLength(50);
        builder.Property(w => w.CompletionNotes).HasMaxLength(4000);

        // Link to the asset (Req 36.1, 36.2). Restrict on delete so a referenced asset can't be
        // hard-deleted out from under its work orders; deletes are soft deletes anyway.
        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(w => w.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional link to the generating plan (Req 36.1). Restrict so a plan isn't hard-deleted out
        // from under the preventive work orders it produced.
        builder.HasOne<MaintenancePlan>()
            .WithMany()
            .HasForeignKey(w => w.MaintenancePlanId)
            .OnDelete(DeleteBehavior.Restrict);

        // Status/priority-filtered list (Req 36.2) as a single indexed read.
        builder.HasIndex(w => new { w.CommunityId, w.Status, w.Priority })
            .HasDatabaseName("IX_WorkOrder_CommunityId_Status_Priority");

        // An asset's work orders (Req 36.2, 36.5).
        builder.HasIndex(w => new { w.CommunityId, w.AssetId })
            .HasDatabaseName("IX_WorkOrder_CommunityId_AssetId");

        // "My assigned work orders" (Req 36.3) — staff-facing list.
        builder.HasIndex(w => new { w.CommunityId, w.AssignedToUserId })
            .HasDatabaseName("IX_WorkOrder_CommunityId_AssignedToUserId");

        // Preventive at-most-once check: has this plan already produced a work order for this
        // occurrence? (Req 36.1). Backs the sweep's existence read.
        builder.HasIndex(w => new { w.CommunityId, w.MaintenancePlanId, w.ScheduledForUtc })
            .HasDatabaseName("IX_WorkOrder_CommunityId_MaintenancePlanId_ScheduledForUtc");
    }
}
