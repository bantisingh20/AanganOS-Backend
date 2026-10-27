using App.Domain.Amenities;
using App.Domain.Community;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="ResidentEvent"/> table (Req 41.4, 41.5, Task 24.2) — a resident-published
/// event with a schedule and a capacity. As a <c>TenantOwnedEntity</c> AND <c>IUnitScoped</c>, the
/// <c>AppDbContext.OnModelCreating</c> loop wires the tenant + soft-delete global filter AND the
/// resident self-scope filter onto it (keyed on <c>UnitId</c> = the organiser's host unit), so a
/// resident principal manages only their OWN unit's events; the rowversion guards edits.
///
/// <para><b>Capacity concurrency anchor (Req 41.5).</b> The event's inline
/// <c>RegisteredCount</c>/<c>Capacity</c> are the seat ledger: the registration flow claims a seat
/// with the atomic guarded UPDATE (<c>SET RegisteredCount = RegisteredCount + 1 WHERE RegisteredCount
/// &lt; Capacity</c>) so two residents racing the last seat serialize at the row — exactly one
/// registers, the other is rejected (no oversell, no read-then-write gap). The
/// <c>(CommunityId, UnitId)</c> index backs the organiser's "my events" read; the
/// <c>(CommunityId, StartsAtUtc)</c> index backs the community-wide upcoming-events browse.</para>
/// </summary>
public sealed class ResidentEventEntityConfiguration : IEntityTypeConfiguration<ResidentEvent>
{
    public void Configure(EntityTypeBuilder<ResidentEvent> builder)
    {
        builder.ToTable("ResidentEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Title).IsRequired().HasMaxLength(300);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.Location).HasMaxLength(300);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(100);

        // Organiser's host unit (Req 41.4). Restrict on delete; deletes are soft deletes.
        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(e => e.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Organiser "my events" read (self-scope) (Req 41.4).
        builder.HasIndex(e => new { e.CommunityId, e.UnitId })
            .HasDatabaseName("IX_ResidentEvent_CommunityId_UnitId");

        // Community-wide upcoming-events browse (Req 41.4, 41.5).
        builder.HasIndex(e => new { e.CommunityId, e.StartsAtUtc })
            .HasDatabaseName("IX_ResidentEvent_CommunityId_StartsAtUtc");
    }
}
