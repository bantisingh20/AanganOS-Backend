using App.Domain.Community;
using App.Domain.Residents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="StorageLocation"/> table (Req 19.4) — the storage counterpart of
/// <see cref="ParkingSlot"/>. As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete
/// global filter and the rowversion mapping from <c>AppDbContext.OnModelCreating</c>.
///
/// <para>Unique <c>(CommunityId, Code)</c> keeps location codes distinct within a community; the
/// optional <c>AssignedUnitId</c> FK is <c>Restrict</c> on delete and the <c>(CommunityId,
/// AssignedUnitId)</c> index backs "a unit's assigned storage" as a single indexed read (Req 19.5).
/// ponytail: single reassignable owner, no active-assignment uniqueness — Phase 6 scope.</para>
/// </summary>
public sealed class StorageLocationEntityConfiguration : IEntityTypeConfiguration<StorageLocation>
{
    public void Configure(EntityTypeBuilder<StorageLocation> builder)
    {
        builder.ToTable("StorageLocations");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Code).IsRequired().HasMaxLength(100);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(s => s.AssignedUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.CommunityId, s.Code })
            .IsUnique()
            .HasDatabaseName("UX_StorageLocation_CommunityId_Code");

        builder.HasIndex(s => new { s.CommunityId, s.AssignedUnitId })
            .HasDatabaseName("IX_StorageLocation_CommunityId_AssignedUnitId");
    }
}
