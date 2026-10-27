using App.Domain.Residents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="CommunicationPreference"/> per-resident, per-category opt-in row (Req 20.1,
/// Task 12.6). As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and
/// the rowversion mapping from <c>AppDbContext.OnModelCreating</c>.
///
/// <para><b>One row per (resident, category).</b> The <c>Unique(ResidentId, Category)</c> index is
/// the DB-level source of truth that each category has exactly one preference per resident (Req
/// 20.1); it also backs the Notification_Service's preference check as a single indexed lookup
/// (<c>ResidentId == x AND Category == y</c>), so there is no N+1 scan at scale.</para>
/// </summary>
public sealed class CommunicationPreferenceEntityConfiguration : IEntityTypeConfiguration<CommunicationPreference>
{
    public void Configure(EntityTypeBuilder<CommunicationPreference> builder)
    {
        builder.ToTable("CommunicationPreferences");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Category).IsRequired().HasMaxLength(100);

        // Resident → communication preferences (one-to-many). Restrict: keep the preference traceable
        // with the resident; soft delete handles archival.
        builder.HasOne(p => p.Resident)
            .WithMany()
            .HasForeignKey(p => p.ResidentId)
            .OnDelete(DeleteBehavior.Restrict);

        // One preference row per (resident, category) — the invariant for Req 20.1 and the indexed
        // lookup path for the Notification_Service preference check.
        builder.HasIndex(p => new { p.ResidentId, p.Category })
            .IsUnique()
            .HasDatabaseName("UX_CommunicationPreference_ResidentId_Category");
    }
}
