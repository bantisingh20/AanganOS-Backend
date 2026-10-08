using CommunityOS.Domain.Community;
using CommunityOS.Domain.Residents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunityOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the append-only <see cref="OwnershipHistory"/> register (Req 16.1, 16.3). As a
/// <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>, so history reads are tenant-scoped (Req 5.2)
/// and a close (EndDate stamp) is concurrency-checked.
///
/// <para><b>Chronological-read index (Req 16.4).</b> <c>(CommunityId, UnitId, StartDate)</c> backs
/// "a unit's ownership history in chronological order" as a single ordered indexed read (no N+1),
/// and also the "find the open row for this unit" lookup the transfer path runs.</para>
///
/// <para><b>At-most-one-open-owner (Req 16.3 invariant).</b> The service closes the prior open row
/// and inserts the new owner in one transaction so a unit never has two open owners. On SQL Server
/// a filtered unique index on <c>(UnitId) WHERE EndDate IS NULL</c> is added in
/// <c>AppDbContext.OnModelCreating</c> (where the provider is known) as a hard DB guarantee; the
/// SQLite test path relies on the transactional close+insert. ponytail: the SQLite fixture has no
/// filtered-index equivalent wired here, so under that provider the invariant rests on the
/// single-transaction close+insert rather than a DB constraint — acceptable for the test path;
/// production (SQL Server) gets the index.</para>
///
/// <para>Relationships are <c>Restrict</c> on delete so a resident or unit cannot be hard-deleted out
/// from under its history; history rows are retained, never physically removed (Req 16.3). No inverse
/// navigation is declared on <see cref="Resident"/> to keep that entity untouched.</para>
/// </summary>
public sealed class OwnershipHistoryEntityConfiguration : IEntityTypeConfiguration<OwnershipHistory>
{
    public void Configure(EntityTypeBuilder<OwnershipHistory> builder)
    {
        builder.ToTable("OwnershipHistories");
        builder.HasKey(o => o.Id);

        // Owner (resident) → ownership rows. Restrict: keep history traceable; soft delete archives.
        builder.HasOne(o => o.Resident)
            .WithMany()
            .HasForeignKey(o => o.ResidentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Link to the unit (Req 16.1). No navigation on Unit's side; restrict on delete.
        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(o => o.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chronological read + open-row lookup per unit (Req 16.4).
        builder.HasIndex(o => new { o.CommunityId, o.UnitId, o.StartDate })
            .HasDatabaseName("IX_OwnershipHistory_CommunityId_UnitId_StartDate");
    }
}
