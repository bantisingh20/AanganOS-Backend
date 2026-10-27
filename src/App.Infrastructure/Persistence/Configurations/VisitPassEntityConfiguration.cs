using App.Domain.Community;
using App.Domain.Gate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;


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
        builder.Property(p => p.Email).HasMaxLength(256);
        builder.Property(p => p.VehicleNumber).HasMaxLength(50);
        builder.Property(p => p.VehicleType).HasMaxLength(100);
        builder.Property(p => p.WhomToMeet).HasMaxLength(200);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(p => p.UnitId)
            .OnDelete(DeleteBehavior.Restrict);


        builder.HasIndex(p => new { p.CommunityId, p.QrToken })
            .IsUnique()
            .HasDatabaseName("UX_VisitPass_CommunityId_QrToken");

        // "A unit's passes" for the resident self-view list (Req 23.5).
        builder.HasIndex(p => new { p.CommunityId, p.UnitId })
            .HasDatabaseName("IX_VisitPass_CommunityId_UnitId");
    }
}
