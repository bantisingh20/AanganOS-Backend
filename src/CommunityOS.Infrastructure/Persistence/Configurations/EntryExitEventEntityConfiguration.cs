using CommunityOS.Domain.Gate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunityOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="EntryExitEvent"/> table (Req 25.3, 25.4). As a <c>TenantOwnedEntity</c> (NOT
/// <c>IUnitScoped</c> — a gate operations log, not resident self-view data) it inherits the tenant +
/// soft-delete global filter and the rowversion mapping from <c>AppDbContext.OnModelCreating</c>;
/// the rowversion makes the exit stamp concurrency-safe so a double-exit can't both win (Req 25.3).
///
/// <para><b>Scalable chronological log + open-event matching (Req 25.4).</b> The
/// <c>(CommunityId, EntryTsUtc)</c> index serves both the default newest-first entry/exit list (16.4)
/// and the exit-matching read ("the open event for this authorization") as a single indexed query.
/// The authorization-link indexes make matching the open event by VisitorId/VisitPassId/
/// RecurringVisitorId a direct indexed lookup rather than a scan.</para>
///
/// <para>The authorization-reference FKs are intentionally NOT modeled as navigations — the event is
/// a standalone, self-describing log row (denormalized VisitorName/Category) that must survive if the
/// source authorization is later edited/soft-deleted, so it only stores the id.</para>
/// </summary>
public sealed class EntryExitEventEntityConfiguration : IEntityTypeConfiguration<EntryExitEvent>
{
    public void Configure(EntityTypeBuilder<EntryExitEvent> builder)
    {
        builder.ToTable("EntryExitEvents");
        builder.HasKey(e => e.Id);

        // IsOpen is derived from ExitTsUtc; not a stored column.
        builder.Ignore(e => e.IsOpen);

        builder.Property(e => e.GateLabel).HasMaxLength(200);
        builder.Property(e => e.VisitorName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Category).IsRequired().HasMaxLength(100);
        builder.Property(e => e.VehicleNumber).HasMaxLength(50);

        // Scalable chronological entry/exit log (Req 25.4) + the exit-matching read path (Req 25.3).
        builder.HasIndex(e => new { e.CommunityId, e.EntryTsUtc })
            .HasDatabaseName("IX_EntryExitEvent_CommunityId_EntryTsUtc");

        // Direct indexed lookup of the OPEN event for a given authorization when recording an exit
        // (Req 25.3 — "exit matched to the open event"), so matching is a single indexed query.
        builder.HasIndex(e => new { e.CommunityId, e.VisitorId })
            .HasDatabaseName("IX_EntryExitEvent_CommunityId_VisitorId");
        builder.HasIndex(e => new { e.CommunityId, e.VisitPassId })
            .HasDatabaseName("IX_EntryExitEvent_CommunityId_VisitPassId");
        builder.HasIndex(e => new { e.CommunityId, e.RecurringVisitorId })
            .HasDatabaseName("IX_EntryExitEvent_CommunityId_RecurringVisitorId");
    }
}
