using UrbanNest.Application.Abstractions;
using UrbanNest.Application.Abstractions.Security;
using UrbanNest.Application.Audit;
using UrbanNest.Application.Config;
using UrbanNest.Application.Exceptions;
using UrbanNest.Application.Gate;
using UrbanNest.Application.Notifications;
using UrbanNest.Application.Querying;
using UrbanNest.Contracts.Envelope;
using UrbanNest.Domain.Gate;
using UrbanNest.Domain.Identity;
using UrbanNest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace UrbanNest.Infrastructure.Gate;

/// <summary>
/// EF-backed <see cref="ISecurityOpsService"/> (Task 16.3, Req 26.1–26.5, 27.1–27.5).
///
/// <para><b>Watchlist (Req 26.1, 26.3, 26.5).</b> Add/clear entries gated on <c>Create</c> on
/// <c>WatchlistEntry</c> (read is <c>View</c>-gated at the endpoint, Req 26.3). The subject
/// identifier is normalized (trim + upper-invariant) on store and match so a presented subject hits
/// the <c>(CommunityId, SubjectIdentifier)</c> index directly — a single indexed equality lookup, no
/// scan. <see cref="MatchWatchlistAsync"/> returns the active matches the gate flow flags; admitting
/// a flagged subject then requires an <c>Approve</c> override (enforced in <c>GateService</c>).</para>
///
/// <para><b>Incidents (Req 27.1, 27.2).</b> Report validates category/severity against the editable
/// lists and stores <c>open</c>. The severity RANK is the <c>Incident_Severity</c> list's SortOrder
/// (not a compiled rank); when it meets the configurable threshold
/// (<c>gate.incident.escalationThreshold</c>) the incident is marked escalated and the responders —
/// resolved as the users whose roles grant <c>View</c> on <c>Incident</c> in that community (RBAC
/// data, no hardcoded recipients) — are notified via Notification_Service in the SAME transaction
/// (EnqueueAsync only Adds the outbox row; the single SaveChanges commits atomically). Acknowledge/
/// resolve are gated on <c>Edit</c> and are concurrency-safe under the incident <c>RowVersion</c>.</para>
///
/// <para><b>SOS (Req 27.3, 27.4).</b> Raise stores <c>active</c> and notifies the configured
/// community audience — the users whose roles grant <c>View</c> on <c>SosAlert</c> in that community —
/// with the MANDATORY emergency template (always delivered regardless of opt-out, Req 20.4), in the
/// same transaction. It stays active until an <c>Edit</c>-permission user acknowledges and resolves
/// it; both transitions are concurrency-safe under the alert <c>RowVersion</c>.</para>
///
/// <para><b>Access integration (Req 27.5).</b> <see cref="TriggerAccessIntegrationAsync"/> validates
/// the action, calls the <see cref="IAccessControlIntegration"/> seam (no-op/logging default; real
/// drivers are Phase 12 / Task 40) and RECORDS the triggered event with the seam's outcome — never
/// failing the operation on an unreachable device.</para>
/// </summary>
public sealed class SecurityOpsService(
    AppDbContext db,
    IAuditService audit,
    IConfigService config,
    IClock clock,
    IAuthorizationGuard authorizationGuard,
    INotificationService notifications,
    IAccessControlIntegration accessControl,
    IPageSizePolicy pageSizePolicy,
    IAsyncQueryExecutor executor)
    : ISecurityOpsService
{
    // Escalation threshold default (severity rank) when unset in Config_Service (no hardcoding of the
    // decision — only a safe fallback). Ranks are the Incident_Severity list's SortOrder (0-based):
    // low=0, medium=1, high=2, critical=3 — so the default 2 auto-escalates high + critical.
    private const int DefaultEscalationThreshold = 2;

    // Allow-lists for the Task 16.4 security-ops list endpoints (Req 26.2, 26.3, 27.x). Only these
    // fields reach the query — no arbitrary client LINQ (steering: no hardcoding, injection-safe).
    private static readonly ListQueryConfiguration<WatchlistEntry> WatchlistQueryConfig =
        new ListQueryConfiguration<WatchlistEntry>()
            .WithDefaultSort(e => e.SubjectIdentifier)
            .Sortable("subjectIdentifier", e => e.SubjectIdentifier)
            .Sortable("status", e => e.Status)
            .Sortable("severity", e => e.Severity)
            .Filterable("status", e => e.Status)
            .Filterable("subjectType", e => e.SubjectType)
            .Filterable("severity", e => e.Severity)
            .Searchable(e => e.SubjectIdentifier);

    private static readonly ListQueryConfiguration<GuardShift> GuardShiftQueryConfig =
        new ListQueryConfiguration<GuardShift>()
            // Chronological roster riding the (CommunityId, ShiftStartUtc) index.
            .WithDefaultSort(s => s.ShiftStartUtc, SortDirection.Descending)
            .Sortable("shiftStartUtc", s => s.ShiftStartUtc)
            .Sortable("shiftEndUtc", s => s.ShiftEndUtc)
            .Sortable("status", s => s.Status)
            .Filterable("status", s => s.Status)
            .Filterable("guardUserId", s => s.GuardUserId);

    private static readonly ListQueryConfiguration<Incident> IncidentQueryConfig =
        new ListQueryConfiguration<Incident>()
            .WithDefaultSort(i => i.CreatedAtUtc, SortDirection.Descending)
            .Sortable("createdAtUtc", i => i.CreatedAtUtc)
            .Sortable("status", i => i.Status)
            .Sortable("severity", i => i.Severity)
            .Filterable("status", i => i.Status)
            .Filterable("severity", i => i.Severity)
            .Filterable("category", i => i.Category)
            .Filterable("escalationState", i => i.EscalationState);

    private static readonly ListQueryConfiguration<SosAlert> SosAlertQueryConfig =
        new ListQueryConfiguration<SosAlert>()
            .WithDefaultSort(a => a.RaisedAtUtc, SortDirection.Descending)
            .Sortable("raisedAtUtc", a => a.RaisedAtUtc)
            .Sortable("status", a => a.Status)
            .Filterable("status", a => a.Status);

    private static readonly ListQueryConfiguration<AccessIntegrationEvent> AccessIntegrationQueryConfig =
        new ListQueryConfiguration<AccessIntegrationEvent>()
            // Chronological event log riding the (CommunityId, CreatedAtUtc) index.
            .WithDefaultSort(e => e.CreatedAtUtc, SortDirection.Descending)
            .Sortable("createdAtUtc", e => e.CreatedAtUtc)
            .Sortable("action", e => e.Action)
            .Sortable("status", e => e.Status)
            .Filterable("action", e => e.Action)
            .Filterable("status", e => e.Status);

    // =============================================================================================
    // Watchlist (Req 26.1, 26.3, 26.5)
    // =============================================================================================

    public async Task<WatchlistEntryDto> AddWatchlistEntryAsync(AddWatchlistEntryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await RequirePermissionAsync(PermissionAction.Create, "WatchlistEntry", "add a watchlist entry", cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(request.CommunityId);

        var subjectType = RequireText(request.SubjectType, "subjectType", "A subject type is required.");
        var identifier = RequireText(request.SubjectIdentifier, "subjectIdentifier", "A subject identifier is required.");
        var reason = RequireText(request.Reason, "reason", "A reason is required.");
        var severity = RequireText(request.Severity, "severity", "A severity is required.");

        await RequireValidCodeAsync(MasterDataKeys.WatchlistSubjectType, subjectType, "subjectType", request.CommunityId, cancellationToken);
        await RequireValidCodeAsync(MasterDataKeys.WatchlistSeverity, severity, "severity", request.CommunityId, cancellationToken);

        var entry = new WatchlistEntry
        {
            CommunityId = request.CommunityId,
            SubjectType = subjectType,
            SubjectIdentifier = Normalize(identifier),
            Reason = reason,
            Severity = severity,
            Status = WatchlistStatuses.Active,
        };

        db.WatchlistEntries.Add(entry);

        await audit.AppendAsync(
            new AuditEntry("Create", "WatchlistEntry", entry.Id.ToString(), entry.CommunityId, Detail: null),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(entry);
    }

    public async Task<WatchlistEntryDto> ClearWatchlistEntryAsync(Guid watchlistEntryId, CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(PermissionAction.Create, "WatchlistEntry", "clear a watchlist entry", cancellationToken);

        // TRACKED load: we mutate the row and need the RowVersion carried into the UPDATE. The global
        // tenant filter restricts this to the caller's authorized communities (a foreign-tenant id →
        // not found).
        var entry = await db.WatchlistEntries.FirstOrDefaultAsync(e => e.Id == watchlistEntryId, cancellationToken)
            ?? throw new NotFoundException("Watchlist entry not found.");

        authorizationGuard.EnsureCommunityAuthorized(entry.CommunityId);

        if (!entry.IsActive)
        {
            throw new ConflictException(
                $"The watchlist entry is '{entry.Status}', not 'active'; it cannot be cleared again.");
        }

        entry.Status = WatchlistStatuses.Cleared;

        await audit.AppendAsync(
            new AuditEntry("Clear", "WatchlistEntry", entry.Id.ToString(), entry.CommunityId, Detail: null),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(entry);
    }

    public async Task<IReadOnlyList<WatchlistEntryDto>> MatchWatchlistAsync(Guid communityId, string subjectIdentifier, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subjectIdentifier))
        {
            return [];
        }

        var normalized = Normalize(subjectIdentifier);

        // Single indexed equality lookup on (CommunityId, SubjectIdentifier) over ACTIVE entries only
        // (Req 26.5 — a cleared entry no longer flags). AsNoTracking read; the global tenant filter
        // keeps it scoped to the caller's authorized communities.
        var matches = await db.WatchlistEntries.AsNoTracking()
            .Where(e => e.CommunityId == communityId
                && e.SubjectIdentifier == normalized
                && e.Status == WatchlistStatuses.Active)
            .ToListAsync(cancellationToken);

        return matches.Select(ToDto).ToList();
    }

    // =============================================================================================
    // Guard shifts (Req 26.4)
    // =============================================================================================

    public async Task<GuardShiftDto> ScheduleGuardShiftAsync(ScheduleGuardShiftRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await RequirePermissionAsync(PermissionAction.Create, "GuardShift", "schedule a guard shift", cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(request.CommunityId);

        if (request.GuardUserId == Guid.Empty)
        {
            throw new ValidationException("guardUserId", "A guard user is required.");
        }

        if (request.ShiftEndUtc <= request.ShiftStartUtc)
        {
            throw new ValidationException("shiftEndUtc", "The shift end must be after the shift start.");
        }

        var shift = new GuardShift
        {
            CommunityId = request.CommunityId,
            GuardUserId = request.GuardUserId,
            GateId = request.GateId,
            GateLabel = string.IsNullOrWhiteSpace(request.GateLabel) ? null : request.GateLabel.Trim(),
            ShiftStartUtc = request.ShiftStartUtc,
            ShiftEndUtc = request.ShiftEndUtc,
            Status = GuardShiftStatuses.Scheduled,
        };

        db.GuardShifts.Add(shift);

        await audit.AppendAsync(
            new AuditEntry("Create", "GuardShift", shift.Id.ToString(), shift.CommunityId, Detail: null),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(shift);
    }

    // =============================================================================================
    // Incidents (Req 27.1, 27.2)
    // =============================================================================================

    public async Task<IncidentDto> ReportIncidentAsync(ReportIncidentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await RequirePermissionAsync(PermissionAction.Create, "Incident", "report an incident", cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(request.CommunityId);

        if (request.ReportedByUserId == Guid.Empty)
        {
            throw new ValidationException("reportedByUserId", "A reporting user is required.");
        }

        var category = RequireText(request.Category, "category", "A category is required.");
        var severity = RequireText(request.Severity, "severity", "A severity is required.");
        var description = RequireText(request.Description, "description", "A description is required.");

        await RequireValidCodeAsync(MasterDataKeys.IncidentCategory, category, "category", request.CommunityId, cancellationToken);
        var severityRank = await ResolveSeverityRankAsync(severity, request.CommunityId, cancellationToken);

        var threshold = await config.GetIntAsync(
            ConfigKeys.IncidentEscalationThreshold, DefaultEscalationThreshold, request.CommunityId, cancellationToken);
        var escalate = severityRank >= threshold;

        var incident = new Incident
        {
            CommunityId = request.CommunityId,
            ReportedByUserId = request.ReportedByUserId,
            GateId = request.GateId,
            Location = string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim(),
            Category = category,
            Severity = severity,
            Description = description,
            Status = escalate ? IncidentStatuses.Escalated : IncidentStatuses.Open,
            EscalationState = escalate ? IncidentEscalationStates.Escalated : IncidentEscalationStates.None,
        };

        db.Incidents.Add(incident);

        await audit.AppendAsync(
            new AuditEntry("Create", "Incident", incident.Id.ToString(), incident.CommunityId,
                Detail: escalate ? "escalated" : null),
            cancellationToken);

        // Escalation notification (Req 27.2): notify the responders in the SAME transaction. The
        // enqueue only Adds outbox rows; the single SaveChanges below commits the incident + the
        // notifications atomically (transactional outbox).
        if (escalate)
        {
            await NotifyResponderAudienceAsync(
                incident.CommunityId,
                "Incident",
                NotificationTemplateKeys.IncidentEscalated,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["category"] = incident.Category,
                    ["severity"] = incident.Severity,
                    ["location"] = incident.Location ?? string.Empty,
                },
                cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(incident);
    }

    public Task<IncidentDto> AcknowledgeIncidentAsync(Guid incidentId, Guid acknowledgingUserId, CancellationToken cancellationToken = default) =>
        TransitionIncidentAsync(
            incidentId,
            acknowledgingUserId,
            toStatus: IncidentStatuses.Acknowledged,
            auditAction: "Acknowledge",
            isAcknowledge: true,
            cancellationToken);

    public Task<IncidentDto> ResolveIncidentAsync(Guid incidentId, Guid resolvingUserId, CancellationToken cancellationToken = default) =>
        TransitionIncidentAsync(
            incidentId,
            resolvingUserId,
            toStatus: IncidentStatuses.Resolved,
            auditAction: "Resolve",
            isAcknowledge: false,
            cancellationToken);

    private async Task<IncidentDto> TransitionIncidentAsync(
        Guid incidentId,
        Guid actingUserId,
        string toStatus,
        string auditAction,
        bool isAcknowledge,
        CancellationToken cancellationToken)
    {
        await RequirePermissionAsync(PermissionAction.Edit, "Incident", "update an incident", cancellationToken);

        if (actingUserId == Guid.Empty)
        {
            throw new ValidationException("userId", "An acting user is required.");
        }

        var incident = await db.Incidents.FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken)
            ?? throw new NotFoundException("Incident not found.");

        authorizationGuard.EnsureCommunityAuthorized(incident.CommunityId);

        // A resolved incident is terminal — neither acknowledge nor resolve applies (Req 27.1). This
        // conflict is also what a concurrency loser sees after the winner resolved.
        if (incident.IsResolved)
        {
            throw new ConflictException("The incident is already resolved; no further transition is valid.");
        }

        var now = clock.UtcNow;
        if (isAcknowledge)
        {
            incident.AcknowledgedByUserId = actingUserId;
            incident.AcknowledgedAtUtc = now;
            // Acknowledging an escalated incident keeps its escalated status visible; otherwise move
            // to acknowledged. EscalationState is independent and untouched.
            if (!incident.IsEscalated)
            {
                incident.Status = toStatus;
            }
            else
            {
                incident.Status = IncidentStatuses.Acknowledged;
            }
        }
        else
        {
            incident.ResolvedByUserId = actingUserId;
            incident.ResolvedAtUtc = now;
            incident.Status = toStatus;
        }

        await audit.AppendAsync(
            new AuditEntry(auditAction, "Incident", incident.Id.ToString(), incident.CommunityId, Detail: null),
            cancellationToken);

        // RowVersion makes the transition record exactly once under concurrency (loser → CONFLICT).
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(incident);
    }

    // =============================================================================================
    // SOS (Req 27.3, 27.4)
    // =============================================================================================

    public async Task<SosAlertDto> RaiseSosAsync(RaiseSosRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await RequirePermissionAsync(PermissionAction.Create, "SosAlert", "raise an SOS alert", cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(request.CommunityId);

        if (request.RaisedByUserId == Guid.Empty)
        {
            throw new ValidationException("raisedByUserId", "A raising user is required.");
        }

        var alert = new SosAlert
        {
            CommunityId = request.CommunityId,
            RaisedByUserId = request.RaisedByUserId,
            UnitId = request.UnitId,
            Location = string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim(),
            Status = SosAlertStatuses.Active,
            RaisedAtUtc = clock.UtcNow,
        };

        db.SosAlerts.Add(alert);

        await audit.AppendAsync(
            new AuditEntry("Create", "SosAlert", alert.Id.ToString(), alert.CommunityId, Detail: null),
            cancellationToken);

        // MANDATORY emergency notification to the configured audience (Req 27.3). The emergency
        // template is classified Mandatory, so Notification_Service always delivers it regardless of
        // a recipient's opt-out (Req 20.4). Enqueued in the same transaction (transactional outbox).
        await NotifyResponderAudienceAsync(
            alert.CommunityId,
            "SosAlert",
            NotificationTemplateKeys.SosRaised,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["location"] = alert.Location ?? string.Empty,
            },
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(alert);
    }

    public Task<SosAlertDto> AcknowledgeSosAsync(Guid sosAlertId, Guid acknowledgingUserId, CancellationToken cancellationToken = default) =>
        TransitionSosAsync(
            sosAlertId,
            acknowledgingUserId,
            toStatus: SosAlertStatuses.Acknowledged,
            auditAction: "Acknowledge",
            isResolve: false,
            cancellationToken);

    public Task<SosAlertDto> ResolveSosAsync(Guid sosAlertId, Guid resolvingUserId, CancellationToken cancellationToken = default) =>
        TransitionSosAsync(
            sosAlertId,
            resolvingUserId,
            toStatus: SosAlertStatuses.Resolved,
            auditAction: "Resolve",
            isResolve: true,
            cancellationToken);

    private async Task<SosAlertDto> TransitionSosAsync(
        Guid sosAlertId,
        Guid actingUserId,
        string toStatus,
        string auditAction,
        bool isResolve,
        CancellationToken cancellationToken)
    {
        // The SOS stays active until an Edit-permission user acknowledges + resolves it (Req 27.4).
        await RequirePermissionAsync(PermissionAction.Edit, "SosAlert", "update an SOS alert", cancellationToken);

        if (actingUserId == Guid.Empty)
        {
            throw new ValidationException("userId", "An acting user is required.");
        }

        var alert = await db.SosAlerts.FirstOrDefaultAsync(a => a.Id == sosAlertId, cancellationToken)
            ?? throw new NotFoundException("SOS alert not found.");

        authorizationGuard.EnsureCommunityAuthorized(alert.CommunityId);

        // A resolved SOS is terminal (Req 27.4). This conflict is also what a concurrency loser sees.
        if (!alert.IsActive)
        {
            throw new ConflictException("The SOS alert is already resolved; no further transition is valid.");
        }

        var now = clock.UtcNow;
        if (isResolve)
        {
            alert.ResolvedByUserId = actingUserId;
            alert.ResolvedAtUtc = now;
            // Record acknowledgement too if resolving straight from active (resolve implies ack).
            alert.AcknowledgedByUserId ??= actingUserId;
            alert.AcknowledgedAtUtc ??= now;
            alert.Status = toStatus;
        }
        else
        {
            alert.AcknowledgedByUserId = actingUserId;
            alert.AcknowledgedAtUtc = now;
            alert.Status = toStatus;
        }

        await audit.AppendAsync(
            new AuditEntry(auditAction, "SosAlert", alert.Id.ToString(), alert.CommunityId, Detail: null),
            cancellationToken);

        // RowVersion makes the transition record exactly once under concurrency (loser → CONFLICT).
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(alert);
    }

    // =============================================================================================
    // Access-control integration (Req 27.5)
    // =============================================================================================

    public async Task<AccessIntegrationEventDto> TriggerAccessIntegrationAsync(TriggerAccessIntegrationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Gated on the guard-facing gate-operations grant (Create on Incident) — the same staff who
        // report incidents operate the gate hardware.
        await RequirePermissionAsync(PermissionAction.Create, "Incident", "trigger an access-control integration", cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(request.CommunityId);

        var action = RequireText(request.Action, "action", "An integration action is required.");
        await RequireValidCodeAsync(MasterDataKeys.AccessIntegrationAction, action, "action", request.CommunityId, cancellationToken);

        // Call the hardware seam (no-op/logging default; real drivers are Phase 12 / Task 40). It
        // never throws for an unreachable/absent device — it returns an outcome we record.
        var result = await accessControl.TriggerAsync(
            new AccessIntegrationCommand(request.CommunityId, action, request.GateId, request.GateLabel, request.Detail),
            cancellationToken);

        var evt = new AccessIntegrationEvent
        {
            CommunityId = request.CommunityId,
            GateId = request.GateId,
            GateLabel = string.IsNullOrWhiteSpace(request.GateLabel) ? null : request.GateLabel.Trim(),
            Action = action,
            Status = result.Succeeded ? AccessIntegrationStatuses.Succeeded : AccessIntegrationStatuses.Triggered,
            Detail = result.Note ?? (string.IsNullOrWhiteSpace(request.Detail) ? null : request.Detail.Trim()),
            TriggeredByUserId = request.TriggeredByUserId,
        };

        db.AccessIntegrationEvents.Add(evt);

        await audit.AppendAsync(
            new AuditEntry("TriggerIntegration", "AccessIntegrationEvent", evt.Id.ToString(), evt.CommunityId, Detail: action),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(evt);
    }

    // =============================================================================================
    // Task 16.4 — paginated list surface (Req 26.2, 26.3, 27.x)
    // =============================================================================================

    public Task<PagedData<WatchlistEntryDto>> ListWatchlistEntriesAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // The full watchlist register (Req 26.3) — tenant-scoped by the global filter; View-gated at
        // the endpoint. Distinct from the internal indexed MatchWatchlistAsync safety check.
        return db.WatchlistEntries.AsNoTracking().ToPagedResultAsync(
            parameters, WatchlistQueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
    }

    public Task<PagedData<GuardShiftDto>> ListGuardShiftsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        return db.GuardShifts.AsNoTracking().ToPagedResultAsync(
            parameters, GuardShiftQueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
    }

    public Task<PagedData<IncidentDto>> ListIncidentsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        return db.Incidents.AsNoTracking().ToPagedResultAsync(
            parameters, IncidentQueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
    }

    public Task<PagedData<SosAlertDto>> ListSosAlertsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        return db.SosAlerts.AsNoTracking().ToPagedResultAsync(
            parameters, SosAlertQueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
    }

    public Task<PagedData<AccessIntegrationEventDto>> ListAccessIntegrationEventsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        return db.AccessIntegrationEvents.AsNoTracking().ToPagedResultAsync(
            parameters, AccessIntegrationQueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
    }

    // =============================================================================================
    // Helpers
    // =============================================================================================

    /// <summary>
    /// Resolve the notification audience as the users whose roles grant <c>View</c> on
    /// <paramref name="resourceType"/> in <paramref name="communityId"/> (RBAC data — no hardcoded
    /// recipients or role names, steering: no hardcoding) and enqueue the template for each in the
    /// current transaction. A single projecting query (UserRole → RolePermission → Permission), no
    /// N+1. Community-scoped and platform-wide role assignments both qualify. The enqueue only Adds
    /// outbox rows; the caller's SaveChanges commits them atomically.
    /// </summary>
    private async Task NotifyResponderAudienceAsync(
        Guid communityId,
        string resourceType,
        string templateKey,
        IReadOnlyDictionary<string, string> placeholders,
        CancellationToken cancellationToken)
    {
        // Distinct user ids whose (non-deleted) roles — scoped to this community or platform-wide —
        // grant View on the resource type. Permission/Role/UserRole are platform-scoped (no tenant
        // filter), so the community predicate on the UserRole scope is explicit here.
        var recipientUserIds = await (
            from ur in db.UserRoles.AsNoTracking()
            where ur.CommunityId == communityId || ur.CommunityId == null
            join rp in db.RolePermissions.AsNoTracking() on ur.RoleId equals rp.RoleId
            join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            where p.ResourceType == resourceType && p.Action == PermissionAction.View
            select ur.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var userId in recipientUserIds)
        {
            await notifications.EnqueueAsync(
                new NotificationRequest(templateKey, userId, communityId, Placeholders: placeholders),
                cancellationToken);
        }
    }

    /// <summary>
    /// Resolve a severity's RANK as its position (SortOrder) in the configurable
    /// <c>Incident_Severity</c> list for the community (Req 27.2) — not a compiled rank. An unknown
    /// severity is a validation error.
    /// </summary>
    private async Task<int> ResolveSeverityRankAsync(string severity, Guid communityId, CancellationToken cancellationToken)
    {
        var list = await config.GetMasterDataAsync(MasterDataKeys.IncidentSeverity, communityId, cancellationToken: cancellationToken);
        var match = list.FirstOrDefault(e => string.Equals(e.Code, severity, StringComparison.Ordinal))
            ?? throw new ValidationException("severity", $"'{severity}' is not a valid incident severity.");
        return match.SortOrder;
    }

    private async Task RequireValidCodeAsync(string listKey, string code, string field, Guid communityId, CancellationToken cancellationToken)
    {
        var list = await config.GetMasterDataAsync(listKey, communityId, cancellationToken: cancellationToken);
        if (!list.Any(e => string.Equals(e.Code, code, StringComparison.Ordinal)))
        {
            throw new ValidationException(field, $"'{code}' is not a valid value for {listKey}.");
        }
    }

    private async Task RequirePermissionAsync(PermissionAction action, string resourceType, string what, CancellationToken cancellationToken)
    {
        if (!await authorizationGuard.HasPermissionAsync(action, resourceType, cancellationToken))
        {
            throw new ForbiddenException($"You do not have permission to {what}.");
        }
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private static string RequireText(string? value, string field, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(field, message);
        }

        return value.Trim();
    }

    private static WatchlistEntryDto ToDto(WatchlistEntry e) =>
        new(e.Id, e.CommunityId, e.SubjectType, e.SubjectIdentifier, e.Reason, e.Severity, e.Status);

    private static GuardShiftDto ToDto(GuardShift s) =>
        new(s.Id, s.CommunityId, s.GuardUserId, s.GateId, s.GateLabel, s.ShiftStartUtc, s.ShiftEndUtc, s.Status);

    private static IncidentDto ToDto(Incident i) =>
        new(i.Id, i.CommunityId, i.ReportedByUserId, i.GateId, i.Location, i.Category, i.Severity,
            i.Description, i.Status, i.EscalationState);

    private static SosAlertDto ToDto(SosAlert a) =>
        new(a.Id, a.CommunityId, a.RaisedByUserId, a.UnitId, a.Location, a.Status, a.RaisedAtUtc);

    private static AccessIntegrationEventDto ToDto(AccessIntegrationEvent e) =>
        new(e.Id, e.CommunityId, e.GateId, e.GateLabel, e.Action, e.Status, e.Detail);
}
