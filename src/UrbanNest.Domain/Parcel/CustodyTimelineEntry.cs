using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Parcel;

/// <summary>
/// One entry in a <see cref="Parcel"/>'s immutable Custody_Timeline (Req 31.1, 31.5). Each row
/// records a single custody event — registration, taken into custody, custody-location assignment,
/// handover, lost/damaged, returned, escalation — together with the acting user and the UTC
/// timestamp it occurred.
///
/// <para><b>Append-only, non-editable (Req 31.1).</b> Modeled exactly like the other append-only
/// registers (<c>AuditLog</c> / <c>OwnershipHistory</c> / <c>TenancyHistory</c>): the service only
/// ever <i>inserts</i> a row — a timeline entry is never updated or deleted through the application.
/// The data layer enforces this independently: the <c>AuditSaveChangesInterceptor</c> rejects any
/// <c>CustodyTimelineEntry</c> in a Modified/Deleted state (the same guard it applies to
/// <c>AuditLog</c>), so there is no application path that can rewrite custody history — a tamper
/// fails loudly rather than silently corrupting the trail.</para>
///
/// <para><b>Chronological, tenant-scoped retrieval (Req 31.5).</b> As a <see cref="TenantOwnedEntity"/>
/// it inherits the tenant + soft-delete global filter, so a timeline read is bounded to the caller's
/// authorized communities with no bespoke predicate. The <c>(CommunityId, ParcelId, OccurredAtUtc)</c>
/// index (in its entity configuration) backs "a parcel's timeline in chronological order" as a single
/// ordered indexed read (no N+1).</para>
///
/// <para><b>Not <see cref="IUnitScoped"/>.</b> The timeline is read with the <c>CustodyTimelineEntry</c>
/// View permission by staff; a resident reaches a parcel's status through the parcel itself (which IS
/// unit-scoped). Keeping the timeline off the resident self-scope filter avoids pinning an audit-style
/// register to one unit, the same stance <c>OwnershipHistory</c>/<c>EntryExitEvent</c> take.</para>
///
/// <para><b>Event type is a stable code (steering: no hardcoding).</b> <see cref="EventType"/> stores
/// one of the well-known <see cref="CustodyTimelineEventTypes"/> codes — <b>data</b> describing what
/// happened, not a behavioural enum; new modules can record their own custody verbs without a schema
/// change. The optional <see cref="FromStatus"/>/<see cref="ToStatus"/> capture a status transition
/// when the event is one, and <see cref="Detail"/> carries a small secret-free note (e.g. the custody
/// location code, the collector/method, the linked incident id, or the SLA-breach context).</para>
/// </summary>
public class CustodyTimelineEntry : TenantOwnedEntity
{
    /// <summary>The parcel this entry belongs to (Req 31.1). Required.</summary>
    public Guid ParcelId { get; set; }

    /// <summary>
    /// What happened — one of the <see cref="CustodyTimelineEventTypes"/> codes (Req 31.1). Stored as
    /// the stable code, never a frozen enum.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>The parcel status before the event, when the event is a status transition; otherwise null.</summary>
    public string? FromStatus { get; set; }

    /// <summary>The parcel status after the event, when the event is a status transition; otherwise null.</summary>
    public string? ToStatus { get; set; }

    /// <summary>
    /// The user who performed the event, from the request tenant context (Req 31.1). Null for a
    /// system actor — e.g. the SLA-breach escalation raised by the background sweep, which has no
    /// interactive user.
    /// </summary>
    public string? ActingUserId { get; set; }

    /// <summary>
    /// Optional small, secret-free detail of the event (Req 31.1): e.g. the custody location code,
    /// the handover method, the linked <c>Incident</c> id for a lost/damaged event, or the
    /// SLA-breach note. Not inline evidence bytes — evidence stays a File_Service reference on the
    /// parcel/incident.
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>When the event occurred, in UTC, from the injected clock (Req 31.1).</summary>
    public DateTime OccurredAtUtc { get; set; }
}

/// <summary>
/// Well-known <c>CustodyTimelineEntry.EventType</c> codes (Req 31.1). Seeded-free constants kept so
/// the service records an event without a magic string. <b>Data</b>, not a behavioural enum — the
/// set is open; a later module can record its own custody verb simply by appending a row with a new
/// code (steering: no hardcoding).
/// </summary>
public static class CustodyTimelineEventTypes
{
    /// <summary>The parcel was registered/received at the gate (Req 28.1).</summary>
    public const string Registered = "registered";

    /// <summary>A parcel status transition occurred (Req 28.3) — <c>FromStatus</c>/<c>ToStatus</c> are set.</summary>
    public const string StatusChanged = "status_changed";

    /// <summary>The parcel was taken into custody and assigned a custody location (Req 29.1).</summary>
    public const string CustodyAssigned = "custody_assigned";

    /// <summary>The parcel was handed over to a verified collector (Req 30.4).</summary>
    public const string HandedOver = "handed_over";

    /// <summary>The parcel was recorded lost or damaged and a linked incident was raised (Req 31.3).</summary>
    public const string LostDamaged = "lost_damaged";

    /// <summary>The parcel was returned to the delivery provider (Req 31.4).</summary>
    public const string Returned = "returned";

    /// <summary>The parcel breached its configured custody SLA and was escalated (Req 31.2).</summary>
    public const string SlaEscalated = "sla_escalated";
}
