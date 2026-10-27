using App.Domain.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="WorkOrderPart"/> table (Req 36.4) — a part/material recorded against a work
/// order. As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the
/// rowversion mapping from <c>AppDbContext.OnModelCreating</c>; NOT <c>IUnitScoped</c> (maintenance
/// cost data is community infrastructure).
///
/// <para><b>Money precision (steering).</b> <see cref="WorkOrderPart.Quantity"/> /
/// <see cref="WorkOrderPart.UnitCost"/> / <see cref="WorkOrderPart.LineTotal"/> are mapped
/// <c>decimal(18,2)</c> so costs are exact, never floating point. The
/// <c>(CommunityId, WorkOrderId)</c> index backs "a work order's parts" as a single indexed read
/// (no N+1). The work-order FK is <c>Restrict</c> on delete; deletes are soft deletes anyway.</para>
/// </summary>
public sealed class WorkOrderPartEntityConfiguration : IEntityTypeConfiguration<WorkOrderPart>
{
    public void Configure(EntityTypeBuilder<WorkOrderPart> builder)
    {
        builder.ToTable("WorkOrderParts");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.PartName).IsRequired().HasMaxLength(200);
        builder.Property(p => p.PartNumber).HasMaxLength(100);
        builder.Property(p => p.Quantity).HasPrecision(18, 2);
        builder.Property(p => p.UnitCost).HasPrecision(18, 2);
        builder.Property(p => p.LineTotal).HasPrecision(18, 2);

        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasForeignKey(p => p.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.CommunityId, p.WorkOrderId })
            .HasDatabaseName("IX_WorkOrderPart_CommunityId_WorkOrderId");
    }
}
