using UrbanNest.Domain.Community;
using UrbanNest.Domain.Residents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="Vehicle"/> table (Req 19.1, 19.2). As a <c>TenantOwnedEntity</c> that is also
/// <c>IUnitScoped</c> it inherits the tenant + soft-delete + resident self-scope global filter
/// (Req 5.4, 19.5) and the rowversion mapping from <c>AppDbContext.OnModelCreating</c> — a resident
/// principal only sees their own unit's vehicles.
///
/// <para><b>Concurrency-safe uniqueness (Req 19.2).</b> The unique index on
/// <c>(CommunityId, RegistrationNumber)</c> is the single source of truth for registration-number
/// uniqueness within a community: two concurrent registrations race to the index and exactly one
/// wins; the service lets the index reject the loser and maps the violation to CONFLICT
/// (<c>UniqueConstraintGuard</c>) rather than a read-then-write pre-check — the same approach as
/// <c>Unit(CommunityId, UnitNumber)</c>.</para>
///
/// <para>The <c>(CommunityId, UnitId)</c> index backs "a unit's vehicles" as a single indexed read
/// (no N+1, Req 19.5). The unit FK is <c>Restrict</c> on delete; deletes are soft deletes anyway.</para>
/// </summary>
public sealed class VehicleEntityConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("Vehicles");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.RegistrationNumber).IsRequired().HasMaxLength(50);
        builder.Property(v => v.VehicleType).IsRequired().HasMaxLength(100);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(v => v.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // DB-enforced registration uniqueness within a community (Req 19.2): the concurrency-safe
        // source of truth, not an application read-then-write.
        builder.HasIndex(v => new { v.CommunityId, v.RegistrationNumber })
            .IsUnique()
            .HasDatabaseName("UX_Vehicle_CommunityId_RegistrationNumber");

        builder.HasIndex(v => new { v.CommunityId, v.UnitId })
            .HasDatabaseName("IX_Vehicle_CommunityId_UnitId");
    }
}
