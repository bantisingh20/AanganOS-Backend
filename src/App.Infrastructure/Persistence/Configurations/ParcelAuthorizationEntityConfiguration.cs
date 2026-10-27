using App.Domain.Parcel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="ParcelAuthorization"/> table (Req 30.1, 30.2). As a <c>TenantOwnedEntity</c>
/// that is also <c>IUnitScoped</c> it inherits the tenant + soft-delete + resident self-scope global
/// filter (Req 5.4) and the rowversion mapping from <c>AppDbContext.OnModelCreating</c> — a resident
/// principal only sees authorizations for their own unit, and the rowversion makes consuming an
/// authorization on handover record exactly once under concurrency (Req 30.4).
///
/// <para><b>Indexed opaque-token lookup (Req 30.2) — same pattern as <c>VisitPass</c>.</b> The unique
/// index on <c>(CommunityId, QrToken)</c> is both the uniqueness guarantee for the opaque QR token
/// within a community and the single indexed read the guard's QR handover rides. The
/// <c>(CommunityId, ParcelId)</c> index backs "an authorization for this parcel" lookup at handover.</para>
///
/// <para>The parcel FK is <c>Restrict</c> on delete (deletes are soft deletes anyway);
/// <c>RecipientUnitId</c>-style <c>UnitId</c> is the <c>IUnitScoped</c> backing column.</para>
/// </summary>
public sealed class ParcelAuthorizationEntityConfiguration : IEntityTypeConfiguration<ParcelAuthorization>
{
    public void Configure(EntityTypeBuilder<ParcelAuthorization> builder)
    {
        builder.ToTable("ParcelAuthorizations");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.CollectorName).IsRequired().HasMaxLength(200);
        builder.Property(a => a.CollectorRelationship).HasMaxLength(200);
        builder.Property(a => a.ExpectedIdReference).HasMaxLength(200);
        builder.Property(a => a.QrToken).IsRequired().HasMaxLength(128);
        builder.Property(a => a.OtpHash).IsRequired().HasMaxLength(256);
        builder.Property(a => a.VerificationMethod).IsRequired().HasMaxLength(50);

        builder.HasOne<Domain.Parcel.Parcel>()
            .WithMany()
            .HasForeignKey(a => a.ParcelId)
            .OnDelete(DeleteBehavior.Restrict);

        // Opaque-token uniqueness within a community + the guard QR-handover lookup path (Req 30.2):
        // a QR scan resolves to exactly one authorization via this index.
        builder.HasIndex(a => new { a.CommunityId, a.QrToken })
            .IsUnique()
            .HasDatabaseName("UX_ParcelAuthorization_CommunityId_QrToken");

        // "The authorizations for this parcel" lookup at handover (Req 30.2) and the resident
        // self-view list.
        builder.HasIndex(a => new { a.CommunityId, a.ParcelId })
            .HasDatabaseName("IX_ParcelAuthorization_CommunityId_ParcelId");
    }
}
