namespace CommunityOS.Application.Helpdesk;

/// <summary>
/// Helpdesk_Service (Task 20.1, Req 32.1–32.4): ticket creation with configurable classification and
/// File_Service attachments, plus the tenant/self-scoped read surface. Assignment / SLA / escalation
/// (Task 20.2), the status lifecycle / feedback / analytics (Task 20.3) and the HTTP controllers
/// (Task 21.4) extend this same seam in later tasks.
///
/// <para><b>Creation (Req 32.1, 32.3).</b> A resident with the <c>Create</c> permission on
/// <c>Ticket</c> raises a ticket for their OWN unit: the host unit is resolved tenant + resident-self
/// scoped (so a cross-tenant / non-own unit is a validation error — the resident-raises-for-own-unit
/// rule), the category/subcategory/priority are validated against the editable
/// <c>Ticket_Category</c> / <c>Ticket_Subcategory</c> / <c>Ticket_Priority</c> lists (an omitted
/// priority falls back to the configured community default), the status starts <c>new</c>, and each
/// supplied File_Service reference is stored as a <c>TicketAttachment</c> row associated with the
/// ticket and its community (Req 32.3). Audited.</para>
///
/// <para><b>Tenant + self scope (Req 32.4).</b> <c>Ticket</c> and <c>TicketAttachment</c> are
/// <c>IUnitScoped</c>, so the DbContext resident self-scope filter restricts a resident principal to
/// tickets (and their attachments) for their own household/unit — no bespoke predicate here; a staff
/// user with the <c>View</c> permission sees all tickets in their authorized communities. Reads use
/// <c>AsNoTracking</c>; mutations are audited.</para>
/// </summary>
public interface IHelpdeskService
{
    /// <summary>
    /// Raise a helpdesk ticket for the resident's own unit (Req 32.1, 32.3). Requires the
    /// <c>Create</c> permission on <c>Ticket</c> + an authorized/same-community (resident self-scoped)
    /// host unit; validates the category/subcategory/priority against the configurable lists (an
    /// omitted priority uses the configured community default); sets the status to <c>new</c>; and
    /// stores one <c>TicketAttachment</c> per supplied File_Service reference associated with the
    /// ticket and its community (requiring <c>Create</c> on <c>TicketAttachment</c> when any are
    /// supplied). Returns the created ticket. Audited.
    /// </summary>
    Task<TicketDto> CreateTicketAsync(CreateTicketRequest request, CancellationToken cancellationToken = default);

    /// <summary>Fetch one ticket by id, tenant/self-scoped; unknown/foreign id → null (Req 32.4).</summary>
    Task<TicketDto?> GetTicketAsync(Guid ticketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated/filtered/sorted/searchable ticket list (Req 32.4). Tenant + resident-self-scoped by
    /// the global query filter (a resident only sees tickets for their own household/unit); the
    /// allow-listed <c>ListQueryConfiguration</c> fixes which fields a client may sort/filter/search
    /// on, so no arbitrary client LINQ reaches the query. <c>AsNoTracking</c> projection, page size
    /// clamped by the shared policy — mirrors the other module list endpoints.
    /// </summary>
    Task<CommunityOS.Contracts.Envelope.PagedData<TicketDto>> ListTicketsAsync(
        CommunityOS.Application.Querying.ListQueryParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// List a ticket's attachments (Req 32.3). Requires the <c>View</c> permission on
    /// <c>TicketAttachment</c>; the attachments are tenant + resident-self scoped by the global filter
    /// (a resident only sees attachments on their own unit's tickets, a foreign-tenant ticket's
    /// attachments are never returned). An unknown/foreign ticket id yields an empty list.
    /// <c>AsNoTracking</c> indexed read, no N+1.
    /// </summary>
    Task<IReadOnlyList<TicketAttachmentDto>> GetTicketAttachmentsAsync(Guid ticketId, CancellationToken cancellationToken = default);

    /// <summary>List a ticket's comments oldest-first (View on TicketComment), tenant/self-scoped.</summary>
    Task<IReadOnlyList<TicketCommentDto>> GetTicketCommentsAsync(Guid ticketId, CancellationToken cancellationToken = default);

    /// <summary>Add a comment to a ticket (Create on TicketComment), tenant/self-scoped. Returns it. Audited.</summary>
    Task<TicketCommentDto> AddTicketCommentAsync(Guid ticketId, AddTicketCommentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assign or re-assign a ticket to a maintenance-staff member (Req 33.1, 33.2, 33.4, 33.5).
    /// Requires the <c>Assign</c> permission on <c>Ticket</c> + an authorized community. The target
    /// assignee must be a staff user holding <c>Edit</c> on <c>Ticket</c> in the ticket's community
    /// (RBAC data, no hardcoded recipient) — otherwise a validation error. The ticket moves to
    /// <c>assigned</c> through the editable <c>Ticket_Status_Transition</c> allow-list (an illegal
    /// transition → VALIDATION_ERROR, nothing mutated), its SLA timer is (re)started from the
    /// configurable per-priority SLA duration (<c>SlaDueAtUtc = now + ticket.sla.hours.{priority}</c>,
    /// clearing any prior escalation stamp so the new assignee gets a fresh timer), and the
    /// (re)assignment + the acting user are recorded via the audit trail (Req 33.4). The transition
    /// records exactly once under the ticket <c>RowVersion</c> (a concurrency loser → CONFLICT). The
    /// complaint-assigned push to the new assignee rides the business transaction via the outbox
    /// (Req 33.5, 64.3). Returns the updated ticket. Audited.
    /// </summary>
    Task<TicketDto> AssignTicketAsync(Guid ticketId, AssignTicketRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Background SLA-breach escalation sweep (Req 33.3, Task 20.2). Escalates a BOUNDED batch (at
    /// most <paramref name="maxTickets"/>, capped internally) of assigned-but-unresolved tickets whose
    /// <c>SlaDueAtUtc</c> has passed and that have not yet been escalated: each gets a one-time
    /// escalation stamp (so it is never double-escalated), an audit entry, and an SLA-breach
    /// notification to the next responder via Notification_Service (push + email). Runs under the
    /// worker's cross-tenant super-admin context, saves per ticket (so one ticket's concurrency loss
    /// is isolated), and stamps the escalation under the ticket <c>RowVersion</c> (a concurrency loser
    /// is skipped rather than re-notified). Returns the number of tickets escalated. Audited per
    /// ticket. Mirrors the parcel custody-SLA sweep.
    /// </summary>
    Task<int> EscalateBreachedTicketsAsync(int maxTickets, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drive a ticket through its configurable status lifecycle (Req 34.1, 34.2, 34.3, Task 20.3).
    /// Requires the <c>Edit</c> permission on <c>Ticket</c> + an authorized community. The target
    /// status must be a valid, active <c>Ticket_Status</c> code AND the <c>current-&gt;to</c>
    /// transition must be present in the editable <c>Ticket_Status_Transition</c> allow-list — an
    /// illegal/no-op transition → VALIDATION_ERROR with nothing mutated (no hardcoded table, exactly
    /// like <c>ChangeParcelStatusAsync</c>). The transition records exactly once under the ticket
    /// <c>RowVersion</c> (a concurrency loser → CONFLICT). On the move into <c>resolved</c> the
    /// resolution instant is stamped and the complaint-resolved notice to the requesting resident
    /// rides the business transaction via the outbox (Req 34.3); a reopen clears the resolution
    /// instant. Returns the updated ticket. Audited.
    /// </summary>
    Task<TicketDto> ChangeTicketStatusAsync(Guid ticketId, ChangeTicketStatusRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submit resident feedback/rating on a resolved ticket (Req 34.4, Task 20.3). Requires the
    /// <c>Create</c> permission on <c>Ticket</c>. The ticket is loaded tenant + resident-self scoped,
    /// so a resident can only leave feedback on their OWN household/unit's ticket (an unknown/foreign
    /// id → not found). Feedback is accepted only while the ticket is in a feedback-eligible status
    /// (<c>resolved</c> / <c>closed</c>) and the rating must fall within the community's configurable
    /// <c>[min, max]</c> bounds (<c>ticket.feedback.minRating</c> / <c>ticket.feedback.maxRating</c>,
    /// no hardcoded scale) — otherwise a VALIDATION_ERROR with nothing mutated. Stores the rating,
    /// optional comment and submission time against the ticket under its <c>RowVersion</c>. Returns
    /// the updated ticket. Audited.
    /// </summary>
    Task<TicketDto> SubmitFeedbackAsync(Guid ticketId, SubmitTicketFeedbackRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aggregated helpdesk analytics for a user with the <c>View</c> permission (Req 34.5, Task 20.3).
    /// Computes the totals, the per-status / per-category / per-priority breakdowns, the SLA-compliance
    /// counts (resolved on/before the SLA due instant vs breached) and the average resolution time —
    /// all DB-side (GROUP BY / Count / Average translated to SQL, no table materialized in memory) and
    /// scoped to the caller's authorized communities by the tenant global query filter. An optional
    /// community filter further narrows the scope to one authorized community. Read-only.
    /// </summary>
    Task<TicketAnalyticsDto> GetTicketAnalyticsAsync(Guid? communityId = null, CancellationToken cancellationToken = default);
}
