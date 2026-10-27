using App.Domain.Residents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="EmergencyContact"/> one-to-many child of a <see cref="Resident"/> (Req 15.3).
/// As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the
/// rowversion mapping from <c>AppDbContext.OnModelCreating</c>. The <c>(CommunityId, ResidentId)</c>
/// index backs "a resident's emergency contacts" as a single indexed read.
/// </summary>
public sealed class EmergencyContactEntityConfiguration : IEntityTypeConfiguration<EmergencyContact>
{
    public void Configure(EntityTypeBuilder<EmergencyContact> builder)
    {
        builder.ToTable("EmergencyContacts");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Phone).IsRequired().HasMaxLength(50);
        builder.Property(c => c.Relationship).IsRequired().HasMaxLength(100);

        // Resident → emergency contacts (one-to-many). Restrict: contacts stay traceable with the
        // resident; soft delete handles archival.
        builder.HasOne(c => c.Resident)
            .WithMany(r => r.EmergencyContacts)
            .HasForeignKey(c => c.ResidentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.CommunityId, c.ResidentId })
            .HasDatabaseName("IX_EmergencyContact_CommunityId_ResidentId");
    }
}
