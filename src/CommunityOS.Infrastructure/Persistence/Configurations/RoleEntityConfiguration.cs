using CommunityOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunityOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Role"/>: Unique(Name, Scope, CommunityId) (Req 3.6). Because <c>CommunityId</c>
/// is nullable, uniqueness is split into two <b>filtered</b> unique indexes — one for platform
/// roles (unique on <c>(Name, Scope)</c> where <c>CommunityId IS NULL</c>) and one for community
/// roles (unique on <c>(Name, Scope, CommunityId)</c> where <c>CommunityId IS NOT NULL</c>) — so a
/// duplicate role name within a scope is rejected at the DB level, concurrency-safe, and surfaces
/// as a conflict rather than being pre-checked with a read-then-write (same technique as Config).
/// </summary>
public sealed class RoleEntityConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");

        builder.Property(r => r.Name).IsRequired().HasMaxLength(128);
        builder.Property(r => r.Scope).HasConversion<int>();

        // One platform role per (name, scope).
        builder.HasIndex(r => new { r.Name, r.Scope })
            .IsUnique()
            .HasFilter("[CommunityId] IS NULL")
            .HasDatabaseName("UX_Role_Platform_Name_Scope");

        // One community role per (name, scope, community).
        builder.HasIndex(r => new { r.Name, r.Scope, r.CommunityId })
            .IsUnique()
            .HasFilter("[CommunityId] IS NOT NULL")
            .HasDatabaseName("UX_Role_Community_Name_Scope");
    }
}
