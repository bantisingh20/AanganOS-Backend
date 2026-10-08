using CommunityOS.Domain.Community;
using CommunityOS.Domain.Gate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunityOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="VisitPass"/> table (Req 23.1–23.4). As a <c>TenantOwnedEntity</c> that is
/// also <c>IUnitScoped</c> it inherits the tenant + soft-delete + resident self-scope global filter
/// (Req 5.4, 23.5) and the rowversion mapping from <c>AppDbContext.OnModelCreating</c> — a resident
/// principal only sees passes for their own host unit, and the rowversion makes the use-count
/// increment concurrency-safe (Req 23.3, 23.4).
///
/// <para><b>Indexed opaque-token lookup (Req 23.3).</b> The unique index on
/// <c>(CommunityId, QrToken)</c> is both the uniqueness guarantee for the opaque token within a
/// community and the single indexed read the gate verification rides — scanning a QR hits the index
/// directly, no scan, no N+1, concurrency-safe. The <c>(CommunityId, UnitId)</c> index backs "a
/// unit's passes" for the resident self-view list (Req 23.5).</para>
///
/// <para>The host-unit FK is <c>Restrict</c> on delete; deletes are soft deletes anyway. The host
/// unit's community equals the pass's community (validated same-community in the service).</para>
/// </summary>
public sealed class VisitPassEntityConfiguration : IEntityTypeConfiguration<VisitPass>
{
    public void Configure(EntityTypeBuilder<VisitPass> builder)
    {
        builder.ToTable("VisitPasses");
        builder.HasKey(p => p.Id);

        // HostUnitId is a get/set alias over UnitId (the IUnitScoped backing column) — ignore it so
        // EF maps the single UnitId column, not a duplicate.
        builder.Ignore(p => p.HostUnitId);

        builder.Property(p => p.VisitorName).IsRequired().HasMaxLength(200);
        builder.Property(p => p.QrToken).IsRequired().HasMaxLength(128);
        builder.Property(p => p.OtpHash).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Status).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Phone).HasMaxLength(50);
        builder.Property(p => p.VehicleNumber).HasMaxLength(50);
        builder.Property(p => p.VehicleType).HasMaxLength(100);
        builder.Property(p => p.WhomToMeet).HasMaxLength(200);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(p => p.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Opaque-token uniqueness within a community + the gate verification lookup path (Req 23.1,
        // 23.3): a QR scan resolves to exactly one pass via this index.
        builder.HasIndex(p => new { p.CommunityId, p.QrToken })
            .IsUnique()
            .HasDatabaseName("UX_VisitPass_CommunityId_QrToken");

        // "A unit's passes" for the resident self-view list (Req 23.5).
        builder.HasIndex(p => new { p.CommunityId, p.UnitId })
            .HasDatabaseName("IX_VisitPass_CommunityId_UnitId");
    }
}
