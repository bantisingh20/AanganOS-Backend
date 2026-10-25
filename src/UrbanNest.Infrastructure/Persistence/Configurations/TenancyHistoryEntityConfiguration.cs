using UrbanNest.Domain.Community;
using UrbanNest.Domain.Residents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the append-only <see cref="TenancyHistory"/> register (Req 16.2, 16.3). As a
/// <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>, so history reads are tenant-scoped (Req 5.2)
/// and ending a tenancy (EndDate stamp) is concurrency-checked.
///
/// <para><b>Chronological-read index (Req 16.4).</b> <c>(CommunityId, UnitId, StartDate)</c> backs
/// "a unit's tenancy history in chronological order" as a single ordered indexed read (no N+1), and
/// the "find this resident's open tenancy for this unit" lookup the end-tenancy path runs.</para>
///
/// <para>No at-most-one-open constraint: a unit may have multiple concurrent open tenancies (co-
/// tenants), so ending a tenancy targets a specific resident's open row rather than closing all.
/// Prior entries are retained, never physically removed (Req 16.3). Relationships are <c>Restrict</c>
/// on delete; no inverse navigation is declared on <see cref="Resident"/>.</para>
/// </summary>
public sealed class TenancyHistoryEntityConfiguration : IEntityTypeConfiguration<TenancyHistory>
{
    public void Configure(EntityTypeBuilder<TenancyHistory> builder)
    {
        builder.ToTable("TenancyHistories");
        builder.HasKey(t => t.Id);

        // Tenant (resident) → tenancy rows. Restrict: keep history traceable; soft delete archives.
        builder.HasOne(t => t.Resident)
            .WithMany()
            .HasForeignKey(t => t.ResidentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Link to the unit (Req 16.2). No navigation on Unit's side; restrict on delete.
        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(t => t.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chronological read + open-row lookup per unit (Req 16.4).
        builder.HasIndex(t => new { t.CommunityId, t.UnitId, t.StartDate })
            .HasDatabaseName("IX_TenancyHistory_CommunityId_UnitId_StartDate");
    }
}
