using App.Domain.Parcel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="CustodyLocation"/> table (Req 29.1) — the secure bin/location a parcel is held
/// in while in custody. As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global
/// filter and the rowversion mapping from <c>AppDbContext.OnModelCreating</c>, so reads are
/// tenant-scoped (Req 5.2) — the same shape as <see cref="StorageLocationEntityConfiguration"/>.
///
/// <para>Unique <c>(CommunityId, Code)</c> keeps location codes distinct within a community. The
/// "one active custody location holds at most one parcel" invariant (Req 29.4) is NOT modelled here
/// — it lives on the <c>Parcel.CustodyLocationId</c> side as a filtered unique index added in
/// <c>AppDbContext.OnModelCreating</c> (where the provider is known), mirroring the one-open-owner
/// filtered index on <see cref="OwnershipHistoryEntityConfiguration"/>.</para>
/// </summary>
public sealed class CustodyLocationEntityConfiguration : IEntityTypeConfiguration<CustodyLocation>
{
    public void Configure(EntityTypeBuilder<CustodyLocation> builder)
    {
        builder.ToTable("CustodyLocations");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Code).IsRequired().HasMaxLength(100);

        builder.HasIndex(c => new { c.CommunityId, c.Code })
            .IsUnique()
            .HasDatabaseName("UX_CustodyLocation_CommunityId_Code");
    }
}
