using App.Domain.Community;
using App.Domain.Parcel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="Parcel"/> table (Req 28.1–28.4, 29.3). As a <c>TenantOwnedEntity</c> that is
/// also <c>IUnitScoped</c> it inherits the tenant + soft-delete + resident self-scope global filter
/// (Req 5.4) and the rowversion mapping from <c>AppDbContext.OnModelCreating</c> — a resident only
/// sees parcels addressed to their own recipient unit, and the rowversion makes the status transition
/// record exactly once under concurrency (Req 28.3, design "RowVersion on Parcel status").
///
/// <para>The <c>(CommunityId, UnitId, Status)</c> index backs the recipient unit's parcel list +
/// the resident self-view as a single indexed read (no N+1); <c>(CommunityId, Status)</c> backs the
/// gate's status-filtered queue (e.g. in-custody / ready-for-pickup). The recipient-unit FK is
/// <c>Restrict</c> on delete; deletes are soft deletes anyway. <c>RecipientUnitId</c> is a get/set
/// alias over <c>UnitId</c> and is not a stored column.</para>
/// </summary>
public sealed class ParcelEntityConfiguration : IEntityTypeConfiguration<Domain.Parcel.Parcel>
{
    public void Configure(EntityTypeBuilder<Domain.Parcel.Parcel> builder)
    {
        builder.ToTable("Parcels");
        builder.HasKey(p => p.Id);

        // RecipientUnitId is a get/set alias over UnitId; not a stored column.
        builder.Ignore(p => p.RecipientUnitId);

        builder.Property(p => p.TrackingNumber).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Provider).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Category).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Status).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Condition).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Receiver).HasMaxLength(200);
        builder.Property(p => p.ReceivedLocation).HasMaxLength(200);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(p => p.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Active custody location (Task 18.2, Req 29.1). Optional FK, Restrict on delete so a custody
        // location in use can't be hard-deleted out from under a held parcel. The "one active custody
        // location → one parcel" uniqueness (Req 29.4) is the filtered unique index on
        // (CustodyLocationId) WHERE CustodyLocationId IS NOT NULL added in AppDbContext.OnModelCreating.
        builder.HasOne<CustodyLocation>()
            .WithMany()
            .HasForeignKey(p => p.CustodyLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        // The recipient unit's parcel list + the resident self-view list (Req 28.5, 5.4).
        builder.HasIndex(p => new { p.CommunityId, p.UnitId, p.Status })
            .HasDatabaseName("IX_Parcel_CommunityId_UnitId_Status");

        // The gate's status-filtered queue (received / in-custody / ready-for-pickup), Req 28.5.
        builder.HasIndex(p => new { p.CommunityId, p.Status })
            .HasDatabaseName("IX_Parcel_CommunityId_Status");
    }
}
