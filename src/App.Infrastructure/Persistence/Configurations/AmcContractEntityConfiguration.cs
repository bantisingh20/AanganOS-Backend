using App.Domain.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="AmcContract"/> table (Req 37.2, 37.3). As a <c>TenantOwnedEntity</c> it
/// inherits the tenant + soft-delete global filter and the rowversion mapping from
/// <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped and the expiry-reminder stamp /
/// status edits record exactly once under concurrency. NOT <c>IUnitScoped</c> — community
/// infrastructure (same stance as <see cref="WorkOrderEntityConfiguration"/>).
///
/// <para><b>Expiry-sweep index (Req 37.3).</b> <c>(CommunityId, Status, EndDateUtc)</c> backs the
/// background AMC-expiry sweep's candidate predicate — active contracts ordered by end date — as a
/// single indexed read (no table scan, no N+1), the same shape the ticket SLA sweep's
/// <c>(CommunityId, Status, SlaDueAtUtc)</c> index serves. <c>(CommunityId, AssetId)</c> backs an
/// asset's contracts. The asset FK is <c>Restrict</c> on delete; deletes are soft deletes anyway.</para>
/// </summary>
public sealed class AmcContractEntityConfiguration : IEntityTypeConfiguration<AmcContract>
{
    public void Configure(EntityTypeBuilder<AmcContract> builder)
    {
        builder.ToTable("AmcContracts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.ContractReference).IsRequired().HasMaxLength(200);
        builder.Property(a => a.VendorName).IsRequired().HasMaxLength(200);
        builder.Property(a => a.VendorContact).HasMaxLength(200);
        builder.Property(a => a.Status).IsRequired().HasMaxLength(50);
        builder.Property(a => a.ContractValue).HasPrecision(18, 2);
        builder.Property(a => a.Notes).HasMaxLength(2000);

        // Link to the covered asset (Req 37.2). Restrict on delete so a referenced asset can't be
        // hard-deleted out from under its contracts; deletes are soft deletes anyway.
        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(a => a.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        // AMC-expiry sweep candidate read (Req 37.3): active contracts ordered by end date.
        builder.HasIndex(a => new { a.CommunityId, a.Status, a.EndDateUtc })
            .HasDatabaseName("IX_AmcContract_CommunityId_Status_EndDateUtc");

        // An asset's contracts (Req 37.2).
        builder.HasIndex(a => new { a.CommunityId, a.AssetId })
            .HasDatabaseName("IX_AmcContract_CommunityId_AssetId");
    }
}
