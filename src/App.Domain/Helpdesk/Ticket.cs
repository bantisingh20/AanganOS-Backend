using App.Domain.Common;

namespace App.Domain.Helpdesk;

/// <summary>
/// A helpdesk complaint/request a resident raises for their OWN unit (Req 32.1). The resident
/// supplies a description, a configurable <see cref="Category"/> / <see cref="Subcategory"/> /
/// <see cref="Priority"/> (master-data codes, NOT frozen enums) and optional attachments
/// (<see cref="TicketAttachment"/> rows referencing File_Service, Req 32.3). The ticket is anchored
/// in the host <see cref="UnitId"/>'s community via the owning
/// <see cref="TenantOwnedEntity.CommunityId"/>.
///
/// <para><b>Resident self-scope (Req 32.4, 5.4).</b> Implements <see cref="IUnitScoped"/> via its
/// non-null <see cref="UnitId"/> (the host unit the ticket was raised for) surfaced as
/// <c>UnitId</c>, so the DbContext resident self-scope filter restricts a resident principal to
/// tickets for their own household/unit — no bespoke predicate in the service. A staff user with the
/// View permission sees all tickets in their authorized communities (the tenant filter), exactly
/// like Parcel. <see cref="HostUnitId"/> is a get/set alias over <see cref="UnitId"/> for a
/// readable domain name.</para>
///
/// <para><b>Configurable classification (steering: no hardcoding, Req 32.2).</b>
/// <see cref="Category"/> resolves against the editable <c>Ticket_Category</c> list,
/// <see cref="Subcategory"/> against <c>Ticket_Subcategory</c> and <see cref="Priority"/> against
/// <c>Ticket_Priority</c>. All stored as stable codes an admin can add to / retire without a code
/// change. The <see cref="TicketClassification"/> constants only name the seeded default codes for
/// the service's convenience — they are data, not behavioural enums.</para>
///
/// <para><b>Status (Task 20.3 extends this).</b> <see cref="Status"/> resolves against the editable
/// <c>Ticket_Status</c> list and starts <see cref="TicketStatuses.New"/> on creation. The full
/// status state machine, feedback and analytics are Task 20.3; assignment / SLA / escalation are
/// Task 20.2. This task fixes the create-time status to <c>new</c> and leaves those seams for the
/// later tasks to extend.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token (the Task 20.2/20.3 transitions record exactly once), audit columns.</para>
/// </summary>
public class Ticket : TenantOwnedEntity, IUnitScoped
{
    /// <summary>
    /// The host unit the ticket was raised for (Req 32.1). Non-null in practice; typed nullable to
    /// satisfy <see cref="IUnitScoped"/>, which the resident self-scope filter keys on (Req 32.4).
    /// This IS the host unit — see <see cref="HostUnitId"/>.
    /// </summary>
    public Guid? UnitId { get; set; }

    /// <summary>The host unit the ticket was raised for (Req 32.1). Alias over <see cref="UnitId"/>.</summary>
    public Guid HostUnitId
    {
        get => UnitId ?? Guid.Empty;
        set => UnitId = value;
    }

    /// <summary>
    /// Optional link to the raising resident (Req 32.1). The acting user is already captured on the
    /// audit trail by the SaveChanges interceptor; the resident-id link is wired by the Task 21.4
    /// endpoint when the caller is a resident principal.
    /// </summary>
    public Guid? RaisedByResidentId { get; set; }

    /// <summary>Short title/summary of the complaint/request (Req 32.1). Required.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Free-text description of the complaint/request (Req 32.1). Required.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Configurable category code resolved against the <c>Ticket_Category</c> master-data list
    /// (Req 32.1, 32.2). Stored as the stable code, never a frozen enum.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Configurable subcategory code resolved against the <c>Ticket_Subcategory</c> master-data list
    /// (Req 32.1, 32.2). Optional (not every category needs a subcategory). Stored as the stable
    /// code, never a frozen enum.
    /// </summary>
    public string? Subcategory { get; set; }

    /// <summary>
    /// Configurable priority code resolved against the <c>Ticket_Priority</c> master-data list
    /// (Req 32.1, 32.2). Stored as the stable code, never a frozen enum.
    /// </summary>
    public string Priority { get; set; } = string.Empty;

    /// <summary>
    /// Configurable status code resolved against the <c>Ticket_Status</c> master-data list. Starts
    /// <see cref="TicketStatuses.New"/> on creation; the Task 20.3 state machine drives transitions.
    /// Stored as the stable code, never a frozen enum.
    /// </summary>
    public string Status { get; set; } = TicketStatuses.New;

    // --- Assignment / SLA / escalation (Task 20.2, Req 33.1–33.5) ---------------------------------

    /// <summary>
    /// The maintenance-staff <c>User</c> the ticket is currently assigned to (Req 33.1), or null while
    /// unassigned. Set when the ticket is auto-assigned on creation (per the configurable
    /// <c>Ticket_Assignment_Rule</c> for its category) or re-assigned by a user with the
    /// <c>Assign</c> permission (Req 33.4). The assignee must be a staff user holding <c>Edit</c> on
    /// <c>Ticket</c> in the community (RBAC data, resolved by the service — no hardcoded recipient).
    /// </summary>
    public Guid? AssignedToUserId { get; set; }

    /// <summary>
    /// When the ticket was (re)assigned to <see cref="AssignedToUserId"/> (Req 33.1, 33.2), or null
    /// while unassigned. Also the SLA start instant: <see cref="SlaDueAtUtc"/> is derived from this
    /// plus the priority's configurable SLA duration (Req 33.2).
    /// </summary>
    public DateTime? AssignedAtUtc { get; set; }

    /// <summary>
    /// The SLA target instant the ticket should be resolved by (Req 33.2), or null while unassigned.
    /// Computed as <see cref="AssignedAtUtc"/> + the configurable per-priority SLA duration
    /// (<c>ticket.sla.hours.{priority}</c>) at assignment time. The background sweep treats a ticket
    /// as breached once <c>now &gt; SlaDueAtUtc</c> and it is still unresolved (Req 33.3).
    /// </summary>
    public DateTime? SlaDueAtUtc { get; set; }

    /// <summary>
    /// When the ticket's SLA breach was escalated to the next responder, or null while it has not
    /// been escalated (Req 33.3, Task 20.2). The background SLA sweep considers a ticket breached only
    /// while this is null, stamps it when it escalates, and so escalates a given ticket AT MOST ONCE —
    /// a stored idempotency marker, not a per-pass re-notification. Mirrors
    /// <c>Parcel.CustodySlaEscalatedAtUtc</c>. A later re-assignment restarts the SLA by clearing this
    /// (the new assignee gets a fresh timer).
    /// </summary>
    public DateTime? SlaEscalatedAtUtc { get; set; }

    // --- Resolution + feedback (Task 20.3, Req 34.3, 34.4, 34.5) ----------------------------------

    /// <summary>
    /// When the ticket was resolved (Req 34.3, 34.5), or null until it reaches <c>resolved</c>. Stamped
    /// by the status lifecycle on the transition into <see cref="TicketStatuses.Resolved"/> and anchors
    /// the analytics resolution-time (raise → resolve) and SLA-compliance (resolved on/before
    /// <see cref="SlaDueAtUtc"/>?) figures (Req 34.5). A reopen clears it so a re-resolution re-stamps a
    /// fresh resolution instant.
    /// </summary>
    public DateTime? ResolvedAtUtc { get; set; }

    // --- Feedback (Task 20.3, Req 34.4) -----------------------------------------------------------

    /// <summary>
    /// The resident's satisfaction rating on the ticket (Req 34.4), or null until feedback is given.
    /// Stored as an integer on a configurable scale — the allowed <c>[min, max]</c> bounds are
    /// resolved from Config_Service (<c>ticket.feedback.minRating</c> / <c>ticket.feedback.maxRating</c>,
    /// seeded fallbacks 1..5), never a hardcoded scale (steering: no hardcoding). Feedback is only
    /// accepted on the ticket's OWN resident (resident self-scope filter) once the ticket has reached
    /// a feedback-eligible status (resolved / closed).
    /// </summary>
    public int? Rating { get; set; }

    /// <summary>Optional free-text comment the resident submits with the <see cref="Rating"/> (Req 34.4), or null.</summary>
    public string? FeedbackComment { get; set; }

    /// <summary>When the resident submitted feedback (Req 34.4), or null while no feedback has been given.</summary>
    public DateTime? FeedbackAtUtc { get; set; }
}

/// <summary>
/// Well-known default <c>Ticket_Status</c> codes (Req 34.1). Seeded master-data codes kept as
/// constants so the service sets/compares status without a magic string. <b>Data</b>, not a
/// behavioural enum — the status list AND the allowed transitions (Task 20.3) are editable master
/// data; this is only a convenience for the known codes.
/// </summary>
public static class TicketStatuses
{
    /// <summary>Initial status on creation (Req 32.1, 34.1): the ticket has been raised, not yet assigned.</summary>
    public const string New = "new";

    /// <summary>Assigned to a maintenance staff member (Req 33.1, 34.1).</summary>
    public const string Assigned = "assigned";

    /// <summary>Work has started on the ticket (Req 34.1).</summary>
    public const string InProgress = "in_progress";

    /// <summary>Waiting on the resident / a third party (Req 34.1).</summary>
    public const string Waiting = "waiting";

    /// <summary>The issue has been resolved (Req 34.1); the resident may give feedback (Task 20.3).</summary>
    public const string Resolved = "resolved";

    /// <summary>The ticket is closed (Req 34.1).</summary>
    public const string Closed = "closed";

    /// <summary>A previously resolved/closed ticket was reopened (Req 34.1).</summary>
    public const string Reopened = "reopened";
}

/// <summary>
/// Well-known default <c>Ticket_Category</c> / <c>Ticket_Subcategory</c> / <c>Ticket_Priority</c>
/// codes (Req 32.1, 32.2). Seeded master-data codes kept as constants. <b>Data</b>, not behavioural
/// enums — every list is editable per community, so an admin adds/retires a classification without a
/// code change; these constants only name the seeded defaults.
/// </summary>
public static class TicketClassification
{
    // Ticket_Category defaults.
    public const string CategoryPlumbing = "plumbing";
    public const string CategoryElectrical = "electrical";
    public const string CategoryHousekeeping = "housekeeping";
    public const string CategorySecurity = "security";
    public const string CategoryCommonArea = "common_area";
    public const string CategoryOther = "other";

    // Ticket_Priority defaults.
    public const string PriorityLow = "low";
    public const string PriorityMedium = "medium";
    public const string PriorityHigh = "high";
    public const string PriorityUrgent = "urgent";
}
