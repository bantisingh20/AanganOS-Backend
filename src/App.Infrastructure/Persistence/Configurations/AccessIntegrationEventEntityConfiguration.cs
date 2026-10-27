using App.Domain.Gate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="AccessIntegrationEvent"/> table (Req 27.5). As a <c>TenantOwnedEntity</c>
/// (NOT <c>IUnitScoped</c> — a gate operations record) it inherits the tenant + soft-delete global
/// filter and the rowversion mapping from <c>AppDbContext.OnModelCreating</c>.
///
/// <para>An append-only record of each triggered CCTV / boom-barrier / access-control hook; the real
/// hardware drivers are Phase 12 (Task 40). The <c>(CommunityId, CreatedAtUtc)</c> index backs the
/// chronological integration-event log as a single indexed read (Task 16.4).</para>
/// </summary>
public sealed class AccessIntegrationEventEntityConfiguration : IEntityTypeConfiguration<AccessIntegrationEvent>
{
    public void Configure(EntityTypeBuilder<AccessIntegrationEvent> builder)
    {
        builder.ToTable("AccessIntegrationEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.GateLabel).HasMaxLength(200);
        builder.Property(e => e.Action).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Detail).HasMaxLength(1000);

        builder.HasIndex(e => new { e.CommunityId, e.CreatedAtUtc })
            .HasDatabaseName("IX_AccessIntegrationEvent_CommunityId_CreatedAtUtc");
    }
}
