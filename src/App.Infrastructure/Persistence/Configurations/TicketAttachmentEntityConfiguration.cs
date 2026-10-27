using App.Domain.Files;
using App.Domain.Helpdesk;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="TicketAttachment"/> table (Req 32.3). As a <c>TenantOwnedEntity</c> that is
/// also <c>IUnitScoped</c> it inherits the tenant + soft-delete + resident self-scope global filter
/// (Req 32.4, 5.4) and the rowversion mapping from <c>AppDbContext.OnModelCreating</c> — a resident
/// only sees attachments on tickets for their own unit.
///
/// <para>The <c>(CommunityId, TicketId)</c> index backs "list a ticket's attachments" as a single
/// indexed read (no N+1). The ticket FK is <c>Restrict</c> on delete so a ticket cannot be
/// hard-deleted out from under its attachments (deletes are soft deletes anyway). The attachment
/// references the File_Service <c>StoredFile</c> by <see cref="TicketAttachment.FileId"/>, with a
/// <c>Restrict</c> FK so the referenced file row cannot be hard-deleted while an attachment points at
/// it — the bytes stay reachable through the authorized download path (Req 7.2).</para>
/// </summary>
public sealed class TicketAttachmentEntityConfiguration : IEntityTypeConfiguration<TicketAttachment>
{
    public void Configure(EntityTypeBuilder<TicketAttachment> builder)
    {
        builder.ToTable("TicketAttachments");
        builder.HasKey(a => a.Id);

        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(a => a.TicketId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<StoredFile>()
            .WithMany()
            .HasForeignKey(a => a.FileId)
            .OnDelete(DeleteBehavior.Restrict);

        // "List a ticket's attachments" + the resident self-view read path (Req 32.3).
        builder.HasIndex(a => new { a.CommunityId, a.TicketId })
            .HasDatabaseName("IX_TicketAttachment_CommunityId_TicketId");
    }
}
