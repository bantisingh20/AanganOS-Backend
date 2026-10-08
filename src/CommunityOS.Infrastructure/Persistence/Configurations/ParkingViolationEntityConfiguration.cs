using CommunityOS.Domain.Community;
using CommunityOS.Domain.Parking;
using CommunityOS.Domain.Residents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunityOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="ParkingViolation"/> table (Req 40.4). As a <c>TenantOwnedEntity</c> it inherits
/// the tenant + soft-delete global filter and the rowversion mapping from
/// <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped (Req 5.2) and status edits are
/// concurrency-checked.
///
/// <para>The violation always names its <c>ParkingSlotId</c> and may name the offending
/// <c>VehicleId</c> and/or the responsible resident <c>UnitId</c> — all <c>Restrict</c> on delete so
/// the referenced rows cannot be hard-deleted out from under a violation record. The
/// <c>EvidenceFileId</c> is a plain opaque File_Service reference (no FK — the file lives in the
/// File_Service metadata table and is reached only through its authorized download path), mirroring
/// <c>TicketAttachment.FileId</c>. The <c>(CommunityId, ParkingSlotId, RecordedAtUtc)</c> index is the
/// chronological per-slot violation read path (no scan).</para>
/// </summary>
public sealed class ParkingViolationEntityConfiguration : IEntityTypeConfiguration<ParkingViolation>
{
    public void Configure(EntityTypeBuilder<ParkingViolation> builder)
    {
        builder.ToTable("ParkingViolations");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.ViolationType).IsRequired().HasMaxLength(100);
        builder.Property(v => v.Description).IsRequired().HasMaxLength(2000);
        builder.Property(v => v.Status).IsRequired().HasMaxLength(100);

        // Link to the slot (Req 40.4). Restrict on delete; deletes are soft deletes.
        builder.HasOne<ParkingSlot>()
            .WithMany()
            .HasForeignKey(v => v.ParkingSlotId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional offending vehicle (Req 40.4). Restrict on delete.
        builder.HasOne<Vehicle>()
            .WithMany()
            .HasForeignKey(v => v.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional responsible unit (Req 40.4). Restrict on delete.
        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(v => v.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chronological per-slot violation read path (Req 40.4).
        builder.HasIndex(v => new { v.CommunityId, v.ParkingSlotId, v.RecordedAtUtc })
            .HasDatabaseName("IX_ParkingViolation_CommunityId_ParkingSlotId_RecordedAtUtc");
    }
}
