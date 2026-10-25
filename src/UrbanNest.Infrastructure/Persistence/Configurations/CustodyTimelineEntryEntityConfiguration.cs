using UrbanNest.Domain.Parcel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DomainParcel = UrbanNest.Domain.Parcel.Parcel;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the append-only <see cref="CustodyTimelineEntry"/> register (Req 31.1, 31.5). As a
/// <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>, so a timeline read is tenant-scoped (Req 5.2,
/// 31.5) with no bespoke predicate. The <c>AuditSaveChangesInterceptor</c> independently rejects any
/// Modified/Deleted state on this type, making the register append-only at the data layer (Req 31.1)
/// — the same guard it applies to <c>AuditLog</c>.
///
/// <para><b>Chronological-read index (Req 31.5).</b> <c>(CommunityId, ParcelId, OccurredAtUtc)</c>
/// backs "a parcel's custody timeline in chronological order" as a single ordered indexed read (no
/// N+1), bounded by the tenant filter.</para>
///
/// <para>The parcel FK is <c>Restrict</c> on delete so a parcel cannot be hard-deleted out from under
/// its timeline; rows are retained, never physically removed (Req 31.1). No inverse navigation is
/// declared on <see cref="DomainParcel"/> to keep that entity untouched.</para>
/// </summary>
public sealed class CustodyTimelineEntryEntityConfiguration : IEntityTypeConfiguration<CustodyTimelineEntry>
{
    public void Configure(EntityTypeBuilder<CustodyTimelineEntry> builder)
    {
        builder.ToTable("CustodyTimelineEntries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType).IsRequired().HasMaxLength(50);
        builder.Property(e => e.FromStatus).HasMaxLength(50);
        builder.Property(e => e.ToStatus).HasMaxLength(50);
        builder.Property(e => e.ActingUserId).HasMaxLength(200);
        builder.Property(e => e.Detail).HasMaxLength(2000);

        // Link to the parcel (Req 31.1). No navigation on the Parcel side; restrict on delete so the
        // timeline stays traceable (soft delete archives the parcel without removing its history).
        builder.HasOne<DomainParcel>()
            .WithMany()
            .HasForeignKey(e => e.ParcelId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chronological per-parcel read (Req 31.5).
        builder.HasIndex(e => new { e.CommunityId, e.ParcelId, e.OccurredAtUtc })
            .HasDatabaseName("IX_CustodyTimelineEntry_CommunityId_ParcelId_OccurredAtUtc");
    }
}
