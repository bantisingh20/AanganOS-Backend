using CommunityOS.Domain.Community;
using CommunityOS.Domain.Gate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunityOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="Visitor"/> table (Req 24.1–24.5). As a <c>TenantOwnedEntity</c> that is also
/// <c>IUnitScoped</c> it inherits the tenant + soft-delete + resident self-scope global filter
/// (Req 5.4) and the rowversion mapping from <c>AppDbContext.OnModelCreating</c> — a resident only
/// sees walk-in requests for their own host unit, and the rowversion makes the approve/reject
/// decision record exactly once under concurrency (Req 24.3).
///
/// <para>The <c>(CommunityId, UnitId, Status)</c> index backs the host unit's pending-walk-in queue
/// (Req 24.5) and the resident self-view list as a single indexed read (no N+1). The host-unit FK
/// is <c>Restrict</c> on delete; deletes are soft deletes anyway. <c>IsAdmittable</c> is a computed
/// property (derived from Status) and is not mapped.</para>
/// </summary>
public sealed class VisitorEntityConfiguration : IEntityTypeConfiguration<Visitor>
{
    public void Configure(EntityTypeBuilder<Visitor> builder)
    {
        builder.ToTable("Visitors");
        builder.HasKey(v => v.Id);

        // HostUnitId is a get/set alias over UnitId; IsAdmittable is derived from Status. Neither is
        // a stored column.
        builder.Ignore(v => v.HostUnitId);
        builder.Ignore(v => v.IsAdmittable);

        builder.Property(v => v.Name).IsRequired().HasMaxLength(200);
        builder.Property(v => v.Phone).HasMaxLength(50);
        builder.Property(v => v.Category).IsRequired().HasMaxLength(100);
        builder.Property(v => v.VehicleNumber).HasMaxLength(50);
        builder.Property(v => v.GateLocation).HasMaxLength(200);
        builder.Property(v => v.IdProofType).HasMaxLength(100);
        builder.Property(v => v.IdProofNumber).HasMaxLength(100);
        builder.Property(v => v.Purpose).HasMaxLength(500);
        builder.Property(v => v.WhomToMeet).HasMaxLength(200);
        builder.Property(v => v.Status).IsRequired().HasMaxLength(50);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(v => v.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // The host unit's pending-walk-in approval queue + the resident self-view list (Req 24.5).
        builder.HasIndex(v => new { v.CommunityId, v.UnitId, v.Status })
            .HasDatabaseName("IX_Visitor_CommunityId_UnitId_Status");
    }
}
