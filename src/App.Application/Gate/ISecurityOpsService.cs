using App.Application.Querying;
using App.Contracts.Envelope;

namespace App.Application.Gate;

/// <summary>
/// Gate security-operations service (Task 16.3, Req 26.1–26.5, 27.1–27.5): the blacklist/watchlist,
/// guard-shift roster, incident reporting + escalation, SOS workflow and the access-control
/// integration hooks. Kept as a FOCUSED service separate from <see cref="IGateService"/> (visit
/// passes / walk-ins / entry-exit) because these are a distinct security-ops concern with their own
/// permission resources; <see cref="IGateService"/> only gains a watchlist-match read it consults
/// in the gate flow. The HTTP controllers + list endpoints are Task 16.4.
///
/// <para><b>Watchlist (Req 26.1, 26.3, 26.5).</b> Staff with <c>Create</c> on <c>WatchlistEntry</c>
/// add/clear entries; <c>View</c> gates reads (Req 26.3). <see cref="MatchWatchlistAsync"/> is the
/// efficient, indexed match the gate flow runs on a presented subject; a match is FLAGGED and
/// admitting the subject then requires an <c>Approve</c>-permission override (enforced in the gate
/// flow, Req 26.5).</para>
///
/// <para><b>Guard shifts (Req 26.4, 26.5).</b> Staff with <c>Create</c> on <c>GuardShift</c> roster
/// a guard (a Phase 1 Guard-role <c>User</c>) to a gate + period; reads are tenant-scoped (the list
/// endpoint is 16.4).</para>
///
/// <para><b>Incidents (Req 27.1, 27.2).</b> A guard/supervisor with <c>Create</c> on <c>Incident</c>
/// reports an incident; when its severity rank meets the configurable escalation threshold the
/// incident auto-escalates and the configured responders are notified via Notification_Service in
/// the SAME transaction (Req 27.2). Acknowledge/resolve transitions are gated on <c>Edit</c> and are
/// concurrency-safe under the incident <c>RowVersion</c>.</para>
///
/// <para><b>SOS (Req 27.3, 27.4).</b> A resident or guard with <c>Create</c> on <c>SosAlert</c>
/// raises an SOS → the alert is <c>active</c> and the configured community audience is notified with
/// a MANDATORY emergency notification (always sent regardless of opt-out, Req 20.4). It stays active
/// until an <c>Edit</c>-permission user acknowledges and resolves it (Req 27.4), concurrency-safe
/// under the alert <c>RowVersion</c>.</para>
///
/// <para><b>Access integration (Req 27.5).</b> <see cref="TriggerAccessIntegrationAsync"/> drives a
/// boom-barrier/CCTV/access-control action through the <see cref="IAccessControlIntegration"/> seam
/// and RECORDS the triggered event (the real hardware drivers are Phase 12 / Task 40).</para>
///
/// <para>All mutations are audited and tenant-scoped; notifications are transactional (enqueued in
/// the caller's transaction).</para>
/// </summary>
public interface ISecurityOpsService
{
    // --- Watchlist (Req 26.1, 26.3, 26.5) ----------------------------------------------------------

    /// <summary>
    /// Add a watchlist entry for a person/vehicle (Req 26.1). Requires <c>Create</c> on
    /// <c>WatchlistEntry</c> + an authorized community; validates the subject type/severity against
    /// the configurable lists; normalizes the identifier (trim + upper-invariant) for the indexed
    /// match; stores it <c>active</c>. Audited.
    /// </summary>
    Task<WatchlistEntryDto> AddWatchlistEntryAsync(AddWatchlistEntryRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear an active watchlist entry (Req 26.5). Requires <c>Create</c> on <c>WatchlistEntry</c> +
    /// an authorized community; sets <c>cleared</c> so it no longer flags at the gate (retained for
    /// the record). A clear of an already-cleared entry → CONFLICT; records exactly once under the
    /// entry <c>RowVersion</c>. Audited.
    /// </summary>
    Task<WatchlistEntryDto> ClearWatchlistEntryAsync(Guid watchlistEntryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Match a presented subject (name / phone / vehicle number) against the community's ACTIVE
    /// watchlist (Req 26.1, 26.5). A tenant-scoped <c>AsNoTracking</c> single indexed equality lookup
    /// on the normalized identifier (no scan). Returns the matching entries (empty when none). This
    /// is the read the gate flow consults to flag a match; it does not require a write permission (it
    /// is an internal safety check, not a watchlist read of the full list).
    /// </summary>
    Task<IReadOnlyList<WatchlistEntryDto>> MatchWatchlistAsync(Guid communityId, string subjectIdentifier, CancellationToken cancellationToken = default);

    // --- Guard shifts (Req 26.4) -------------------------------------------------------------------

    /// <summary>
    /// Schedule a guard shift (Req 26.4). Requires <c>Create</c> on <c>GuardShift</c> + an authorized
    /// community; validates the shift period (end after start) and the guard user; stores it
    /// <c>scheduled</c>. Audited.
    /// </summary>
    Task<GuardShiftDto> ScheduleGuardShiftAsync(ScheduleGuardShiftRequest request, CancellationToken cancellationToken = default);

    // --- Incidents (Req 27.1, 27.2) ----------------------------------------------------------------

    /// <summary>
    /// Report a security incident (Req 27.1). Requires <c>Create</c> on <c>Incident</c> + an
    /// authorized community; validates category/severity against the configurable lists; stores it
    /// <c>open</c>. When the severity rank meets the configurable escalation threshold the incident
    /// is marked escalated and the configured responders are notified via Notification_Service in the
    /// same transaction (Req 27.2). Audited.
    /// </summary>
    Task<IncidentDto> ReportIncidentAsync(ReportIncidentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acknowledge an open incident (Req 27.1). Requires <c>Edit</c> on <c>Incident</c> + an
    /// authorized community; records the acknowledging user + timestamp and sets <c>acknowledged</c>
    /// (keeping any escalation state). An acknowledge of a resolved incident → CONFLICT; records
    /// exactly once under the incident <c>RowVersion</c>. Audited.
    /// </summary>
    Task<IncidentDto> AcknowledgeIncidentAsync(Guid incidentId, Guid acknowledgingUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve an incident (Req 27.1). Requires <c>Edit</c> on <c>Incident</c> + an authorized
    /// community; records the resolving user + timestamp and sets <c>resolved</c>. A resolve of an
    /// already-resolved incident → CONFLICT; records exactly once under the incident <c>RowVersion</c>.
    /// Audited.
    /// </summary>
    Task<IncidentDto> ResolveIncidentAsync(Guid incidentId, Guid resolvingUserId, CancellationToken cancellationToken = default);

    // --- SOS (Req 27.3, 27.4) ----------------------------------------------------------------------

    /// <summary>
    /// Raise an SOS alert (Req 27.3). Requires <c>Create</c> on <c>SosAlert</c> + an authorized
    /// community; stores it <c>active</c> and notifies the configured community audience with a
    /// MANDATORY emergency notification (always sent regardless of opt-out, Req 20.4) in the same
    /// transaction. Audited.
    /// </summary>
    Task<SosAlertDto> RaiseSosAsync(RaiseSosRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acknowledge an active SOS alert (Req 27.4). Requires <c>Edit</c> on <c>SosAlert</c> + an
    /// authorized community; records the acknowledging user + timestamp and sets <c>acknowledged</c>
    /// (still active until resolved). An acknowledge of a resolved SOS → CONFLICT; records exactly
    /// once under the alert <c>RowVersion</c>. Audited.
    /// </summary>
    Task<SosAlertDto> AcknowledgeSosAsync(Guid sosAlertId, Guid acknowledgingUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve an SOS alert (Req 27.4). Requires <c>Edit</c> on <c>SosAlert</c> + an authorized
    /// community; records the resolving user + timestamp and sets <c>resolved</c> so it is no longer
    /// active. A resolve of an already-resolved SOS → CONFLICT; records exactly once under the alert
    /// <c>RowVersion</c>. Audited.
    /// </summary>
    Task<SosAlertDto> ResolveSosAsync(Guid sosAlertId, Guid resolvingUserId, CancellationToken cancellationToken = default);

    // --- Access-control integration (Req 27.5) -----------------------------------------------------

    /// <summary>
    /// Trigger an access-control hardware action (open/close boom barrier, CCTV snapshot, access
    /// event) through the <see cref="IAccessControlIntegration"/> seam and RECORD the triggered event
    /// (Req 27.5). Requires <c>Create</c> on <c>Incident</c> (the guard-facing gate-operations grant)
    /// + an authorized community; validates the action against the configurable list. The real
    /// hardware drivers are Phase 12 / Task 40 — this phase records the event regardless of whether a
    /// device is attached, and never fails the operation on an unreachable device. Audited.
    /// </summary>
    Task<AccessIntegrationEventDto> TriggerAccessIntegrationAsync(TriggerAccessIntegrationRequest request, CancellationToken cancellationToken = default);

    // --- Task 16.4 — paginated list surface over the security-ops entities (Req 26.2, 26.3, 27.x) --

    /// <summary>
    /// Paginated/filter/sort list of watchlist entries (Req 26.3, 26.5), tenant-scoped. Allow-list:
    /// filter/sort by status, subjectType, severity; search by subject identifier. View-gated at the
    /// endpoint. Reads the full register — distinct from the internal indexed <see cref="MatchWatchlistAsync"/>.
    /// </summary>
    Task<PagedData<WatchlistEntryDto>> ListWatchlistEntriesAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated/sort list of guard-shift roster entries (Req 26.2), tenant-scoped and chronological
    /// by default riding the (CommunityId, ShiftStartUtc) index. Allow-list: filter/sort by status,
    /// guardUserId, shiftStartUtc.
    /// </summary>
    Task<PagedData<GuardShiftDto>> ListGuardShiftsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated/filter/sort list of incidents (Req 26.3, 27.1), tenant-scoped. Allow-list: filter/
    /// sort by status, severity, category, escalationState; the status+severity filters are the
    /// incident queue clients drive.
    /// </summary>
    Task<PagedData<IncidentDto>> ListIncidentsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated/filter/sort list of SOS alerts (Req 27.3, 27.4), tenant-scoped, newest-first by
    /// default. Allow-list: filter/sort by status, raisedAtUtc — so "active SOS alerts" rides the
    /// status filter.
    /// </summary>
    Task<PagedData<SosAlertDto>> ListSosAlertsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated/filter/sort list of recorded access-integration events (Req 27.5), tenant-scoped,
    /// newest-first by default riding the (CommunityId, CreatedAtUtc) index. Allow-list: filter/sort
    /// by action, status.
    /// </summary>
    Task<PagedData<AccessIntegrationEventDto>> ListAccessIntegrationEventsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default);
}
