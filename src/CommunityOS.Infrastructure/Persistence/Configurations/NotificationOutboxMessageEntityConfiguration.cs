using CommunityOS.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunityOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="NotificationOutboxMessage"/> — the transactional-outbox table (Req 11.5). It is
/// a <c>TenantOwnedEntity</c>, so the AppDbContext OnModelCreating loop applies the tenant +
/// soft-delete global filter and the concurrency token automatically.
///
/// <para>The <c>(Status, NextAttemptUtc)</c> index is the Task 7.2 worker's drain path: fetch the
/// next batch of <see cref="OutboxStatus.Pending"/> rows whose <c>NextAttemptUtc</c> is due,
/// ordered by creation — so the drain query stays index-backed and fast at scale (Req 65.1).</para>
/// </summary>
public sealed class NotificationOutboxMessageEntityConfiguration : IEntityTypeConfiguration<NotificationOutboxMessage>
{
    public void Configure(EntityTypeBuilder<NotificationOutboxMessage> builder)
    {
        builder.ToTable("NotificationOutbox");

        builder.Property(m => m.TemplateKey).IsRequired().HasMaxLength(200);
        builder.Property(m => m.Category).IsRequired().HasMaxLength(100);
        builder.Property(m => m.Title).IsRequired().HasMaxLength(400);
        builder.Property(m => m.Body).IsRequired().HasMaxLength(2000);
        builder.Property(m => m.PayloadJson).HasMaxLength(4000);
        builder.Property(m => m.TargetPlayerId).HasMaxLength(128);
        builder.Property(m => m.LastError).HasMaxLength(1000);
        builder.Property(m => m.Status).HasConversion<int>();
        builder.Property(m => m.CategoryKind).HasConversion<int>();
        builder.Property(m => m.Channels).HasConversion<int>();

        // Worker drain path (Task 7.2): due Pending rows, oldest first.
        builder.HasIndex(m => new { m.Status, m.NextAttemptUtc, m.CreatedAtUtc })
            .HasDatabaseName("IX_NotificationOutbox_Status_NextAttempt_Created");

        // Lookup a recipient's notifications (admin "sent notifications" view, tenant-filtered).
        builder.HasIndex(m => m.RecipientUserId)
            .HasDatabaseName("IX_NotificationOutbox_Recipient");
    }
}
