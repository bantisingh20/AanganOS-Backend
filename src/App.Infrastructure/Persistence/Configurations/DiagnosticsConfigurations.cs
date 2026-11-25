using App.Domain.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

public sealed class ErrorLogEntityConfiguration : IEntityTypeConfiguration<ErrorLog>
{
    public void Configure(EntityTypeBuilder<ErrorLog> builder)
    {
        builder.ToTable("ErrorLogs");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.CorrelationId).HasMaxLength(128);
        builder.Property(e => e.ApiPath).IsRequired().HasMaxLength(512);
        builder.Property(e => e.HttpMethod).IsRequired().HasMaxLength(16);
        builder.Property(e => e.ExceptionType).IsRequired().HasMaxLength(256);
        builder.Property(e => e.ErrorMessage).IsRequired();
        builder.Property(e => e.FileName).HasMaxLength(512);
        builder.Property(e => e.ControllerOrFunction).HasMaxLength(256);
        builder.Property(e => e.UserName).HasMaxLength(256);
        builder.Property(e => e.UserRole).HasMaxLength(128);
        builder.Property(e => e.ClientIp).HasMaxLength(64);
        builder.Property(e => e.UserAgent).HasMaxLength(512);

        // Indexes for high-performance log querying and analytics
        builder.HasIndex(e => e.CreatedAtUtc)
            .HasDatabaseName("IX_ErrorLogs_CreatedAtUtc");

        builder.HasIndex(e => new { e.StatusCode, e.CreatedAtUtc })
            .HasDatabaseName("IX_ErrorLogs_StatusCode_CreatedAtUtc");

        builder.HasIndex(e => new { e.UserId, e.CreatedAtUtc })
            .HasDatabaseName("IX_ErrorLogs_UserId_CreatedAtUtc");

        builder.HasIndex(e => new { e.OrganizationId, e.CreatedAtUtc })
            .HasDatabaseName("IX_ErrorLogs_OrganizationId_CreatedAtUtc");

        builder.HasIndex(e => new { e.CommunityId, e.CreatedAtUtc })
            .HasDatabaseName("IX_ErrorLogs_CommunityId_CreatedAtUtc");
    }
}
