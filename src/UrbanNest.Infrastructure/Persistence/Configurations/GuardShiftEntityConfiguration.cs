using UrbanNest.Domain.Gate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="GuardShift"/> table (Req 26.4). As a <c>TenantOwnedEntity</c> (NOT
/// <c>IUnitScoped</c> — a community-wide roster) it inherits the tenant + soft-delete global filter
/// and the rowversion mapping from <c>AppDbContext.OnModelCreating</c>.
///
/// <para>The <c>(CommunityId, ShiftStartUtc)</c> index backs the chronological roster list (Task
/// 16.4) as a single indexed read; <c>(CommunityId, GuardUserId)</c> backs "a guard's shifts". The
/// shift references the guard <c>User</c> by id only (a Phase 1 Guard-role user) — not a duplicated
/// profile and not a hard FK navigation, since <c>User</c> is a platform-scoped entity in another
/// module (Req 26.2).</para>
/// </summary>
public sealed class GuardShiftEntityConfiguration : IEntityTypeConfiguration<GuardShift>
{
    public void Configure(EntityTypeBuilder<GuardShift> builder)
    {
        builder.ToTable("GuardShifts");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.GateLabel).HasMaxLength(200);
        builder.Property(s => s.Status).IsRequired().HasMaxLength(50);

        // Chronological roster list (Task 16.4) + "a guard's shifts" read, both single indexed reads.
        builder.HasIndex(s => new { s.CommunityId, s.ShiftStartUtc })
            .HasDatabaseName("IX_GuardShift_CommunityId_ShiftStartUtc");
        builder.HasIndex(s => new { s.CommunityId, s.GuardUserId })
            .HasDatabaseName("IX_GuardShift_CommunityId_GuardUserId");
    }
}
