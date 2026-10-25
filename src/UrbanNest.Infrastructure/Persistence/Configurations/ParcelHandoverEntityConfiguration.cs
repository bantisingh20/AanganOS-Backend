using UrbanNest.Domain.Parcel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="ParcelHandover"/> table (Req 30.4). As a <c>TenantOwnedEntity</c> that is also
/// <c>IUnitScoped</c> it inherits the tenant + soft-delete + resident self-scope global filter
/// (Req 5.4) and the rowversion mapping from <c>AppDbContext.OnModelCreating</c> — a resident
/// principal only sees handovers for their own unit's parcels.
///
/// <para><b>One handover per parcel (Req 30.4).</b> The unique index on <c>(ParcelId)</c> makes
/// "a parcel is handed over at most once" a hard DB guarantee: even if the idempotency-key wrapper
/// were bypassed, a second handover insert for the same parcel trips this unique constraint. Combined
/// with the <c>Idempotency-Key</c> at-most-once wrapper on the operation this gives belt-and-braces
/// exactly-once handover (Req 30.4, 30.5).</para>
///
/// <para>The parcel + authorization FKs are <c>Restrict</c> on delete (deletes are soft deletes
/// anyway). <c>ProofFileId</c> is the opaque File_Service reference to the captured handover proof.</para>
/// </summary>
public sealed class ParcelHandoverEntityConfiguration : IEntityTypeConfiguration<ParcelHandover>
{
    public void Configure(EntityTypeBuilder<ParcelHandover> builder)
    {
        builder.ToTable("ParcelHandovers");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.CollectorName).IsRequired().HasMaxLength(200);
        builder.Property(h => h.CollectorRelationship).HasMaxLength(200);
        builder.Property(h => h.VerificationMethod).IsRequired().HasMaxLength(50);
        builder.Property(h => h.GuardUserId).HasMaxLength(128);

        builder.HasOne<Domain.Parcel.Parcel>()
            .WithMany()
            .HasForeignKey(h => h.ParcelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ParcelAuthorization>()
            .WithMany()
            .HasForeignKey(h => h.ParcelAuthorizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // One handover per parcel (Req 30.4): the DB guarantee behind at-most-once handover,
        // alongside the Idempotency-Key wrapper (Req 30.5).
        builder.HasIndex(h => h.ParcelId)
            .IsUnique()
            .HasDatabaseName("UX_ParcelHandover_ParcelId");
    }
}
