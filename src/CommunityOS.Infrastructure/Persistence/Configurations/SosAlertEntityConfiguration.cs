using CommunityOS.Domain.Gate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunityOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="SosAlert"/> table (Req 27.3, 27.4). As a <c>TenantOwnedEntity</c> (NOT
/// <c>IUnitScoped</c> — a community-wide emergency event handled by security staff; its
/// <c>UnitId</c> is a plain reference, not the <c>IUnitScoped</c> contract) it inherits the tenant +
/// soft-delete global filter and the rowversion mapping from <c>AppDbContext.OnModelCreating</c>;
/// the rowversion makes the acknowledge/resolve transition record exactly once (Req 27.4).
///
/// <para>The <c>(CommunityId, Status)</c> index backs the active-SOS queue (Req 27.4) + the
/// status-filtered list (Task 16.4); <c>(CommunityId, RaisedAtUtc)</c> backs the chronological list.
/// <c>IsActive</c> is derived from Status and is not mapped.</para>
/// </summary>
public sealed class SosAlertEntityConfiguration : IEntityTypeConfiguration<SosAlert>
{
    public void Configure(EntityTypeBuilder<SosAlert> builder)
    {
        builder.ToTable("SosAlerts");
        builder.HasKey(a => a.Id);

        builder.Ignore(a => a.IsActive);

        builder.Property(a => a.Location).HasMaxLength(200);
        builder.Property(a => a.Status).IsRequired().HasMaxLength(50);

        builder.HasIndex(a => new { a.CommunityId, a.Status })
            .HasDatabaseName("IX_SosAlert_CommunityId_Status");
        builder.HasIndex(a => new { a.CommunityId, a.RaisedAtUtc })
            .HasDatabaseName("IX_SosAlert_CommunityId_RaisedAtUtc");
    }
}
