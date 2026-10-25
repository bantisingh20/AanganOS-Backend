using Microsoft.EntityFrameworkCore;
using UrbanNest.Domain.Config;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Configuration"/>: required key/value, and the Unique(Key, Scope, CommunityId)
/// guarantee from Req 8.1. Because <c>CommunityId</c> is nullable and most relational providers
/// treat NULLs as distinct in a unique index (so several platform rows for the same key would
/// slip through), uniqueness is split into two <b>filtered</b> unique indexes:
/// one for platform rows (unique on <c>Key</c> where <c>CommunityId IS NULL</c>) and one for
/// community rows (unique on <c>(Key, CommunityId)</c> where <c>CommunityId IS NOT NULL</c>).
/// Both SQL Server and SQLite honour filtered indexes.
/// </summary>
public sealed class ConfigurationEntityConfiguration : IEntityTypeConfiguration<Configuration>
{
    public void Configure(EntityTypeBuilder<Configuration> builder)
    {
        builder.ToTable("Configurations");

        builder.Property(c => c.Key).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Value).IsRequired().HasMaxLength(4000);
        builder.Property(c => c.Scope).HasConversion<int>();

        // Exactly one platform default per key.
        builder.HasIndex(c => c.Key)
            .IsUnique()
            .HasFilter("[CommunityId] IS NULL")
            .HasDatabaseName("UX_Configuration_Platform_Key");

        // Exactly one community override per (key, community).
        builder.HasIndex(c => new { c.Key, c.CommunityId })
            .IsUnique()
            .HasFilter("[CommunityId] IS NOT NULL")
            .HasDatabaseName("UX_Configuration_Community_Key");

        // Primary lookup path: resolve a key for a community (or platform) fast.
        builder.HasIndex(c => new { c.CommunityId, c.Key })
            .HasDatabaseName("IX_Configuration_CommunityId_Key");
    }
}
