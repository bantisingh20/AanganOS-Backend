using App.Domain.Helpdesk;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="TicketComment"/> table (helpdesk discussion thread). As a
/// <c>TenantOwnedEntity</c> that is also <c>IUnitScoped</c> it inherits the tenant + soft-delete +
/// resident self-scope global filter and the rowversion mapping from <c>AppDbContext.OnModelCreating</c>
/// — a resident only sees comments on tickets for their own unit.
///
/// <para>The <c>(CommunityId, TicketId, CreatedAtUtc)</c> index backs "list a ticket's comments in
/// order" as a single indexed read (no N+1). The ticket FK is <c>Restrict</c> on delete so a ticket
/// cannot be hard-deleted out from under its comments (deletes are soft deletes anyway).</para>
/// </summary>
public sealed class TicketCommentEntityConfiguration : IEntityTypeConfiguration<TicketComment>
{
    public void Configure(EntityTypeBuilder<TicketComment> builder)
    {
        builder.ToTable("TicketComments");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Body).IsRequired().HasMaxLength(2000);

        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(c => c.TicketId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chronological "list a ticket's comments" read path.
        builder.HasIndex(c => new { c.CommunityId, c.TicketId, c.CreatedAtUtc })
            .HasDatabaseName("IX_TicketComment_CommunityId_TicketId_CreatedAtUtc");
    }
}
