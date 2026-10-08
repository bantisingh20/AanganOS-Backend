namespace CommunityOS.Application.Helpdesk;

/// <summary>Read shape for a <c>Ticket</c> returned to callers (Req 32.1).</summary>
/// <param name="Id">The ticket id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="HostUnitId">The host unit the ticket was raised for.</param>
/// <param name="RaisedByResidentId">The raising resident, when linked, or null.</param>
/// <param name="Title">Short title/summary of the complaint/request.</param>
/// <param name="Description">Free-text description of the complaint/request.</param>
/// <param name="Category">Configurable ticket-category code (Req 32.1, 32.2).</param>
/// <param name="Subcategory">Configurable ticket-subcategory code, or null (Req 32.1, 32.2).</param>
/// <param name="Priority">Configurable ticket-priority code (Req 32.1, 32.2).</param>
/// <param name="Status">Configurable ticket-status code (new on creation, Req 34.1).</param>
/// <param name="AssignedToUserId">The maintenance-staff user the ticket is assigned to, or null while unassigned (Req 33.1).</param>
/// <param name="AssignedAtUtc">When the ticket was (re)assigned, or null while unassigned (Req 33.1).</param>
/// <param name="SlaDueAtUtc">The per-priority SLA target instant, or null while unassigned (Req 33.2).</param>
/// <param name="SlaEscalatedAtUtc">When the ticket's SLA breach was escalated, or null (Req 33.3).</param>
/// <param name="Rating">The resident's feedback rating on the configurable scale, or null until given (Req 34.4).</param>
/// <param name="FeedbackComment">Optional free-text feedback comment, or null (Req 34.4).</param>
/// <param name="FeedbackAtUtc">When the resident submitted feedback, or null (Req 34.4).</param>
/// <param name="CreatedAtUtc">When the ticket was raised.</param>
public sealed record TicketDto(
    Guid Id,
    Guid CommunityId,
    Guid HostUnitId,
    Guid? RaisedByResidentId,
    string Title,
    string Description,
    string Category,
    string? Subcategory,
    string Priority,
    string Status,
    Guid? AssignedToUserId,
    DateTime? AssignedAtUtc,
    DateTime? SlaDueAtUtc,
    DateTime? SlaEscalatedAtUtc,
    int? Rating,
    string? FeedbackComment,
    DateTime? FeedbackAtUtc,
    DateTime CreatedAtUtc);

/// <summary>
/// Drive a helpdesk ticket to a new <c>Ticket_Status</c> (Req 34.1, 34.2). The caller supplies the
/// target status code; the service validates the <c>current-&gt;to</c> transition against the
/// editable <c>Ticket_Status_Transition</c> allow-list (an illegal/no-op transition →
/// VALIDATION_ERROR, nothing mutated — no hardcoded table), records it under the ticket
/// <c>RowVersion</c> and, when the ticket is resolved, notifies the requesting resident
/// (Req 34.3). Mirrors <c>ChangeParcelStatusRequest</c>.
/// </summary>
/// <param name="ToStatus">The target <c>Ticket_Status</c> code. Required.</param>
/// <param name="Reason">Optional free-text reason recorded in the audit trail.</param>
public sealed record ChangeTicketStatusRequest(
    string ToStatus,
    string? Reason);

/// <summary>
/// Submit resident feedback/rating on a resolved ticket (Req 34.4). The resident supplies an integer
/// <see cref="Rating"/> on the community's configurable scale and an optional comment; the service
/// restricts this to the ticket's OWN resident (resident self-scope filter), accepts it only while
/// the ticket is in a feedback-eligible status (resolved / closed) and validates the rating against
/// the configured <c>[min, max]</c> bounds — otherwise a validation error with nothing mutated.
/// </summary>
/// <param name="Rating">The satisfaction rating on the configurable scale (within the configured bounds). Required.</param>
/// <param name="Comment">Optional free-text feedback comment.</param>
public sealed record SubmitTicketFeedbackRequest(
    int Rating,
    string? Comment);

/// <summary>One grouped analytics bucket: a <paramref name="Key"/> value and its ticket <paramref name="Count"/> (Req 34.5).</summary>
/// <param name="Key">The group value (a status / category / priority code).</param>
/// <param name="Count">How many tickets fall in the group within the authorized/queried scope.</param>
public sealed record TicketCountByKeyDto(string Key, int Count);

/// <summary>
/// Aggregated helpdesk analytics for a user with the <c>View</c> permission (Req 34.5), computed
/// DB-side and scoped to the user's authorized communities (the tenant global filter). Carries the
/// total ticket count, the per-status / per-category / per-priority breakdowns, the SLA-compliance
/// figures (resolved-within-SLA vs breached, derived from the stored SLA due instant) and the
/// average resolution time in hours over resolved/closed tickets.
/// </summary>
/// <param name="TotalTickets">Total tickets in the authorized/queried scope.</param>
/// <param name="ByStatus">Ticket counts grouped by <c>Ticket_Status</c> code.</param>
/// <param name="ByCategory">Ticket counts grouped by <c>Ticket_Category</c> code.</param>
/// <param name="ByPriority">Ticket counts grouped by <c>Ticket_Priority</c> code.</param>
/// <param name="ResolvedWithinSla">Resolved/closed tickets whose resolution landed on/before their SLA due instant (SLA compliant).</param>
/// <param name="ResolvedBreachedSla">Resolved/closed tickets whose resolution landed after their SLA due instant (SLA breached).</param>
/// <param name="AverageResolutionHours">Average hours from raise to resolution over resolved/closed tickets, or null when none are resolved.</param>
public sealed record TicketAnalyticsDto(
    int TotalTickets,
    IReadOnlyList<TicketCountByKeyDto> ByStatus,
    IReadOnlyList<TicketCountByKeyDto> ByCategory,
    IReadOnlyList<TicketCountByKeyDto> ByPriority,
    int ResolvedWithinSla,
    int ResolvedBreachedSla,
    double? AverageResolutionHours);

/// <summary>
/// Assign or re-assign a helpdesk ticket to a maintenance-staff member (Req 33.1, 33.4). The caller
/// supplies the target assignee <c>User</c> id; the service validates the assignee is a staff user
/// holding <c>Edit</c> on <c>Ticket</c> in the ticket's community, transitions the ticket to
/// <c>assigned</c> through the configurable <c>Ticket_Status_Transition</c> allow-list, (re)starts
/// the per-priority SLA timer, records the (re)assignment and the acting user, and sends the
/// complaint-assigned push to the new assignee (Req 33.5, 64.3).
/// </summary>
/// <param name="AssignedToUserId">The maintenance-staff user to assign the ticket to. Required.</param>
public sealed record AssignTicketRequest(Guid AssignedToUserId);

/// <summary>Read shape for one <c>TicketAttachment</c> — a File_Service reference on a ticket (Req 32.3).</summary>
/// <param name="Id">The attachment id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="TicketId">The ticket this attachment belongs to.</param>
/// <param name="FileId">Opaque File_Service reference to the stored bytes (streamed via File_Service).</param>
public sealed record TicketAttachmentDto(
    Guid Id,
    Guid CommunityId,
    Guid TicketId,
    Guid FileId);

/// <summary>
/// Raise a helpdesk ticket for the resident's OWN unit (Req 32.1, 32.3). The resident supplies the
/// host unit, a title + description, a configurable category / optional subcategory / optional
/// priority (validated against the editable <c>Ticket_Category</c> / <c>Ticket_Subcategory</c> /
/// <c>Ticket_Priority</c> lists — when the priority is omitted the configured community default is
/// used) and optional File_Service attachment references. The service anchors the ticket in the host
/// unit's community, sets the status to <c>new</c> and stores one <c>TicketAttachment</c> row per
/// supplied file reference (associated with the ticket and its community, Req 32.3).
/// </summary>
/// <param name="HostUnitId">The host unit the ticket is raised for (must be in the caller's community; a resident's own unit). Required.</param>
/// <param name="Title">Short title/summary. Required.</param>
/// <param name="Description">Free-text description. Required.</param>
/// <param name="Category">Ticket-category code from the Ticket_Category list. Required.</param>
/// <param name="Subcategory">Optional ticket-subcategory code from the Ticket_Subcategory list.</param>
/// <param name="Priority">Optional ticket-priority code from the Ticket_Priority list; defaults to the configured community default when omitted.</param>
/// <param name="AttachmentFileIds">Optional File_Service references (photo/video/document) to associate with the ticket (Req 32.3).</param>
public sealed record CreateTicketRequest(
    Guid HostUnitId,
    string Title,
    string Description,
    string Category,
    string? Subcategory,
    string? Priority,
    IReadOnlyList<Guid>? AttachmentFileIds);
