using UrbanNest.Domain.Amenities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="ResidentEventRegistration"/> table (Req 41.5, Task 24.2) — one resident's seat
/// on a <see cref="ResidentEvent"/>. As a <c>TenantOwnedEntity</c> AND <c>IUnitScoped</c>, the
/// <c>AppDbContext.OnModelCreating</c> loop wires the tenant + soft-delete global filter AND the
/// resident self-scope filter onto it (keyed on <c>UnitId</c> = the registering resident's unit), so
/// a resident principal sees only their OWN registrations; the rowversion guards the cancel (so a seat
/// release records exactly once).
///
/// <para><b>One registration per resident per event.</b> The <c>Unique(ResidentEventId, ResidentId)</c>
/// index makes "a resident registers for an event at most once" a DB guarantee — a concurrent
/// duplicate registration → CONFLICT via <c>UniqueConstraintGuard</c>, no read-then-write existence
/// check. The at-capacity gate lives on the event's guarded <c>RegisteredCount</c> counter; this row
/// records the seat holder so a cancellation releases exactly one. The index is also the single
/// indexed read the registration flow uses to detect an existing registration.</para>
/// </summary>
public sealed class ResidentEventRegistrationEntityConfiguration : IEntityTypeConfiguration<ResidentEventRegistration>
{
    public void Configure(EntityTypeBuilder<ResidentEventRegistration> builder)
    {
        builder.ToTable("ResidentEventRegistrations");
        builder.HasKey(r => r.Id);

        // Link to the event (Req 41.5). Restrict on delete; deletes are soft deletes.
        builder.HasOne<ResidentEvent>()
            .WithMany()
            .HasForeignKey(r => r.ResidentEventId)
            .OnDelete(DeleteBehavior.Restrict);

        // One registration per resident per event (Req 41.5): the DB-level source of truth so a
        // concurrent duplicate registration maps to CONFLICT rather than a read-then-write check.
        builder.HasIndex(r => new { r.ResidentEventId, r.ResidentId })
            .IsUnique()
            .HasDatabaseName("UX_ResidentEventRegistration_EventId_ResidentId");
    }
}
