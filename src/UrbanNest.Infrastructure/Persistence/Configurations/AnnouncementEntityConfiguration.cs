using UrbanNest.Domain.Community;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="Announcement"/> table (MVP notices slice) — the community notice board. As a
/// <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped (Req 5.2) and a
/// soft-deleted notice stays traceable but is hidden from ordinary reads — the same shape as
/// <see cref="AmenityEntityConfiguration"/>. It is deliberately NOT <c>IUnitScoped</c>: a notice is
/// community-wide, not pinned to one unit.
///
/// <para>The <c>(CommunityId, PublishedAtUtc)</c> index backs the board's newest-first list as a
/// single indexed read (no N+1).</para>
/// </summary>
public sealed class AnnouncementEntityConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.ToTable("Announcements");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Title).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Body).IsRequired().HasMaxLength(4000);
        builder.Property(a => a.Category).IsRequired().HasMaxLength(50);
        // Reversible board-visibility flag (admin activate/deactivate). Defaults to true.
        builder.Property(a => a.IsActive).HasDefaultValue(true);

        // The board's newest-first list as a single indexed read, no N+1.
        builder.HasIndex(a => new { a.CommunityId, a.PublishedAtUtc })
            .HasDatabaseName("IX_Announcement_CommunityId_PublishedAtUtc");
    }
}
