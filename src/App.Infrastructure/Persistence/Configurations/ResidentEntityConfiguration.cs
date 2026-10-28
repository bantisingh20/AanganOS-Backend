using App.Domain.Residents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;


public sealed class ResidentEntityConfiguration : IEntityTypeConfiguration<Resident>
{
    public void Configure(EntityTypeBuilder<Resident> builder)
    {
        builder.ToTable("Residents");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Email).HasMaxLength(256);
        builder.Property(r => r.Phone).HasMaxLength(50);
        builder.Property(r => r.ResidentType).IsRequired().HasMaxLength(100);
        builder.Property(r => r.VerificationStatus).IsRequired().HasMaxLength(50);
        builder.Property(r => r.RejectionReason).HasMaxLength(1000);
        // Optional photo reference (File_Service id). Nullable; 200 matches the File_Service
        // reference length used elsewhere.
        builder.Property(r => r.PhotoFileId).HasMaxLength(200);
        builder.Property(r => r.Gender).HasMaxLength(16);
        builder.Property(r => r.GovtIdType).HasMaxLength(32);
        builder.Property(r => r.GovtIdFrontFileId).HasMaxLength(200);
        builder.Property(r => r.GovtIdBackFileId).HasMaxLength(200);

        // Verification-queue list (Req 15.4): residents in a community by verification status.
        builder.HasIndex(r => new { r.CommunityId, r.VerificationStatus })
            .HasDatabaseName("IX_Resident_CommunityId_VerificationStatus");
    }
}
