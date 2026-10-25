using UrbanNest.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="UserRole"/>: Unique(UserId, RoleId, CommunityId) (Req 3.3). Because
/// <c>CommunityId</c> is nullable, uniqueness is split into two <b>filtered</b> unique indexes —
/// one for platform-scoped assignments (unique on <c>(UserId, RoleId)</c> where
/// <c>CommunityId IS NULL</c>) and one for community-scoped assignments (unique on
/// <c>(UserId, RoleId, CommunityId)</c> where <c>CommunityId IS NOT NULL</c>) — so a duplicate
/// assignment is rejected at the DB level and surfaces as a conflict (concurrency-safe, Req 3.3).
///
/// <para>The <c>(UserId)</c> index serves the effective-permissions query (Task 5.3) which starts
/// from the acting user and joins through their assignments.</para>
/// </summary>
public sealed class UserRoleEntityConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles");

        builder.HasOne(ur => ur.User)
            .WithMany()
            .HasForeignKey(ur => ur.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ur => ur.Role)
            .WithMany()
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        // One platform assignment per (user, role).
        builder.HasIndex(ur => new { ur.UserId, ur.RoleId })
            .IsUnique()
            .HasFilter("[CommunityId] IS NULL")
            .HasDatabaseName("UX_UserRole_Platform_User_Role");

        // One community assignment per (user, role, community).
        builder.HasIndex(ur => new { ur.UserId, ur.RoleId, ur.CommunityId })
            .IsUnique()
            .HasFilter("[CommunityId] IS NOT NULL")
            .HasDatabaseName("UX_UserRole_Community_User_Role");

        // Primary traversal: a user's assignments (effective-permissions query, claims issuance).
        builder.HasIndex(ur => ur.UserId)
            .HasDatabaseName("IX_UserRole_UserId");
    }
}
