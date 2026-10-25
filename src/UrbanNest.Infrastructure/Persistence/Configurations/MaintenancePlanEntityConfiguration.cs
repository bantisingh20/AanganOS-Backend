using UrbanNest.Domain.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="MaintenancePlan"/> table (Req 36.1) — a preventive-maintenance schedule for an
/// asset. As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the
/// rowversion mapping from <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped and a
/// retired/soft-deleted plan stays traceable but is hidden from ordinary reads. It is deliberately
/// NOT <c>IUnitScoped</c> — a plan is community infrastructure (same stance as
/// <see cref="AssetEntityConfiguration"/>).
///
/// <para><b>Due-plan sweep index (Req 36.1).</b> <c>(CommunityId, IsActive, NextDueAtUtc)</c> backs
/// the background sweep's "active plans whose next occurrence is due" predicate as a single ordered
/// indexed read (no table scan, no N+1), so the preventive-generation pass stays fast and bounded at
/// scale. The asset FK is <c>Restrict</c> on delete so an asset can't be hard-deleted out from under
/// its plans; deletes are soft deletes anyway.</para>
/// </summary>
public sealed class MaintenancePlanEntityConfiguration : IEntityTypeConfiguration<MaintenancePlan>
{
    public void Configure(EntityTypeBuilder<MaintenancePlan> builder)
    {
        builder.ToTable("MaintenancePlans");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.Frequency).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Priority).IsRequired().HasMaxLength(50);

        // Link to the scheduled asset (Req 36.1). Restrict on delete so a referenced asset can't be
        // hard-deleted out from under its plans; deletes are soft deletes anyway.
        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(p => p.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        // The preventive-generation sweep's due-plan read (Req 36.1): active plans ordered by their
        // next occurrence, bounded by the tenant filter — a single indexed read.
        builder.HasIndex(p => new { p.CommunityId, p.IsActive, p.NextDueAtUtc })
            .HasDatabaseName("IX_MaintenancePlan_CommunityId_IsActive_NextDueAtUtc");

        // The asset's plans list (Req 36.1) as a single indexed read.
        builder.HasIndex(p => new { p.CommunityId, p.AssetId })
            .HasDatabaseName("IX_MaintenancePlan_CommunityId_AssetId");
    }
}
