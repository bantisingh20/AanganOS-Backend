using UrbanNest.Domain.Community;
using UrbanNest.Domain.Gate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="RecurringVisitor"/> table (Req 25.2). As a <c>TenantOwnedEntity</c> that is
/// also <c>IUnitScoped</c> it inherits the tenant + soft-delete + resident self-scope global filter
/// (Req 5.4) and the rowversion mapping from <c>AppDbContext.OnModelCreating</c> — a resident only
/// sees recurring visitors for their own host unit (the ones they created / may revoke).
///
/// <para>The <c>(CommunityId, UnitId, Status)</c> index backs the host unit's active recurring-visitor
/// list and the entry-workflow lookup as a single indexed read (no N+1). The host-unit FK is
/// <c>Restrict</c> on delete; deletes are soft deletes anyway. <c>HostUnitId</c>/<c>IsActive</c>/
/// <c>IsAuthorizedOn</c> are derived members and are not mapped.</para>
/// </summary>
public sealed class RecurringVisitorEntityConfiguration : IEntityTypeConfiguration<RecurringVisitor>
{
    public void Configure(EntityTypeBuilder<RecurringVisitor> builder)
    {
        builder.ToTable("RecurringVisitors");
        builder.HasKey(r => r.Id);

        // HostUnitId is a get/set alias over UnitId; IsActive is derived from Status. Neither is a
        // stored column (IsAuthorizedOn is a method, so EF ignores it automatically).
        builder.Ignore(r => r.HostUnitId);
        builder.Ignore(r => r.IsActive);

        builder.Property(r => r.Name).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Phone).HasMaxLength(50);
        builder.Property(r => r.Category).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(50);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(r => r.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // The host unit's recurring-visitor list + the entry-workflow authorization lookup (Req 25.2).
        builder.HasIndex(r => new { r.CommunityId, r.UnitId, r.Status })
            .HasDatabaseName("IX_RecurringVisitor_CommunityId_UnitId_Status");
    }
}
