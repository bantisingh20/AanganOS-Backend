using CommunityOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunityOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="RolePermission"/> as a composite-key join table: PK(RoleId, PermissionId)
/// (design Identity table). Cascade-deletes with the owning role so re-defining a role's permission
/// set is a clean replace; the permission catalog side restricts (a permission can't be deleted out
/// from under a role that still grants it).
/// </summary>
public sealed class RolePermissionEntityConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions");

        builder.HasKey(rp => new { rp.RoleId, rp.PermissionId });

        builder.HasOne(rp => rp.Role)
            .WithMany(r => r.RolePermissions)
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(rp => rp.Permission)
            .WithMany()
            .HasForeignKey(rp => rp.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Reverse lookup used by the effective-permissions query (permission → roles granting it)
        // and by role-definition reads.
        builder.HasIndex(rp => rp.PermissionId)
            .HasDatabaseName("IX_RolePermission_PermissionId");
    }
}
