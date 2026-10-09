using CommunityOS.Domain.Residents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunityOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="Resident"/> table (Req 15.1, 15.5). As a <c>TenantOwnedEntity</c> it inherits
/// the global tenant + soft-delete query filter and the rowversion mapping from
/// <c>AppDbContext.OnModelCreating</c>, so resident reads are tenant-scoped (Req 5.2), optimistic
/// concurrency on edit is enforced (the token the Task 12.3 approve-once workflow relies on) and
/// delete is a soft delete that stays traceable (Req 15.5).
///
/// <para><b>Verification-queue index (Req 15.4).</b> <c>(CommunityId, VerificationStatus)</c> backs
/// the "pending residents in this community" verification-queue list the Task 12.7 endpoint runs, so
/// it hits an index at scale rather than scanning.</para>
///
/// <para>The <c>EmergencyContacts</c> and <c>HouseholdMembers</c> collections are mapped from the
/// child side (see their configurations), so no relationship is declared here.</para>
/// </summary>
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

        // Verification-queue list (Req 15.4): residents in a community by verification status.
        builder.HasIndex(r => new { r.CommunityId, r.VerificationStatus })
            .HasDatabaseName("IX_Resident_CommunityId_VerificationStatus");
    }
}
