using UrbanNest.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Device"/> (design data-model table: <c>Device | Id, UserId, Platform,
/// OneSignalPlayerId, LastSeen</c>). <see cref="Device.OneSignalPlayerId"/> is Unique so a push
/// target maps to exactly one user — the data-layer guarantee behind notification confidentiality
/// (Req 11.3): a resolved notification can never be routed to another user's device. The
/// <c>(UserId, IsActive)</c> index backs the recipient resolver's lookup of a user's active devices.
/// Device is a platform-scoped entity (belongs to a user, not one community), so the OnModelCreating
/// loop applies the soft-delete filter and concurrency token automatically — no tenant predicate.
/// </summary>
public sealed class DeviceEntityConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("Devices");

        builder.Property(d => d.OneSignalPlayerId).IsRequired().HasMaxLength(128);
        builder.Property(d => d.Platform).HasConversion<int>();

        // One user per push target (confidentiality boundary, Req 11.3).
        builder.HasIndex(d => d.OneSignalPlayerId)
            .IsUnique()
            .HasDatabaseName("UX_Device_OneSignalPlayerId");

        // Recipient resolver path: a user's active devices.
        builder.HasIndex(d => new { d.UserId, d.IsActive })
            .HasDatabaseName("IX_Device_User_Active");
    }
}
