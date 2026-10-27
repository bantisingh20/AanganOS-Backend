using App.Domain.Community;
using App.Domain.Parking;
using App.Domain.Residents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the append-only <see cref="EvReservation"/> register (Req 41.2, 41.3, Task 24.2). As a
/// <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>, so reservation reads are tenant-scoped (Req 5.2)
/// and a close (EndedAtUtc stamp) / status edit is concurrency-checked.
///
/// <para><b>Current-reservation + chronological read index (Req 41.2, 41.3).</b>
/// <c>(CommunityId, ParkingSlotId, ReservedFromUtc)</c> backs "an EV slot's reservation history" and
/// the "find the active reservation for this slot" lookup the release path runs. The
/// <c>(CommunityId, UnitId)</c> index backs the per-unit concurrent-reservation-limit count (Req 41.2)
/// — a single indexed count, no scan.</para>
///
/// <para><b>One active reservation per EV slot (Req 41.3 — the headline invariant).</b> The service
/// inserts the active reservation row inside one transaction and never leaves two active rows for a
/// slot. On SQL Server a filtered unique index on <c>(ParkingSlotId) WHERE EndedAtUtc IS NULL</c> is
/// added in <c>AppDbContext.OnModelCreating</c> (where the provider is known) as a hard DB guarantee —
/// two concurrent reservations of the SAME EV slot race to the index and exactly one wins; the loser's
/// unique violation maps to CONFLICT via <c>UniqueConstraintGuard</c>. ponytail: the SQLite test
/// fixture has no filtered-index equivalent wired here, so under that provider the invariant rests on
/// the single-transaction insert rather than a DB constraint — acceptable for the test path; production
/// (SQL Server) gets the index. Same approach as the one-active-assignment
/// <see cref="ParkingAssignmentEntityConfiguration"/>.</para>
///
/// <para>Relationships are <c>Restrict</c> on delete so a slot/unit cannot be hard-deleted out from
/// under its reservation history; the metered <c>EnergyKwh</c> is <c>decimal(18,3)</c> (metered
/// precision), never a float.</para>
/// </summary>
public sealed class EvReservationEntityConfiguration : IEntityTypeConfiguration<EvReservation>
{
    public void Configure(EntityTypeBuilder<EvReservation> builder)
    {
        builder.ToTable("EvReservations");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status).IsRequired().HasMaxLength(100);
        builder.Property(r => r.EnergyKwh).HasColumnType("decimal(18,3)");

        // Link to the EV slot (Req 41.2). No navigation on the slot side; restrict on delete.
        builder.HasOne<ParkingSlot>()
            .WithMany()
            .HasForeignKey(r => r.ParkingSlotId)
            .OnDelete(DeleteBehavior.Restrict);

        // Reserving unit (Req 41.2). Restrict: keep history traceable.
        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(r => r.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chronological read + active-row lookup per slot (Req 41.2, 41.3).
        builder.HasIndex(r => new { r.CommunityId, r.ParkingSlotId, r.ReservedFromUtc })
            .HasDatabaseName("IX_EvReservation_CommunityId_ParkingSlotId_ReservedFromUtc");

        // Per-unit concurrent-reservation-limit count path (Req 41.2).
        builder.HasIndex(r => new { r.CommunityId, r.UnitId })
            .HasDatabaseName("IX_EvReservation_CommunityId_UnitId");
    }
}
