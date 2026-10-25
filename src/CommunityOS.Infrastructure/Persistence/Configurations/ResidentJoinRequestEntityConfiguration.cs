using CommunityOS.Domain.Community;
using CommunityOS.Domain.Residents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunityOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="ResidentJoinRequest"/> table (resident self-onboarding). A
/// <c>TenantOwnedEntity</c>, so it inherits the tenant + soft-delete global filter and rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>. NOT <c>IUnitScoped</c> — a join request is read
/// by community ADMINS (not residents), so it's community-scoped only.
///
/// <para>The <c>(CommunityId, Status, CreatedAtUtc)</c> index backs the admin "pending requests"
/// list as a single indexed read. The <c>(CommunityId, UnitId)</c> index backs the one-active-
/// request-per-unit check at submit. The unit FK is <c>Restrict</c> on delete.</para>
/// </summary>
public sealed class ResidentJoinRequestEntityConfiguration : IEntityTypeConfiguration<ResidentJoinRequest>
{
    public void Configure(EntityTypeBuilder<ResidentJoinRequest> builder)
    {
        builder.ToTable("ResidentJoinRequests");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).IsRequired().HasMaxLength(256);
        builder.Property(r => r.Phone).IsRequired().HasMaxLength(32);
        builder.Property(r => r.PinHash).IsRequired().HasMaxLength(512);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(32);
        builder.Property(r => r.DecidedBy).HasMaxLength(200);
        builder.Property(r => r.RejectReason).HasMaxLength(500);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(r => r.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.CommunityId, r.Status, r.CreatedAtUtc })
            .HasDatabaseName("IX_ResidentJoinRequest_CommunityId_Status_CreatedAtUtc");

        builder.HasIndex(r => new { r.CommunityId, r.UnitId })
            .HasDatabaseName("IX_ResidentJoinRequest_CommunityId_UnitId");
    }
}
