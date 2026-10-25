using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Helpdesk;

/// <summary>
/// One attachment (photo / video / document) on a <see cref="Ticket"/> (Req 32.3). The resident
/// uploads the bytes through File_Service, which stores them under a <c>StoredFile</c> row bound to
/// the ticket (<c>OwningResourceType = "Ticket"</c>, <c>OwningResourceId = ticketId</c>) and carrying
/// the ticket's community; this row then references that stored file by its opaque
/// <see cref="FileId"/> — never inline bytes or a path (the opaque-reference pattern,
/// <c>StoredFile</c> remarks). A ticket may carry zero or more attachments, so this is a one-to-many
/// child keyed by <see cref="TicketId"/>.
///
/// <para><b>Associated with the Ticket and its CommunityId (Req 32.3).</b> The attachment row carries
/// the same <see cref="TenantOwnedEntity.CommunityId"/> as its ticket (set by the service), and the
/// referenced <c>StoredFile</c> independently carries that community too, so the authorized streaming
/// check stays tenant-safe (Req 7.2).</para>
///
/// <para><b>Resident self-scope (Req 32.4, 5.4).</b> Implements <see cref="IUnitScoped"/> via its
/// <see cref="UnitId"/> (the parent ticket's host unit), so the DbContext resident self-scope filter
/// restricts a resident principal to attachments on tickets for their own unit — the same stance
/// <c>ParcelAuthorization</c> takes for a unit-scoped child. No bespoke predicate in the service.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token, audit columns.</para>
/// </summary>
public class TicketAttachment : TenantOwnedEntity, IUnitScoped
{
    /// <summary>
    /// The parent ticket's host unit (Req 32.4), mirrored from <see cref="Ticket.UnitId"/> so the
    /// resident self-scope filter restricts a resident to their own unit's ticket attachments. Typed
    /// nullable to satisfy <see cref="IUnitScoped"/>.
    /// </summary>
    public Guid? UnitId { get; set; }

    /// <summary>The ticket this attachment belongs to (Req 32.3). Required.</summary>
    public Guid TicketId { get; set; }

    /// <summary>
    /// Opaque File_Service reference to the stored attachment bytes (Req 32.3). This is the
    /// <c>StoredFile.Id</c>; the bytes live in the blob store outside the webroot, streamed only
    /// through the authorized File_Service download path (Req 7.2, 7.4). Required.
    /// </summary>
    public Guid FileId { get; set; }
}
