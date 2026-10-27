namespace App.Application.Gate;

// ===================================================================================================
// Task 16.3 — watchlist / guard shifts / incidents / SOS / access-integration DTOs (Req 26.x, 27.x)
// ===================================================================================================

/// <summary>Read shape for a <c>WatchlistEntry</c> (Req 26.1).</summary>
/// <param name="Id">The entry id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="SubjectType">Configurable subject-type code (person / vehicle).</param>
/// <param name="SubjectIdentifier">Normalized matched value (name / phone / vehicle number).</param>
/// <param name="Reason">Why the subject is watchlisted.</param>
/// <param name="Severity">Configurable severity code.</param>
/// <param name="Status">Configurable status code (active / cleared).</param>
public sealed record WatchlistEntryDto(
    Guid Id,
    Guid CommunityId,
    string SubjectType,
    string SubjectIdentifier,
    string Reason,
    string Severity,
    string Status);

/// <summary>
/// Add a watchlist entry (Req 26.1). The community is resolved from the caller's authorized scope
/// (supplied explicitly so a multi-community admin names the target community).
/// </summary>
/// <param name="CommunityId">Target community. Required.</param>
/// <param name="SubjectType">Subject-type code from the <c>Watchlist_Subject_Type</c> list. Required.</param>
/// <param name="SubjectIdentifier">The value to match (name / phone / vehicle number). Required.</param>
/// <param name="Reason">Why the subject is watchlisted. Required.</param>
/// <param name="Severity">Severity code from the <c>Watchlist_Severity</c> list. Required.</param>
public sealed record AddWatchlistEntryRequest(
    Guid CommunityId,
    string SubjectType,
    string SubjectIdentifier,
    string Reason,
    string Severity);

/// <summary>Read shape for a <c>GuardShift</c> roster entry (Req 26.4).</summary>
/// <param name="Id">The shift id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="GuardUserId">The rostered guard user.</param>
/// <param name="GateId">Optional gate id.</param>
/// <param name="GateLabel">Optional free-text gate label.</param>
/// <param name="ShiftStartUtc">Shift start.</param>
/// <param name="ShiftEndUtc">Shift end.</param>
/// <param name="Status">Configurable status code (scheduled / active / completed / cancelled).</param>
public sealed record GuardShiftDto(
    Guid Id,
    Guid CommunityId,
    Guid GuardUserId,
    Guid? GateId,
    string? GateLabel,
    DateTime ShiftStartUtc,
    DateTime ShiftEndUtc,
    string Status);

/// <summary>Schedule a guard shift (Req 26.4).</summary>
/// <param name="CommunityId">Target community. Required.</param>
/// <param name="GuardUserId">The guard user (a Guard-role user). Required.</param>
/// <param name="GateId">Optional gate id.</param>
/// <param name="GateLabel">Optional free-text gate label.</param>
/// <param name="ShiftStartUtc">Shift start. Required.</param>
/// <param name="ShiftEndUtc">Shift end; must be after <paramref name="ShiftStartUtc"/>.</param>
public sealed record ScheduleGuardShiftRequest(
    Guid CommunityId,
    Guid GuardUserId,
    Guid? GateId,
    string? GateLabel,
    DateTime ShiftStartUtc,
    DateTime ShiftEndUtc);

/// <summary>Read shape for an <c>Incident</c> (Req 27.1).</summary>
/// <param name="Id">The incident id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="ReportedByUserId">The reporting user.</param>
/// <param name="GateId">Optional gate id.</param>
/// <param name="Location">Optional free-text location.</param>
/// <param name="Category">Configurable incident-category code.</param>
/// <param name="Severity">Configurable severity code.</param>
/// <param name="Description">What happened.</param>
/// <param name="Status">Configurable status code (open / acknowledged / escalated / resolved).</param>
/// <param name="EscalationState">Escalation state (none / escalated).</param>
public sealed record IncidentDto(
    Guid Id,
    Guid CommunityId,
    Guid ReportedByUserId,
    Guid? GateId,
    string? Location,
    string Category,
    string Severity,
    string Description,
    string Status,
    string EscalationState);

/// <summary>Report a security incident (Req 27.1).</summary>
/// <param name="CommunityId">Target community. Required.</param>
/// <param name="ReportedByUserId">The reporting guard/supervisor user. Required.</param>
/// <param name="GateId">Optional gate id.</param>
/// <param name="Location">Optional free-text location.</param>
/// <param name="Category">Category code from the <c>Incident_Category</c> list. Required.</param>
/// <param name="Severity">Severity code from the <c>Incident_Severity</c> list. Required.</param>
/// <param name="Description">What happened. Required.</param>
public sealed record ReportIncidentRequest(
    Guid CommunityId,
    Guid ReportedByUserId,
    Guid? GateId,
    string? Location,
    string Category,
    string Severity,
    string Description);

/// <summary>Read shape for a <c>SosAlert</c> (Req 27.3).</summary>
/// <param name="Id">The alert id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="RaisedByUserId">Who raised it.</param>
/// <param name="UnitId">Optional originating unit.</param>
/// <param name="Location">Optional free-text location.</param>
/// <param name="Status">Configurable status code (active / acknowledged / resolved).</param>
/// <param name="RaisedAtUtc">When it was raised.</param>
public sealed record SosAlertDto(
    Guid Id,
    Guid CommunityId,
    Guid RaisedByUserId,
    Guid? UnitId,
    string? Location,
    string Status,
    DateTime RaisedAtUtc);

/// <summary>Raise an SOS alert (Req 27.3).</summary>
/// <param name="CommunityId">Target community. Required.</param>
/// <param name="RaisedByUserId">The resident/guard raising it. Required.</param>
/// <param name="UnitId">Optional originating unit.</param>
/// <param name="Location">Optional free-text location.</param>
public sealed record RaiseSosRequest(
    Guid CommunityId,
    Guid RaisedByUserId,
    Guid? UnitId,
    string? Location);

/// <summary>Read shape for a recorded <c>AccessIntegrationEvent</c> (Req 27.5).</summary>
/// <param name="Id">The event id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="GateId">Optional gate id.</param>
/// <param name="GateLabel">Optional free-text gate/device label.</param>
/// <param name="Action">Configurable integration-action code.</param>
/// <param name="Status">Configurable outcome code (triggered / succeeded / failed).</param>
/// <param name="Detail">Optional secret-free detail/note.</param>
public sealed record AccessIntegrationEventDto(
    Guid Id,
    Guid CommunityId,
    Guid? GateId,
    string? GateLabel,
    string Action,
    string Status,
    string? Detail);

/// <summary>Trigger an access-control hardware action and record it (Req 27.5).</summary>
/// <param name="CommunityId">Target community. Required.</param>
/// <param name="Action">Action code from the <c>Access_Integration_Action</c> list. Required.</param>
/// <param name="GateId">Optional gate id.</param>
/// <param name="GateLabel">Optional free-text gate/device label.</param>
/// <param name="TriggeredByUserId">Optional user triggering the action.</param>
/// <param name="Detail">Optional secret-free detail/reason.</param>
public sealed record TriggerAccessIntegrationRequest(
    Guid CommunityId,
    string Action,
    Guid? GateId,
    string? GateLabel,
    Guid? TriggeredByUserId,
    string? Detail);
