using UrbanNest.Domain.Gate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="WatchlistEntry"/> table (Req 26.1, 26.5). As a <c>TenantOwnedEntity</c> (NOT
/// <c>IUnitScoped</c> — a community-wide security register, not resident self-view data) it inherits
/// the tenant + soft-delete global filter and the rowversion mapping from
/// <c>AppDbContext.OnModelCreating</c>; the rowversion makes the clear transition record exactly once.
///
/// <para><b>Efficient gate match (Req 26.5).</b> The <c>(CommunityId, SubjectIdentifier)</c> index is
/// the single indexed equality lookup the gate flow runs on every presented subject — no scan, no
/// N+1. The service stores and matches the identifier normalized (trim + upper-invariant) so casing/
/// spacing can't slip a match. <c>IsActive</c> is derived from Status and is not mapped.</para>
/// </summary>
public sealed class WatchlistEntryEntityConfiguration : IEntityTypeConfiguration<WatchlistEntry>
{
    public void Configure(EntityTypeBuilder<WatchlistEntry> builder)
    {
        builder.ToTable("WatchlistEntries");
        builder.HasKey(e => e.Id);

        builder.Ignore(e => e.IsActive);

        builder.Property(e => e.SubjectType).IsRequired().HasMaxLength(50);
        builder.Property(e => e.SubjectIdentifier).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Reason).IsRequired().HasMaxLength(1000);
        builder.Property(e => e.Severity).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(50);

        // The gate-match lookup path (Req 26.5): a presented subject resolves via this index.
        builder.HasIndex(e => new { e.CommunityId, e.SubjectIdentifier })
            .HasDatabaseName("IX_WatchlistEntry_CommunityId_SubjectIdentifier");
    }
}
