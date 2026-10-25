using UrbanNest.Application.Abstractions;
using UrbanNest.Application.Abstractions.Security;
using UrbanNest.Application.Audit;
using UrbanNest.Application.Auth;
using UrbanNest.Application.Config;
using UrbanNest.Application.Exceptions;
using UrbanNest.Application.Helpdesk;
using UrbanNest.Application.Notifications;
using UrbanNest.Application.Querying;
using UrbanNest.Contracts.Envelope;
using UrbanNest.Domain.Identity;
using UrbanNest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using DomainTicket = UrbanNest.Domain.Helpdesk.Ticket;
using DomainTicketAttachment = UrbanNest.Domain.Helpdesk.TicketAttachment;
using DomainTicketComment = UrbanNest.Domain.Helpdesk.TicketComment;
using DomainTicketStatuses = UrbanNest.Domain.Helpdesk.TicketStatuses;
using DomainTicketClassification = UrbanNest.Domain.Helpdesk.TicketClassification;

namespace UrbanNest.Infrastructure.Helpdesk;

/// <summary>
/// EF-backed <see cref="IHelpdeskService"/> (Task 20.1, Req 32.1–32.4): ticket creation with
/// configurable classification + File_Service attachments, and the tenant/self-scoped read surface.
/// Assignment / SLA / escalation (20.2), the status lifecycle / feedback / analytics (20.3) and the
/// HTTP controllers (21.4) extend this same seam in later tasks.
///
/// <para><b>Creation (Req 32.1, 32.3).</b> A resident with <c>Create</c> on <c>Ticket</c> raises a
/// ticket for their OWN unit: the host unit is resolved tenant + resident-self scoped (so a
/// cross-tenant / non-own unit is a validation error — the resident-raises-for-own-unit rule,
/// same-community by construction), the category + optional subcategory + priority are validated
/// against the editable <c>Ticket_Category</c> / <c>Ticket_Subcategory</c> / <c>Ticket_Priority</c>
/// lists (an omitted priority falls back to the configured community default), the status starts
/// <c>new</c>, and each supplied File_Service reference is stored as a <c>TicketAttachment</c> row
/// associated with the ticket and its community (Req 32.3). The ticket + its attachments + the audit
/// entry commit in ONE transaction. Audited.</para>
///
/// <para><b>Tenant + self scope (Req 32.4).</b> <c>Ticket</c> and <c>TicketAttachment</c> are
/// <c>IUnitScoped</c>, so the DbContext resident self-scope filter restricts a resident principal to
/// tickets (and their attachments) for their own household/unit — no bespoke predicate here. Reads
/// use <c>AsNoTracking</c>; mutations are audited.</para>
/// </summary>
public sealed class HelpdeskService(
    AppDbContext db,
    IAuditService audit,
    IConfigService config,
    IClock clock,
    IAuthorizationGuard authorizationGuard,
    INotificationService notifications,
    IPageSizePolicy pageSizePolicy,
    IAsyncQueryExecutor executor)
    : IHelpdeskService
{
    // Allow-list for the Task 21.4 ticket list endpoint (Req 32.4). Only these fields reach the query
    // — no arbitrary client LINQ (steering: no hardcoding, injection-safe). Sorts/filters ride the
    // ticket's indexed columns; default is newest-first. Mirrors the Parcel/Gate list configs.
    private static readonly ListQueryConfiguration<DomainTicket> TicketQueryConfig =
        new ListQueryConfiguration<DomainTicket>()
            .WithDefaultSort(t => t.CreatedAtUtc, SortDirection.Descending)
            .Sortable("createdAtUtc", t => t.CreatedAtUtc)
            .Sortable("status", t => t.Status)
            .Sortable("category", t => t.Category)
            .Sortable("priority", t => t.Priority)
            .Filterable("status", t => t.Status)
            .Filterable("category", t => t.Category)
            .Filterable("subcategory", t => t.Subcategory)
            .Filterable("priority", t => t.Priority)
            .Filterable("hostUnitId", t => t.UnitId)
            .Searchable(t => t.Title)
            .Searchable(t => t.Description);

    // Default Ticket_Priority (Req 32.1) when ticket.defaultPriority is unset in Config_Service — a
    // safe fallback, not a hardcoded decision: a community overrides the key to set its own default
    // without a code change (steering: no hardcoding). medium is a seeded Ticket_Priority code.
    private const string DefaultPriority = "medium";

    // Per-priority SLA fallbacks (hours) when ticket.sla.hours.{priority} is unset in Config_Service
    // (Req 33.2) — safe defaults, not a hardcoded decision: a community overrides the per-priority key
    // to tune its SLA without a code change (steering: no hardcoding). A priority NOT listed here (a
    // community-added Ticket_Priority code) falls back to DefaultSlaHours. Lower priority → longer SLA.
    private static readonly IReadOnlyDictionary<string, int> DefaultSlaHoursByPriority =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [DomainTicketClassification.PriorityUrgent] = 4,
            [DomainTicketClassification.PriorityHigh] = 8,
            [DomainTicketClassification.PriorityMedium] = 24,
            [DomainTicketClassification.PriorityLow] = 72,
        };

    // Fallback SLA (hours) for a priority with no seeded default above (Req 33.2). A conservative
    // whole-day target so an unknown/new priority still gets a bounded, overridable SLA.
    private const int DefaultSlaHours = 24;

    // Feedback rating scale fallbacks (Req 34.4) when ticket.feedback.minRating/maxRating is unset in
    // Config_Service — safe defaults (a 1..5 scale), not a hardcoded decision: a community overrides
    // the keys to set its own scale (e.g. 1..10) without a code change (steering: no hardcoding).
    private const int DefaultFeedbackMinRating = 1;
    private const int DefaultFeedbackMaxRating = 5;

    // The statuses in which a resident may submit feedback (Req 34.4): once the ticket is resolved or
    // closed. These are seeded Ticket_Status codes; a feedback attempt in any other status is rejected
    // so a resident can't rate work that is still in flight.
    private static readonly string[] FeedbackEligibleStatuses =
        [DomainTicketStatuses.Resolved, DomainTicketStatuses.Closed];

    // Hard upper bound on how many breached tickets a single SLA sweep call processes, regardless of
    // the worker-supplied batch size — keeps the sweep's work bounded so it stays correct and the app
    // stays up under a large backlog (steering: bounded work / scalable). Mirrors the parcel sweep's
    // MaxSlaSweepBatch; the worker passes its configured batch size and this clamps it.
    private const int MaxSlaSweepBatch = 500;

    public async Task<TicketDto> CreateTicketAsync(CreateTicketRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 32.1): a resident raises a ticket with Create on Ticket. The Task 21.4
        // endpoint gate declares the same requirement; the invariant must hold regardless of caller —
        // no permission → FORBIDDEN with nothing mutated.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "Ticket", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to raise a ticket.");
        }

        var title = RequireText(request.Title, "title", "A ticket title is required.");
        var description = RequireText(request.Description, "description", "A ticket description is required.");

        // Resolve the host unit tenant + resident-self scoped to anchor the ticket in the unit's
        // community and run the own-unit check. The resident self-scope filter (if the caller is a
        // resident) already restricts this read to their own unit, so a cross-unit host → not found
        // (the resident-raises-for-own-unit rule, Req 32.1, 32.4).
        var communityId = await RequireHostUnitCommunityAsync(request.HostUnitId, cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(communityId);

        var category = await RequireValidCodeAsync(
            request.Category, MasterDataKeys.TicketCategory, communityId, "category", "ticket category", cancellationToken);

        // Subcategory is optional: validate only when supplied, otherwise null.
        string? subcategory = null;
        if (!string.IsNullOrWhiteSpace(request.Subcategory))
        {
            subcategory = await RequireValidCodeAsync(
                request.Subcategory, MasterDataKeys.TicketSubcategory, communityId, "subcategory", "ticket subcategory", cancellationToken);
        }

        // Priority is optional on the request: fall back to the configured community default (also
        // validated) when omitted — never a hardcoded choice (steering: no hardcoding).
        var priorityCode = string.IsNullOrWhiteSpace(request.Priority)
            ? await config.GetStringAsync(ConfigKeys.TicketDefaultPriority, DefaultPriority, communityId, cancellationToken)
            : request.Priority;
        var priority = await RequireValidCodeAsync(
            priorityCode, MasterDataKeys.TicketPriority, communityId, "priority", "ticket priority", cancellationToken);

        var ticket = new DomainTicket
        {
            CommunityId = communityId,
            HostUnitId = request.HostUnitId,
            Title = title,
            Description = description,
            Category = category,
            Subcategory = subcategory,
            Priority = priority,
            Status = DomainTicketStatuses.New,
        };

        db.Tickets.Add(ticket);

        // Attachments (Req 32.3): store one TicketAttachment per supplied File_Service reference,
        // associated with the ticket and its community. Only Adds the rows — the single SaveChanges
        // below commits the ticket + its attachments + the audit entry atomically.
        await AddAttachmentsAsync(ticket, request.AttachmentFileIds, communityId, cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Create", "Ticket", ticket.Id.ToString(), ticket.CommunityId, Detail: null),
            cancellationToken);

        // Config-driven auto-assignment on creation (Req 33.1): resolve the assignee from the editable
        // Ticket_Assignment_Rule for the ticket's category (falling back to the configured default
        // assignee); when one resolves, move new->assigned through the allow-list, start the SLA timer
        // and enqueue the complaint-assigned push — all staged on this same context so the single
        // SaveChanges below commits the ticket + attachments + audit + assignment + notification
        // atomically. When no rule/default resolves the ticket stays `new`/unassigned (documented
        // choice — a mis-configured community never mis-routes a ticket).
        var autoAssignee = await ResolveAutoAssigneeAsync(category, communityId, cancellationToken);
        if (autoAssignee is Guid assigneeUserId)
        {
            await ApplyAssignmentAsync(ticket, assigneeUserId, communityId, "AutoAssign", cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(ticket);
    }

    public async Task<TicketDto> AssignTicketAsync(Guid ticketId, AssignTicketRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 33.4): (re)assigning a ticket needs the Assign permission on Ticket.
        // The Task 21.4 endpoint declares the same requirement; the invariant must hold regardless of
        // caller — no permission → FORBIDDEN with nothing mutated.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Assign, "Ticket", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to assign a ticket.");
        }

        if (request.AssignedToUserId == Guid.Empty)
        {
            throw new ValidationException("assignedToUserId", "An assignee is required.");
        }

        // Tenant + self-scope filtered TRACKED load so EF carries the ticket's RowVersion into the
        // UPDATE (the transition records exactly once under concurrency). An unknown/foreign id is
        // hidden by the global filter → not found.
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId, cancellationToken)
            ?? throw new NotFoundException("The ticket was not found in your authorized communities.");

        authorizationGuard.EnsureCommunityAuthorized(ticket.CommunityId);

        // The assignee must be a staff user holding Edit on Ticket in the ticket's community (RBAC
        // data, no hardcoded recipient) — otherwise a validation error, nothing mutated.
        if (!await IsEligibleAssigneeAsync(request.AssignedToUserId, ticket.CommunityId, cancellationToken))
        {
            throw new ValidationException(
                "assignedToUserId", "The selected assignee is not a maintenance-staff member for this community.");
        }

        await ApplyAssignmentAsync(ticket, request.AssignedToUserId, ticket.CommunityId, "Assign", cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(ticket);
    }

    public async Task<int> EscalateBreachedTicketsAsync(int maxTickets, CancellationToken cancellationToken = default)
    {
        // Bound the work regardless of the caller-supplied size (steering: bounded work / scalable):
        // process at most MaxSlaSweepBatch breached tickets per call so one pass can't run unbounded
        // under a large backlog. The worker loops to drain the rest across passes. Mirrors the parcel
        // custody-SLA sweep.
        var take = Math.Clamp(maxTickets, 1, MaxSlaSweepBatch);
        var now = clock.UtcNow;

        // Candidate breached tickets: assigned, past their SLA due time, not yet escalated. The
        // SlaEscalatedAtUtc == null guard is the at-most-once dedupe marker so a ticket is escalated
        // once, not every pass. TRACKED (we stamp the marker). The worker runs under a super-admin
        // context, so the global tenant filter does not hide rows; the (CommunityId, Status,
        // SlaDueAtUtc) index backs the predicate and a page cap keeps memory bounded. Resolved status
        // tickets are excluded because the sweep only looks at `assigned` (an in_progress/resolved
        // ticket is Task 20.3's concern and is not SLA-escalated here).
        var candidates = await db.Tickets
            .Where(t => t.Status == DomainTicketStatuses.Assigned
                && t.SlaDueAtUtc != null
                && t.SlaDueAtUtc < now
                && t.SlaEscalatedAtUtc == null)
            .OrderBy(t => t.SlaDueAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return 0;
        }

        var escalated = 0;
        foreach (var ticket in candidates)
        {
            var hoursOverdue = (now - ticket.SlaDueAtUtc!.Value).TotalHours;

            // Stamp the escalation marker so this ticket is never escalated twice. The ticket is
            // TRACKED, so its RowVersion rides the UPDATE: a concurrent sweep/mutation losing the
            // row-version check surfaces as a CONFLICT on SaveChanges — we treat that as "someone else
            // handled it" and skip rather than re-notify (handled by the per-ticket save below).
            ticket.SlaEscalatedAtUtc = now;

            await audit.AppendAsync(
                new AuditEntry("SlaEscalated", "Ticket", ticket.Id.ToString(), ticket.CommunityId,
                    Detail: $"hoursOverdue={hoursOverdue:F1}; priority={ticket.Priority}"),
                cancellationToken);

            // Notify the next responder (Req 33.3): staff holding Edit on Ticket in the community,
            // excluding the current assignee (the escalation goes to the NEXT responder, not back to
            // the overdue assignee). EnqueueAsync only Adds outbox rows; the per-ticket SaveChanges
            // below commits the stamp + audit + notifications together (transactional outbox).
            await EnqueueEscalationAsync(ticket, hoursOverdue, cancellationToken);

            try
            {
                // Save per ticket so one ticket's concurrency loss (another sweep/writer bumped its
                // RowVersion) is isolated — that ticket is skipped, the rest of the batch still
                // escalates. Keeps the sweep correct under concurrency without a batch-wide rollback.
                await db.SaveChangesAsync(cancellationToken);
                escalated++;
            }
            catch (ConflictException)
            {
                // Another writer updated this ticket first (e.g. a concurrent re-assignment/resolution,
                // or a parallel sweep). Our staged changes for THIS ticket are discarded on the failed
                // save; detach so the next iteration's save is clean, and skip without re-notifying.
                db.Entry(ticket).State = EntityState.Detached;
            }
        }

        return escalated;
    }

    public async Task<TicketDto> ChangeTicketStatusAsync(Guid ticketId, ChangeTicketStatusRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 34.1): driving a ticket's status is an Edit verb on Ticket (the Task
        // 21.4 endpoint gate declares the same). No permission → FORBIDDEN with nothing mutated.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "Ticket", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to change a ticket's status.");
        }

        var toStatus = RequireText(request.ToStatus, "toStatus", "A target status is required.");

        // TRACKED load so EF carries the ticket's RowVersion into the UPDATE (the transition records
        // exactly once under concurrency). The global tenant + resident self-scope filter restricts
        // this to the caller's authorized communities, so a foreign-tenant/non-own id → not found.
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId, cancellationToken)
            ?? throw new NotFoundException("The ticket was not found in your authorized communities.");

        authorizationGuard.EnsureCommunityAuthorized(ticket.CommunityId);

        // The target must be a valid, active Ticket_Status code (Req 34.1) …
        await RequireValidCodeAsync(
            toStatus, MasterDataKeys.TicketStatus, ticket.CommunityId, "toStatus", "ticket status", cancellationToken);

        // … and the from->to transition must be in the editable allow-list (Req 34.2). An illegal
        // transition (including a no-op from==to that is not seeded) → VALIDATION_ERROR, nothing
        // mutated — the SAME configurable state machine assignment uses, no hardcoded table.
        await RequireAllowedTransitionAsync(ticket.Status, toStatus, ticket.CommunityId, cancellationToken);

        var fromStatus = ticket.Status;
        var now = clock.UtcNow;
        ticket.Status = toStatus;

        // Stamp/clear the resolution instant so analytics (resolution time + SLA compliance, Req 34.5)
        // and the feedback gate have an anchor: entering `resolved` stamps now; a reopen clears it so a
        // re-resolution re-stamps a fresh instant.
        if (toStatus == DomainTicketStatuses.Resolved)
        {
            ticket.ResolvedAtUtc = now;
        }
        else if (toStatus == DomainTicketStatuses.Reopened)
        {
            ticket.ResolvedAtUtc = null;
        }

        await audit.AppendAsync(
            new AuditEntry("ChangeStatus", "Ticket", ticket.Id.ToString(), ticket.CommunityId,
                Detail: $"from={fromStatus}; to={toStatus}" + (string.IsNullOrWhiteSpace(request.Reason) ? "" : $"; reason={request.Reason!.Trim()}")),
            cancellationToken);

        // Resolved → notify the requesting resident (Req 34.3). EnqueueAsync only Adds outbox rows; the
        // single SaveChanges below commits the transition + audit + notifications atomically
        // (transactional outbox). A notification decision never breaks the status change.
        if (toStatus == DomainTicketStatuses.Resolved)
        {
            await EnqueueResolvedAsync(ticket, cancellationToken);
        }

        // RowVersion makes the transition record exactly once under concurrency: the loser's save trips
        // a DbUpdateConcurrencyException mapped centrally to CONFLICT (Req 34.2, 22.4).
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(ticket);
    }

    public async Task<TicketDto> SubmitFeedbackAsync(Guid ticketId, SubmitTicketFeedbackRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 34.4): a resident submits feedback with Create on Ticket. The Task
        // 21.4 endpoint gate declares the same. No permission → FORBIDDEN with nothing mutated.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "Ticket", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to submit ticket feedback.");
        }

        // TRACKED load tenant + resident SELF-scoped: a resident principal can only load their OWN
        // household/unit's ticket (the global resident self-scope filter), so a foreign/other-unit id
        // → not found — this IS the self-scope enforcement for Req 34.4 (no bespoke predicate). The
        // RowVersion rides the UPDATE so the feedback write is concurrency-safe.
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId, cancellationToken)
            ?? throw new NotFoundException("The ticket was not found in your authorized communities.");

        authorizationGuard.EnsureCommunityAuthorized(ticket.CommunityId);

        // Status gate (Req 34.4): feedback is only accepted once the ticket is resolved/closed — a
        // resident can't rate work still in flight. Any other status → VALIDATION_ERROR, nothing mutated.
        if (!FeedbackEligibleStatuses.Contains(ticket.Status, StringComparer.Ordinal))
        {
            throw new ValidationException(
                "status", "Feedback can only be submitted once the ticket has been resolved or closed.");
        }

        // Configurable rating scale (Req 34.4): resolve the community's [min, max] bounds via
        // Config_Service (seeded fallbacks 1..5) — never a hardcoded scale. A rating outside the bounds
        // → VALIDATION_ERROR. A mis-set max < min collapses to the min so the gate stays sane.
        var minRating = await config.GetIntAsync(ConfigKeys.TicketFeedbackMinRating, DefaultFeedbackMinRating, ticket.CommunityId, cancellationToken);
        var maxRating = await config.GetIntAsync(ConfigKeys.TicketFeedbackMaxRating, DefaultFeedbackMaxRating, ticket.CommunityId, cancellationToken);
        if (maxRating < minRating)
        {
            maxRating = minRating;
        }

        if (request.Rating < minRating || request.Rating > maxRating)
        {
            throw new ValidationException(
                "rating", $"The rating must be between {minRating} and {maxRating}.");
        }

        ticket.Rating = request.Rating;
        ticket.FeedbackComment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        ticket.FeedbackAtUtc = clock.UtcNow;

        await audit.AppendAsync(
            new AuditEntry("Feedback", "Ticket", ticket.Id.ToString(), ticket.CommunityId,
                Detail: $"rating={request.Rating}"),
            cancellationToken);

        // RowVersion makes the feedback write record exactly once under concurrency (loser → CONFLICT).
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(ticket);
    }

    public async Task<TicketAnalyticsDto> GetTicketAnalyticsAsync(Guid? communityId = null, CancellationToken cancellationToken = default)
    {
        // Defense-in-depth (Req 34.5): analytics needs View on Ticket. No permission → FORBIDDEN.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "Ticket", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view ticket analytics.");
        }

        // Base query: tenant-scoped by the global filter (a user only ever aggregates tickets in their
        // authorized communities, Req 34.5). When a community is named, confirm it is authorized and
        // narrow to it. AsNoTracking read-only. Every aggregate below runs DB-side off this query — no
        // table is materialized in memory (steering: filter/aggregate in SQL, no table scan).
        var query = db.Tickets.AsNoTracking().AsQueryable();
        if (communityId is Guid scopedCommunity)
        {
            authorizationGuard.EnsureCommunityAuthorized(scopedCommunity);
            query = query.Where(t => t.CommunityId == scopedCommunity);
        }

        // Total (DB-side COUNT).
        var total = await query.CountAsync(cancellationToken);

        // Per-status / per-category / per-priority breakdowns — three DB-side GROUP BYs projected to a
        // small key+count list each (no entities loaded), backed by the (CommunityId, Status) and
        // (CommunityId, Category, Priority) indexes.
        var byStatus = await GroupCountAsync(query, t => t.Status, cancellationToken);
        var byCategory = await GroupCountAsync(query, t => t.Category, cancellationToken);
        var byPriority = await GroupCountAsync(query, t => t.Priority, cancellationToken);

        // SLA compliance (Req 34.5): over resolved tickets with both a resolution instant and an SLA
        // due instant, how many resolved on/before the SLA (compliant) vs after (breached). Two DB-side
        // COUNTs, no in-memory scan.
        var resolvedWithSla = query.Where(t => t.ResolvedAtUtc != null && t.SlaDueAtUtc != null);
        var resolvedWithinSla = await resolvedWithSla.CountAsync(t => t.ResolvedAtUtc <= t.SlaDueAtUtc, cancellationToken);
        var resolvedBreachedSla = await resolvedWithSla.CountAsync(t => t.ResolvedAtUtc > t.SlaDueAtUtc, cancellationToken);

        // Average resolution time in hours over resolved tickets (Req 34.5). Computed DB-side as the
        // average of (ResolvedAtUtc - CreatedAtUtc) in minutes, then converted to hours — EF translates
        // EF.Functions.DateDiffMinute + Average to SQL (no row-by-row subtraction in memory). Null when
        // no ticket is resolved yet (AverageAsync over an empty set would throw, so guard with a count).
        var resolved = query.Where(t => t.ResolvedAtUtc != null);
        double? averageResolutionHours = null;
        if (await resolved.AnyAsync(cancellationToken))
        {
            var avgMinutes = await resolved
                .AverageAsync(t => (double)EF.Functions.DateDiffMinute(t.CreatedAtUtc, t.ResolvedAtUtc!.Value), cancellationToken);
            averageResolutionHours = Math.Round(avgMinutes / 60.0, 2);
        }

        return new TicketAnalyticsDto(
            total, byStatus, byCategory, byPriority, resolvedWithinSla, resolvedBreachedSla, averageResolutionHours);
    }

    public async Task<TicketDto?> GetTicketAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        // Tenant + resident self-scope filtered AsNoTracking read (Req 32.4, 5.4): a resident only
        // sees tickets for their own unit; an unknown/foreign id is hidden by the filter → null.
        var ticket = await db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == ticketId, cancellationToken);
        return ticket is null ? null : ToDto(ticket);
    }

    /// <summary>
    /// DB-side GROUP BY over the (already tenant-scoped) ticket query, projecting each group to a
    /// <see cref="TicketCountByKeyDto"/> (Req 34.5). Only the key + count cross the wire — no entity is
    /// materialized — so the aggregate stays correct and fast at scale (steering: aggregate in SQL, no
    /// table scan).
    /// </summary>
    private static async Task<IReadOnlyList<TicketCountByKeyDto>> GroupCountAsync(
        IQueryable<DomainTicket> query,
        System.Linq.Expressions.Expression<Func<DomainTicket, string>> keySelector,
        CancellationToken cancellationToken)
    {
        var grouped = await query
            .GroupBy(keySelector)
            .Select(g => new TicketCountByKeyDto(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        return grouped;
    }

    /// <summary>
    /// Enqueue the complaint-resolved notice to the requesting resident(s) (Req 34.3): the login users
    /// of the active household members on the ticket's host unit — the same unit-resident audience the
    /// parcel-received notice resolves (RBAC/household data, no hardcoded recipient). One projecting
    /// query, distinct, no N+1. EnqueueAsync only Adds the outbox rows; the caller's SaveChanges
    /// commits them in the same transaction (transactional outbox). No linked login user on the unit →
    /// nothing to enqueue; the resolution still commits.
    /// </summary>
    private async Task EnqueueResolvedAsync(DomainTicket ticket, CancellationToken cancellationToken)
    {
        var recipientUserIds = await db.HouseholdMembers.AsNoTracking()
            .Where(h => h.UnitId == ticket.UnitId && h.EndDate == null)
            .Join(db.Residents.AsNoTracking().Where(r => r.UserId != null),
                h => h.ResidentId,
                r => r.Id,
                (h, r) => r.UserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (recipientUserIds.Count == 0)
        {
            return;
        }

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ticketTitle"] = ticket.Title,
            ["category"] = ticket.Category,
        };

        foreach (var userId in recipientUserIds)
        {
            await notifications.EnqueueAsync(
                new NotificationRequest(NotificationTemplateKeys.TicketResolved, userId, ticket.CommunityId, Placeholders: placeholders),
                cancellationToken);
        }
    }

    public Task<PagedData<TicketDto>> ListTicketsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // Tenant + resident self-scope filtered by the global query filter (Req 32.4): a resident
        // principal only sees tickets for their own household/unit, a staff View principal sees all
        // tickets in their authorized communities. The allow-listed query config fixes the
        // sort/filter/search surface; AsNoTracking projection, page size clamped by the shared policy
        // — mirrors ListParcelsAsync.
        return db.Tickets.AsNoTracking().ToPagedResultAsync(
            parameters, TicketQueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
    }

    public async Task<IReadOnlyList<TicketAttachmentDto>> GetTicketAttachmentsAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        // Defense-in-depth (Req 32.3): listing attachments needs View on TicketAttachment.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "TicketAttachment", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view ticket attachments.");
        }

        // Tenant + resident self-scope filtered AsNoTracking read riding the (CommunityId, TicketId)
        // index — a foreign-tenant / non-own-unit ticket's attachments are hidden by the filter, so an
        // unknown/foreign id yields an empty list (no leak, Req 32.4). Ordered by creation then id as a
        // stable tie-break.
        var attachments = await db.TicketAttachments.AsNoTracking()
            .Where(a => a.TicketId == ticketId)
            .OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

        return attachments.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<TicketCommentDto>> GetTicketCommentsAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        // Defense-in-depth: listing comments needs View on TicketComment.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "TicketComment", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view ticket comments.");
        }

        // Tenant + resident self-scope filtered AsNoTracking read riding the
        // (CommunityId, TicketId, CreatedAtUtc) index — a foreign-tenant / non-own-unit ticket's
        // comments are hidden by the filter, so an unknown/foreign id yields an empty list (no leak).
        // Oldest-first so the thread reads as a conversation; id tie-break for stability.
        var comments = await db.TicketComments.AsNoTracking()
            .Where(c => c.TicketId == ticketId)
            .OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

        return comments.Select(ToDto).ToList();
    }

    public async Task<TicketCommentDto> AddTicketCommentAsync(
        Guid ticketId, AddTicketCommentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth: posting a comment needs Create on TicketComment.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "TicketComment", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to comment on a ticket.");
        }

        var body = RequireText(request.Body, "body", "A comment is required.");
        if (body.Length > 2000)
        {
            throw new ValidationException("body", "A comment can be at most 2000 characters.");
        }

        // Load the parent ticket tenant + resident-self scoped: anchors the comment in the ticket's
        // community + unit AND enforces "comment only on your own unit's ticket" (a foreign/other-unit
        // id is hidden by the filter → not found; no bespoke predicate).
        var ticket = await db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == ticketId, cancellationToken)
            ?? throw new NotFoundException("The ticket was not found in your authorized communities.");

        authorizationGuard.EnsureCommunityAuthorized(ticket.CommunityId);

        var comment = new DomainTicketComment
        {
            CommunityId = ticket.CommunityId,
            UnitId = ticket.UnitId, // mirror the ticket's unit so resident self-scope restricts reads
            TicketId = ticket.Id,
            Body = body,
        };
        db.TicketComments.Add(comment);

        await audit.AppendAsync(
            new AuditEntry("Create", "TicketComment", comment.Id.ToString(), ticket.CommunityId, Detail: null),
            cancellationToken);

        // Single SaveChanges commits the comment + audit atomically. CreatedBy/CreatedAtUtc are stamped
        // by the audit SaveChanges interceptor, so the returned DTO re-reads them off the tracked entity.
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(comment);
    }

    /// <summary>
    /// Resolve the auto-assignment target for a new ticket's category (Req 33.1) from the editable
    /// <c>Ticket_Assignment_Rule</c> list — each entry's code is a <c>category-&gt;userId</c> pair
    /// (no hardcoded category→assignee table). When a rule matches and names a user who is still an
    /// eligible staff assignee, that user is returned; otherwise the configured default assignee
    /// (<c>ticket.defaultAssigneeUserId</c>) is tried (also validated); when neither resolves the
    /// ticket is left unassigned (null) — a deliberate, documented choice so a mis-configured
    /// community never mis-routes a ticket. The eligibility check keeps the rule honest: a rule naming
    /// a user who has lost the staff grant (or is in another community) is ignored, not blindly
    /// applied.
    /// </summary>
    private async Task<Guid?> ResolveAutoAssigneeAsync(string category, Guid communityId, CancellationToken cancellationToken)
    {
        // Rules are category->userId pairs. Find the one for this category (community-resolved master
        // data, platform fallback) in a single cached read.
        var rules = await config.GetMasterDataAsync(MasterDataKeys.TicketAssignmentRule, communityId, cancellationToken: cancellationToken);
        var prefix = $"{category}->";
        var ruleUserId = rules
            .Select(e => e.Code)
            .Where(code => code.StartsWith(prefix, StringComparison.Ordinal))
            .Select(code => code[prefix.Length..])
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(ruleUserId)
            && Guid.TryParse(ruleUserId, out var ruleAssignee)
            && await IsEligibleAssigneeAsync(ruleAssignee, communityId, cancellationToken))
        {
            return ruleAssignee;
        }

        // No matching rule (or the rule named an ineligible user): fall back to the configured default
        // assignee, when set AND still eligible. Unset/ineligible → leave unassigned.
        var defaultAssignee = await config.GetStringAsync(ConfigKeys.TicketDefaultAssigneeUserId, string.Empty, communityId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(defaultAssignee)
            && Guid.TryParse(defaultAssignee, out var defaultUserId)
            && await IsEligibleAssigneeAsync(defaultUserId, communityId, cancellationToken))
        {
            return defaultUserId;
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="userId"/> is an eligible ticket assignee in <paramref name="communityId"/>
    /// (Req 33.1, 33.4): a user whose (community-scoped or platform-wide) roles grant <c>Edit</c> on
    /// <c>Ticket</c> in that community — RBAC data, no hardcoded role name (mirrors the incident-
    /// responder resolution). Permission/Role/UserRole are platform-scoped (no tenant filter), so the
    /// community predicate on the UserRole scope is explicit. One projecting existence query, no N+1.
    /// </summary>
    private Task<bool> IsEligibleAssigneeAsync(Guid userId, Guid communityId, CancellationToken cancellationToken) =>
        (from ur in db.UserRoles.AsNoTracking()
         where ur.UserId == userId && (ur.CommunityId == communityId || ur.CommunityId == null)
         join rp in db.RolePermissions.AsNoTracking() on ur.RoleId equals rp.RoleId
         join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
         where p.ResourceType == "Ticket" && p.Action == PermissionAction.Edit
         select ur.UserId)
        .AnyAsync(cancellationToken);

    /// <summary>
    /// Apply an assignment / re-assignment to a TRACKED <paramref name="ticket"/> (Req 33.1, 33.2,
    /// 33.4, 33.5): move its status to <c>assigned</c> through the editable
    /// <c>Ticket_Status_Transition</c> allow-list (an illegal transition → VALIDATION_ERROR, nothing
    /// mutated — no hardcoded table), stamp the assignee + assignment time, (re)start the per-priority
    /// SLA timer (clearing any prior escalation stamp so the new assignee gets a fresh timer), record
    /// the (re)assignment + acting user via the audit trail (Req 33.4), and enqueue the complaint-
    /// assigned push to the assignee (Req 33.5, 64.3). Only stages the changes on the shared context —
    /// the caller's SaveChanges commits them atomically with the rest of the operation.
    /// </summary>
    private async Task ApplyAssignmentAsync(DomainTicket ticket, Guid assigneeUserId, Guid communityId, string auditAction, CancellationToken cancellationToken)
    {
        // Status change goes through the SAME configurable state machine as every other transition
        // (Req 34.1): current -> assigned must be in the editable allow-list. new->assigned (first
        // assignment) and assigned->assigned (re-assignment) are both seeded; a community that removes
        // an edge makes the corresponding (re)assignment impossible without a code change.
        await RequireAllowedTransitionAsync(ticket.Status, DomainTicketStatuses.Assigned, communityId, cancellationToken);

        var now = clock.UtcNow;
        var slaHours = await ResolveSlaHoursAsync(ticket.Priority, communityId, cancellationToken);

        ticket.AssignedToUserId = assigneeUserId;
        ticket.AssignedAtUtc = now;
        ticket.SlaDueAtUtc = now.AddHours(slaHours);
        ticket.SlaEscalatedAtUtc = null; // a (re)assignment restarts the SLA → fresh escalation window
        ticket.Status = DomainTicketStatuses.Assigned;

        await audit.AppendAsync(
            new AuditEntry(auditAction, "Ticket", ticket.Id.ToString(), communityId,
                Detail: $"assignedTo={assigneeUserId}; slaHours={slaHours}"),
            cancellationToken);

        await EnqueueAssignedAsync(ticket, assigneeUserId, communityId, cancellationToken);
    }

    /// <summary>
    /// Require that the <paramref name="from"/>-&gt;<paramref name="to"/> transition is present (and
    /// active) in the editable <c>Ticket_Status_Transition</c> allow-list for the community (Req 34.1).
    /// Each entry's code is a <c>from-&gt;to</c> pair of <c>Ticket_Status</c> codes; a transition not
    /// in the list is rejected with a validation error — no hardcoded transition table. Mirrors the
    /// parcel state machine's transition check.
    /// </summary>
    private async Task RequireAllowedTransitionAsync(string from, string to, Guid communityId, CancellationToken cancellationToken)
    {
        var pair = $"{from}->{to}";
        var allowed = await config.GetMasterDataAsync(MasterDataKeys.TicketStatusTransition, communityId, cancellationToken: cancellationToken);
        if (!allowed.Any(e => string.Equals(e.Code, pair, StringComparison.Ordinal)))
        {
            throw new ValidationException(
                "status", $"A ticket cannot move from '{from}' to '{to}'; the transition is not allowed.");
        }
    }

    /// <summary>
    /// Resolve the SLA duration (hours) for a ticket's priority (Req 33.2): the per-priority config
    /// value <c>ticket.sla.hours.{priority}</c> (community override, platform fallback), falling back
    /// to the seeded per-priority default (then <see cref="DefaultSlaHours"/> for an unlisted
    /// priority) when unset — never a hardcoded hours table. A non-positive configured value is
    /// treated as the default so a mis-set key can't produce an already-breached ticket.
    /// </summary>
    private async Task<int> ResolveSlaHoursAsync(string priority, Guid communityId, CancellationToken cancellationToken)
    {
        var fallback = DefaultSlaHoursByPriority.TryGetValue(priority, out var seeded) ? seeded : DefaultSlaHours;
        var hours = await config.GetIntAsync(ConfigKeys.TicketSlaHoursKey(priority), fallback, communityId, cancellationToken);
        return hours > 0 ? hours : fallback;
    }

    /// <summary>
    /// Enqueue the complaint-assigned push to the ticket's assignee (Req 33.5, 64.3). The template
    /// declares the push channel only (Req 64.3). EnqueueAsync only Adds the outbox row; the caller's
    /// SaveChanges commits it in the same transaction (transactional outbox).
    /// </summary>
    private async Task EnqueueAssignedAsync(DomainTicket ticket, Guid assigneeUserId, Guid communityId, CancellationToken cancellationToken)
    {
        await notifications.EnqueueAsync(
            new NotificationRequest(NotificationTemplateKeys.TicketAssigned, assigneeUserId, communityId,
                Placeholders: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ticketTitle"] = ticket.Title,
                    ["category"] = ticket.Category,
                    ["priority"] = ticket.Priority,
                }),
            cancellationToken);
    }

    /// <summary>
    /// Enqueue the SLA-breach escalation notice to the NEXT responder(s) (Req 33.3): the staff users
    /// whose (community-scoped or platform-wide) roles grant <c>Edit</c> on <c>Ticket</c> in the
    /// community — RBAC data, no hardcoded recipients (mirrors the incident-responder audience) —
    /// EXCLUDING the current (overdue) assignee so the escalation goes to the next responder, not back
    /// to the assignee who missed the SLA. One projecting query, distinct, no N+1. The template
    /// declares push + email. EnqueueAsync only Adds the outbox rows; the caller's SaveChanges commits
    /// them in the same transaction (transactional outbox).
    /// </summary>
    private async Task EnqueueEscalationAsync(DomainTicket ticket, double hoursOverdue, CancellationToken cancellationToken)
    {
        var responderUserIds = await (
            from ur in db.UserRoles.AsNoTracking()
            where ur.CommunityId == ticket.CommunityId || ur.CommunityId == null
            join rp in db.RolePermissions.AsNoTracking() on ur.RoleId equals rp.RoleId
            join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            where p.ResourceType == "Ticket" && p.Action == PermissionAction.Edit
            select ur.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ticketTitle"] = ticket.Title,
            ["category"] = ticket.Category,
            ["priority"] = ticket.Priority,
            ["hoursOverdue"] = ((int)hoursOverdue).ToString(),
        };

        foreach (var userId in responderUserIds.Where(id => id != ticket.AssignedToUserId))
        {
            await notifications.EnqueueAsync(
                new NotificationRequest(NotificationTemplateKeys.TicketEscalated, userId, ticket.CommunityId, Placeholders: placeholders),
                cancellationToken);
        }
    }

    /// <summary>
    /// Add one <see cref="DomainTicketAttachment"/> per supplied File_Service reference (Req 32.3),
    /// associated with the ticket and its community. Requires <c>Create</c> on <c>TicketAttachment</c>
    /// when any attachment is supplied (defense-in-depth). Each referenced <c>StoredFile</c> must exist
    /// in the ticket's community (tenant-scoped existence check — the global filter already hides
    /// foreign-tenant files), so a reference to a file outside the community is rejected before the
    /// ticket is created. Only Adds the rows; the caller's SaveChanges commits them with the ticket.
    /// </summary>
    private async Task AddAttachmentsAsync(
        DomainTicket ticket, IReadOnlyList<Guid>? attachmentFileIds, Guid communityId, CancellationToken cancellationToken)
    {
        if (attachmentFileIds is null || attachmentFileIds.Count == 0)
        {
            return;
        }

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "TicketAttachment", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to attach files to a ticket.");
        }

        // De-dupe the supplied references so the same file is not attached twice.
        var fileIds = attachmentFileIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (fileIds.Count == 0)
        {
            return;
        }

        // Validate every referenced file exists in the ticket's community in ONE indexed read (no
        // N+1): the global tenant filter already scopes StoredFiles to authorized communities, so an
        // explicit CommunityId predicate rejects a cross-community reference. Any missing/foreign id →
        // VALIDATION_ERROR before the ticket is created.
        var validFileIds = await db.StoredFiles.AsNoTracking()
            .Where(f => fileIds.Contains(f.Id) && f.CommunityId == communityId)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);

        var missing = fileIds.Except(validFileIds).ToList();
        if (missing.Count > 0)
        {
            throw new ValidationException(
                "attachmentFileIds", "One or more attachment file references were not found in the ticket's community.");
        }

        foreach (var fileId in fileIds)
        {
            db.TicketAttachments.Add(new DomainTicketAttachment
            {
                CommunityId = communityId,
                UnitId = ticket.UnitId, // the parent ticket's host unit (IUnitScoped backing column, Req 32.4)
                TicketId = ticket.Id,
                FileId = fileId,
            });
        }
    }

    /// <summary>
    /// Resolve the host unit (tenant + resident-self scoped) and return its community id, rejecting an
    /// unknown / foreign-tenant / (for a resident) non-own unit. The returned community anchors the
    /// ticket so it is always owned by the same community as its host unit (same-community by
    /// construction, Req 32.1).
    /// </summary>
    private async Task<Guid> RequireHostUnitCommunityAsync(Guid hostUnitId, CancellationToken cancellationToken)
    {
        var communityId = await db.Units.AsNoTracking()
            .Where(u => u.Id == hostUnitId)
            .Select(u => (Guid?)u.CommunityId)
            .FirstOrDefaultAsync(cancellationToken);

        return communityId ?? throw new ValidationException(
            "hostUnitId", "The host unit was not found in your authorized communities.");
    }

    /// <summary>
    /// Validate a code against a configurable master-data list resolved for the community (Req 32.1,
    /// 32.2) — not a frozen enum, so the classification vocabulary stays editable per community.
    /// Returns the trimmed code on success; an unknown/retired code → VALIDATION_ERROR.
    /// </summary>
    private async Task<string> RequireValidCodeAsync(
        string? value, string listKey, Guid communityId, string field, string what, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(field, $"A {what} is required.");
        }

        var code = value.Trim();
        var allowed = await config.GetMasterDataAsync(listKey, communityId, cancellationToken: cancellationToken);
        if (!allowed.Any(e => string.Equals(e.Code, code, StringComparison.Ordinal)))
        {
            throw new ValidationException(field, $"'{code}' is not a valid {what}.");
        }

        return code;
    }

    private static string RequireText(string? value, string field, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(field, message);
        }

        return value.Trim();
    }

    private static TicketDto ToDto(DomainTicket t) =>
        new(t.Id, t.CommunityId, t.HostUnitId, t.RaisedByResidentId, t.Title, t.Description,
            t.Category, t.Subcategory, t.Priority, t.Status,
            t.AssignedToUserId, t.AssignedAtUtc, t.SlaDueAtUtc, t.SlaEscalatedAtUtc,
            t.Rating, t.FeedbackComment, t.FeedbackAtUtc, t.CreatedAtUtc);

    private static TicketAttachmentDto ToDto(DomainTicketAttachment a) =>
        new(a.Id, a.CommunityId, a.TicketId, a.FileId);

    private static TicketCommentDto ToDto(DomainTicketComment c) =>
        new(c.Id, c.CommunityId, c.TicketId, c.Body, c.CreatedBy, c.CreatedAtUtc);
}
