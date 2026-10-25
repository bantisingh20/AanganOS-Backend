using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Gate;

/// <summary>
/// An emergency SOS alert a resident or guard raises (Req 27.3), active until a staff member
/// acknowledges and resolves it (Req 27.4). Carries its owning
/// <see cref="TenantOwnedEntity.CommunityId"/>.
///
/// <para><b>Not <c>IUnitScoped</c>.</b> An SOS is a community-wide emergency event handled by
/// security staff, not resident self-view data — scoped by community (the tenant filter), never
/// pinned to one unit. The optional <see cref="UnitId"/> records where the SOS came from (for the
/// responder) without turning the alert into resident-self-scoped data; it is therefore surfaced as
/// a plain property, NOT the <c>IUnitScoped</c> contract.</para>
///
/// <para><b>Mandatory emergency notification (Req 27.3).</b> Raising an SOS sends an emergency
/// notification to the configured community audience through Notification_Service. Emergency is a
/// MANDATORY notification category — the template is classified mandatory so it is always delivered
/// regardless of a recipient's opt-out (Req 20.4). The notification is enqueued in the SAME
/// transaction that creates the alert (transactional outbox).</para>
///
/// <para><b>Active until acknowledged + resolved (Req 27.4).</b> <see cref="Status"/> starts
/// <c>active</c> and stays active until an Edit-permission user acknowledges (→ <c>acknowledged</c>)
/// and resolves (→ <c>resolved</c>) it. The inherited <c>RowVersion</c> makes the acknowledge/resolve
/// transition record exactly once under concurrency (a double-ack → CONFLICT). <see cref="Status"/>
/// resolves against the editable <c>Sos_Alert_Status</c> list (steering: no hardcoding).</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token, audit columns.</para>
/// </summary>
public class SosAlert : TenantOwnedEntity
{
    /// <summary>The resident or guard who raised the SOS (Req 27.3). Required.</summary>
    public Guid RaisedByUserId { get; set; }

    /// <summary>Optional originating unit (for the responder, Req 27.3), or null. Not the IUnitScoped contract.</summary>
    public Guid? UnitId { get; set; }

    /// <summary>Optional free-text location/identifier of the emergency (Req 27.3).</summary>
    public string? Location { get; set; }

    /// <summary>
    /// Configurable status code resolved against the <c>Sos_Alert_Status</c> master-data list
    /// (active / acknowledged / resolved, Req 27.4). Starts <c>active</c>. Stored as the stable code,
    /// never a frozen enum.
    /// </summary>
    public string Status { get; set; } = SosAlertStatuses.Active;

    /// <summary>When the SOS was raised (Req 27.3). Set on create.</summary>
    public DateTime RaisedAtUtc { get; set; }

    /// <summary>The staff user who acknowledged the SOS (Req 27.4), or null while active.</summary>
    public Guid? AcknowledgedByUserId { get; set; }

    /// <summary>When the SOS was acknowledged (Req 27.4), or null while active.</summary>
    public DateTime? AcknowledgedAtUtc { get; set; }

    /// <summary>The staff user who resolved the SOS (Req 27.4), or null until resolved.</summary>
    public Guid? ResolvedByUserId { get; set; }

    /// <summary>When the SOS was resolved (Req 27.4), or null until resolved.</summary>
    public DateTime? ResolvedAtUtc { get; set; }

    /// <summary>Whether the SOS is still active (not resolved, Req 27.4). Computed, not stored.</summary>
    public bool IsActive =>
        !string.Equals(Status, SosAlertStatuses.Resolved, StringComparison.Ordinal);
}

/// <summary>
/// Well-known default <c>Sos_Alert_Status</c> codes (Req 27.4). Seeded master-data codes kept as
/// constants. <b>Data</b>, not a behavioural enum — the list is editable.
/// </summary>
public static class SosAlertStatuses
{
    /// <summary>Initial status on raise (Req 27.3): stays active until acknowledged + resolved (Req 27.4).</summary>
    public const string Active = "active";

    /// <summary>A staff member has acknowledged the SOS (Req 27.4), still unresolved.</summary>
    public const string Acknowledged = "acknowledged";

    /// <summary>The SOS has been resolved (Req 27.4).</summary>
    public const string Resolved = "resolved";
}
