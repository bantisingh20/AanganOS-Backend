using App.Domain.Community;
using App.Domain.Gate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;


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
        builder.Property(v => v.Email).HasMaxLength(256);
        builder.Property(v => v.QrToken).HasMaxLength(128);
        builder.Property(v => v.OtpHash).HasMaxLength(256);
        builder.HasIndex(v => new { v.CommunityId, v.QrToken })
            .IsUnique()
            .HasFilter("[QrToken] IS NOT NULL")
            .HasDatabaseName("UX_Visitor_CommunityId_QrToken");
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
