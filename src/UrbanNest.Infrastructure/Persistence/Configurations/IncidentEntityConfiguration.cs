using UrbanNest.Domain.Gate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="Incident"/> table (Req 27.1, 27.2). As a <c>TenantOwnedEntity</c> (NOT
/// <c>IUnitScoped</c> — a community-wide security record) it inherits the tenant + soft-delete global
/// filter and the rowversion mapping from <c>AppDbContext.OnModelCreating</c>; the rowversion makes
/// the acknowledge/resolve transitions record exactly once under concurrency (Req 27.1).
///
/// <para>The <c>(CommunityId, Status)</c> index backs the open-incidents queue + the status-filtered
/// list (Task 16.4); <c>(CommunityId, CreatedAtUtc)</c> backs the chronological list. <c>IsResolved</c>
/// / <c>IsEscalated</c> are derived from Status/EscalationState and are not mapped.</para>
/// </summary>
public sealed class IncidentEntityConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("Incidents");
        builder.HasKey(i => i.Id);

        builder.Ignore(i => i.IsResolved);
        builder.Ignore(i => i.IsEscalated);

        builder.Property(i => i.Location).HasMaxLength(200);
        builder.Property(i => i.Category).IsRequired().HasMaxLength(100);
        builder.Property(i => i.Severity).IsRequired().HasMaxLength(50);
        builder.Property(i => i.Description).IsRequired().HasMaxLength(2000);
        builder.Property(i => i.Status).IsRequired().HasMaxLength(50);
        builder.Property(i => i.EscalationState).IsRequired().HasMaxLength(50);

        builder.HasIndex(i => new { i.CommunityId, i.Status })
            .HasDatabaseName("IX_Incident_CommunityId_Status");
        builder.HasIndex(i => new { i.CommunityId, i.CreatedAtUtc })
            .HasDatabaseName("IX_Incident_CommunityId_CreatedAtUtc");
    }
}
