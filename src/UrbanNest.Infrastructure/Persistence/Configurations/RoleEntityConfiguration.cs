using UrbanNest.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;


public sealed class RoleEntityConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");

        builder.Property(r => r.Name).IsRequired().HasMaxLength(128);
        builder.Property(r => r.Scope).HasConversion<int>();
        builder.Property(r => r.Surface).HasConversion<int>();

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
