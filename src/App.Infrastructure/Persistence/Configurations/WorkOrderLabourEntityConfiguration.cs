using App.Domain.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="WorkOrderLabour"/> table (Req 36.4) — a labour entry recorded against a work
/// order. As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the
/// rowversion mapping from <c>AppDbContext.OnModelCreating</c>; NOT <c>IUnitScoped</c> (maintenance
/// cost data is community infrastructure).
///
/// <para><b>Money precision (steering).</b> <see cref="WorkOrderLabour.Hours"/> /
/// <see cref="WorkOrderLabour.HourlyRate"/> / <see cref="WorkOrderLabour.LabourCost"/> are mapped
/// <c>decimal(18,2)</c> so costs are exact, never floating point. The
/// <c>(CommunityId, WorkOrderId)</c> index backs "a work order's labour" as a single indexed read
/// (no N+1). The work-order FK is <c>Restrict</c> on delete; deletes are soft deletes anyway.</para>
/// </summary>
public sealed class WorkOrderLabourEntityConfiguration : IEntityTypeConfiguration<WorkOrderLabour>
{
    public void Configure(EntityTypeBuilder<WorkOrderLabour> builder)
    {
        builder.ToTable("WorkOrderLabour");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.WorkerName).IsRequired().HasMaxLength(200);
        builder.Property(l => l.Notes).HasMaxLength(2000);
        builder.Property(l => l.Hours).HasPrecision(18, 2);
        builder.Property(l => l.HourlyRate).HasPrecision(18, 2);
        builder.Property(l => l.LabourCost).HasPrecision(18, 2);

        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasForeignKey(l => l.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => new { l.CommunityId, l.WorkOrderId })
            .HasDatabaseName("IX_WorkOrderLabour_CommunityId_WorkOrderId");
    }
}
