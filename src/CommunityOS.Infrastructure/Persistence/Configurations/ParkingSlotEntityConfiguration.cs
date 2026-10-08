using CommunityOS.Domain.Community;
using CommunityOS.Domain.Parking;
using CommunityOS.Domain.Residents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunityOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="ParkingSlot"/> table (Req 19.4, 40.1). As a <c>TenantOwnedEntity</c> it
/// inherits the tenant + soft-delete global filter and the rowversion mapping from
/// <c>AppDbContext.OnModelCreating</c>; its <c>RowVersion</c> guards slot edits.
///
/// <para>Unique <c>(CommunityId, Code)</c> keeps slot codes distinct within a community. The optional
/// legacy <c>AssignedUnitId</c> FK (Phase 2, Req 19.4) is <c>Restrict</c> on delete; the
/// <c>(CommunityId, AssignedUnitId)</c> index backs the Phase 2 "a unit's assigned slots" read.</para>
///
/// <para><b>Phase 6 layout (Req 40.1).</b> The optional <c>ZoneId</c> FK links the slot to a
/// <see cref="ParkingZone"/> (<c>Restrict</c> on delete so a zone with slots cannot be hard-deleted
/// out from under them). The <c>(CommunityId, ZoneId, Level)</c> index is the layout/occupancy read
/// path — the occupancy dashboard groups active-assignment counts by zone/level over it (no scan).
/// <c>SlotType</c>/<c>Level</c>/<c>SlotNumber</c> are the configurable layout columns. The
/// authoritative "one active assignment per slot" invariant lives on the <c>ParkingAssignment</c>
/// side (its filtered unique index), NOT here — the legacy <c>AssignedUnitId</c> is retained for the
/// Phase 2 flow but is no longer the source of truth.</para>
/// </summary>
public sealed class ParkingSlotEntityConfiguration : IEntityTypeConfiguration<ParkingSlot>
{
    public void Configure(EntityTypeBuilder<ParkingSlot> builder)
    {
        builder.ToTable("ParkingSlots");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Code).IsRequired().HasMaxLength(100);
        builder.Property(s => s.Level).HasMaxLength(100);
        builder.Property(s => s.SlotNumber).HasMaxLength(100);
        builder.Property(s => s.SlotType).IsRequired().HasMaxLength(100);

        // Legacy Phase 2 unit association (Req 19.4). Restrict on delete; retained for backward compat.
        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(s => s.AssignedUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Phase 6 layout zone (Req 40.1). Restrict: a zone with slots cannot be hard-deleted.
        builder.HasOne<ParkingZone>()
            .WithMany()
            .HasForeignKey(s => s.ZoneId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.CommunityId, s.Code })
            .IsUnique()
            .HasDatabaseName("UX_ParkingSlot_CommunityId_Code");

        builder.HasIndex(s => new { s.CommunityId, s.AssignedUnitId })
            .HasDatabaseName("IX_ParkingSlot_CommunityId_AssignedUnitId");

        // Layout + occupancy read path (Req 40.1, 40.5): slots grouped by zone/level in one indexed
        // read — the occupancy dashboard projects active-assignment counts over this.
        builder.HasIndex(s => new { s.CommunityId, s.ZoneId, s.Level })
            .HasDatabaseName("IX_ParkingSlot_CommunityId_ZoneId_Level");
    }
}
