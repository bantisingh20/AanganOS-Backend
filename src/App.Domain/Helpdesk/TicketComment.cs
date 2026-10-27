using App.Domain.Common;

namespace App.Domain.Helpdesk;

/// <summary>
/// One comment on a <see cref="Ticket"/> — the helpdesk discussion thread. A resident or staff member
/// posts a text message against a ticket; the comments read newest-first as a conversation. A ticket
/// may carry zero or more comments, so this is a one-to-many child keyed by <see cref="TicketId"/>,
/// modelled on <see cref="TicketAttachment"/> (its sibling child-of-ticket).
///
/// <para><b>Author + time.</b> The author and timestamp come from the inherited audit columns
/// <c>CreatedBy</c> (the acting user id, stamped by the SaveChanges interceptor) and
/// <c>CreatedAtUtc</c> — no separate field. The comment body is the only payload.</para>
///
/// <para><b>Resident self-scope (Req 32.4, 5.4).</b> Implements <see cref="IUnitScoped"/> via its
/// <see cref="UnitId"/> (mirrored from the parent ticket's host unit), so the DbContext resident
/// self-scope filter restricts a resident principal to comments on tickets for their own unit — no
/// bespoke predicate in the service.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token, audit columns.</para>
/// </summary>
public class TicketComment : TenantOwnedEntity, IUnitScoped
{
    /// <summary>
    /// The parent ticket's host unit, mirrored from <see cref="Ticket.UnitId"/> so the resident
    /// self-scope filter restricts a resident to their own unit's ticket comments. Nullable to satisfy
    /// <see cref="IUnitScoped"/>.
    /// </summary>
    public Guid? UnitId { get; set; }

    /// <summary>The ticket this comment belongs to. Required.</summary>
    public Guid TicketId { get; set; }

    /// <summary>The comment text. Required.</summary>
    public string Body { get; set; } = string.Empty;
}
