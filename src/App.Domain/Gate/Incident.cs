using App.Domain.Common;

namespace App.Domain.Gate;

/// <summary>
/// A security incident a guard/supervisor reports (Req 27.1), carrying its category, severity,
/// description, location and reporting user. Carries its owning
/// <see cref="TenantOwnedEntity.CommunityId"/>.
///
/// <para><b>Not <c>IUnitScoped</c>.</b> An incident is a community-wide security record, not resident
/// self-view data — scoped by community (the tenant filter), never pinned to one unit.</para>
///
/// <para><b>Configurable category / severity / status (steering: no hardcoding).</b>
/// <see cref="Category"/> resolves against the editable <c>Incident_Category</c> list;
/// <see cref="Severity"/> against <c>Incident_Severity</c>. The severity's <i>rank</i> is the
/// master-data <c>SortOrder</c> (not a compiled rank), compared against a configurable escalation
/// threshold (<c>gate.incident.escalationThreshold</c>) so a community tunes when an incident
/// auto-escalates without a code change (Req 27.2). <see cref="Status"/> resolves against
/// <c>Incident_Status</c> (open / acknowledged / escalated / resolved).</para>
///
/// <para><b>Escalation state (Req 27.2).</b> <see cref="EscalationState"/> records whether the
/// incident has been escalated to responders (<c>none</c> → <c>escalated</c>); set when the severity
/// meets the threshold on report, driving the responder notification. Kept distinct from
/// <see cref="Status"/> so an acknowledged incident still records that it was escalated.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token (the acknowledge/resolve-once guard, Req 22.4), audit columns.</para>
/// </summary>
public class Incident : TenantOwnedEntity
{
    /// <summary>The guard/supervisor who reported the incident (Req 27.1). Required.</summary>
    public Guid ReportedByUserId { get; set; }

    /// <summary>Optional gate id the incident relates to (the Gate master is a later task), or null.</summary>
    public Guid? GateId { get; set; }

    /// <summary>Optional free-text location/identifier of the incident (Req 27.1).</summary>
    public string? Location { get; set; }

    /// <summary>
    /// Configurable incident-category code resolved against the <c>Incident_Category</c> master-data
    /// list (trespass / theft / vandalism / medical / fire / dispute / other, Req 27.1). Stored as the
    /// stable code, never a frozen enum.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Configurable severity code resolved against the <c>Incident_Severity</c> master-data list
    /// (low / medium / high / critical, Req 27.1). The severity's rank is the list's SortOrder,
    /// compared against the configurable escalation threshold (Req 27.2). Stored as the stable code.
    /// </summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>Free-text description of what happened (Req 27.1). Required.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Configurable status code resolved against the <c>Incident_Status</c> master-data list
    /// (open / acknowledged / escalated / resolved, Req 27.1). Starts <c>open</c>. Stored as the
    /// stable code, never a frozen enum.
    /// </summary>
    public string Status { get; set; } = IncidentStatuses.Open;

    /// <summary>
    /// Whether the incident has been escalated to responders (Req 27.2): <c>none</c> until the
    /// severity meets the configured threshold, then <c>escalated</c>. Resolves against the editable
    /// <c>Incident_Escalation_State</c> list. Distinct from <see cref="Status"/> so an acknowledged
    /// incident still records it was escalated.
    /// </summary>
    public string EscalationState { get; set; } = IncidentEscalationStates.None;

    /// <summary>The user who acknowledged the incident (Req 27.1), or null while open.</summary>
    public Guid? AcknowledgedByUserId { get; set; }

    /// <summary>When the incident was acknowledged, or null while open.</summary>
    public DateTime? AcknowledgedAtUtc { get; set; }

    /// <summary>The user who resolved the incident (Req 27.1), or null until resolved.</summary>
    public Guid? ResolvedByUserId { get; set; }

    /// <summary>When the incident was resolved, or null until resolved.</summary>
    public DateTime? ResolvedAtUtc { get; set; }

    /// <summary>Whether the incident is still open (not resolved). Computed, not stored.</summary>
    public bool IsResolved =>
        string.Equals(Status, IncidentStatuses.Resolved, StringComparison.Ordinal);

    /// <summary>Whether the incident has been escalated to responders. Computed, not stored.</summary>
    public bool IsEscalated =>
        string.Equals(EscalationState, IncidentEscalationStates.Escalated, StringComparison.Ordinal);
}

/// <summary>
/// Well-known default <c>Incident_Status</c> codes (Req 27.1). Seeded master-data codes kept as
/// constants. <b>Data</b>, not a behavioural enum — the list is editable.
/// </summary>
public static class IncidentStatuses
{
    /// <summary>Initial status on report (Req 27.1).</summary>
    public const string Open = "open";

    /// <summary>A responder has acknowledged the incident (Req 27.1).</summary>
    public const string Acknowledged = "acknowledged";

    /// <summary>The incident was escalated to responders (Req 27.2).</summary>
    public const string Escalated = "escalated";

    /// <summary>The incident has been resolved (Req 27.1).</summary>
    public const string Resolved = "resolved";
}

/// <summary>
/// Well-known default <c>Incident_Escalation_State</c> codes (Req 27.2). Seeded master-data codes
/// kept as constants. <b>Data</b>, not a behavioural enum — the list is editable.
/// </summary>
public static class IncidentEscalationStates
{
    /// <summary>Not escalated (severity below the configured threshold).</summary>
    public const string None = "none";

    /// <summary>Escalated to the configured responders (Req 27.2).</summary>
    public const string Escalated = "escalated";
}
