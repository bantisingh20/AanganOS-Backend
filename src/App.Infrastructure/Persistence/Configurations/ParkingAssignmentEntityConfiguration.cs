using App.Domain.Community;
using App.Domain.Gate;
using App.Domain.Parking;
using App.Domain.Residents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the append-only <see cref="ParkingAssignment"/> register (Req 40.2, 40.3). As a
/// <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>, so history reads are tenant-scoped (Req 5.2) and
/// a close (EndedAtUtc stamp) is concurrency-checked.
///
/// <para><b>Current-assignment + chronological read index (Req 40.3, 40.5).</b>
/// <c>(CommunityId, ParkingSlotId, StartedAtUtc)</c> backs "a slot's assignment history in
/// chronological order" and the "find the active row for this slot" lookup the reassign/release path
/// runs — a single ordered indexed read, no N+1.</para>
///
/// <para><b>One active assignment per slot (Req 40.3 — the headline invariant).</b> The service closes
/// the prior active row and inserts the new one in one transaction so a slot never has two active
/// assignments. On SQL Server a filtered unique index on <c>(ParkingSlotId) WHERE EndedAtUtc IS NULL</c>
/// is added in <c>AppDbContext.OnModelCreating</c> (where the provider is known) as a hard DB
/// guarantee — two concurrent assignments of the SAME slot race to the index and exactly one wins; the
/// loser's unique violation maps to CONFLICT via <c>UniqueConstraintGuard</c>. ponytail: the SQLite
/// test fixture has no filtered-index equivalent wired here, so under that provider the invariant rests
/// on the single-transaction close+insert rather than a DB constraint — acceptable for the test path;
/// production (SQL Server) gets the index. Same approach as the one-open-owner
/// <see cref="OwnershipHistoryEntityConfiguration"/> and the one-active-custody-location Parcel index.</para>
///
/// <para>Relationships are <c>Restrict</c> on delete so a slot/unit/visitor cannot be hard-deleted out
/// from under its assignment history; rows are retained, never physically removed.</para>
/// </summary>
public sealed class ParkingAssignmentEntityConfiguration : IEntityTypeConfiguration<ParkingAssignment>
{
    public void Configure(EntityTypeBuilder<ParkingAssignment> builder)
    {
        builder.ToTable("ParkingAssignments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.AssignmentType).IsRequired().HasMaxLength(100);

        // Link to the slot (Req 40.2). No navigation on the slot side; restrict on delete.
        builder.HasOne<ParkingSlot>()
            .WithMany()
            .HasForeignKey(a => a.ParkingSlotId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional resident-unit subject (Req 40.2). Restrict: keep history traceable.
        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(a => a.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional visitor subject (Req 40.2, Task 24.2 guest parking). Restrict on delete.
        builder.HasOne<Visitor>()
            .WithMany()
            .HasForeignKey(a => a.VisitorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chronological read + active-row lookup per slot (Req 40.3, 40.5).
        builder.HasIndex(a => new { a.CommunityId, a.ParkingSlotId, a.StartedAtUtc })
            .HasDatabaseName("IX_ParkingAssignment_CommunityId_ParkingSlotId_StartedAtUtc");
    }
}
