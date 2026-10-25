using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Maintenance;

/// <summary>
/// One entry in an <see cref="Asset"/>'s immutable equipment history (Req 36.5). When a
/// <see cref="WorkOrder"/> is completed the Maintenance_Service appends a single entry recording the
/// completed work (the asset, the source work order, what happened, the acting user and the UTC
/// timestamp) so the asset carries a full, traceable maintenance history over its lifecycle.
///
/// <para><b>Append-only, non-editable (Req 36.5).</b> Modeled exactly like the other append-only
/// registers (<c>AuditLog</c> / <c>CustodyTimelineEntry</c> / <c>OwnershipHistory</c>): the service
/// only ever <i>inserts</i> a row — an equipment-history entry is never updated or deleted through
/// the application. The data layer enforces this independently: the
/// <c>AuditSaveChangesInterceptor</c> rejects any <c>EquipmentHistoryEntry</c> in a Modified/Deleted
/// state (the same guard it applies to <c>AuditLog</c> / <c>CustodyTimelineEntry</c>), so there is no
/// application path that can rewrite equipment history — a tamper fails loudly rather than silently
/// corrupting the trail.</para>
///
/// <para><b>Chronological, tenant-scoped retrieval (Req 36.5).</b> As a <see cref="TenantOwnedEntity"/>
/// it inherits the tenant + soft-delete global filter, so a history read is bounded to the caller's
/// authorized communities with no bespoke predicate. The <c>(CommunityId, AssetId, OccurredAtUtc)</c>
/// index (in its entity configuration) backs "an asset's equipment history in chronological order"
/// as a single ordered indexed read (no N+1). NOT <see cref="IUnitScoped"/> — an audit-style register
/// read by staff, the same stance as <c>CustodyTimelineEntry</c>.</para>
///
/// <para><b>Event type is a stable code (steering: no hardcoding).</b> <see cref="EventType"/> stores
/// one of the well-known <see cref="EquipmentHistoryEventTypes"/> codes — <b>data</b> describing what
/// happened, not a behavioural enum; later maintenance modules (inspections / AMC / meters, Task 21.3)
/// can append their own history verbs without a schema change.</para>
/// </summary>
public class EquipmentHistoryEntry : TenantOwnedEntity
{
    /// <summary>The asset this history entry belongs to (Req 36.5). Required.</summary>
    public Guid AssetId { get; set; }

    /// <summary>
    /// The work order whose completion produced this entry (Req 36.5), or null for a future
    /// non-work-order history event (inspection/meter, Task 21.3).
    /// </summary>
    public Guid? WorkOrderId { get; set; }

    /// <summary>
    /// What happened — one of the <see cref="EquipmentHistoryEventTypes"/> codes (Req 36.5). Stored
    /// as the stable code, never a frozen enum.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// A short, human-readable summary of the event (Req 36.5): e.g. the completed work order's
    /// title. Secret-free; not inline evidence bytes.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Optional small, secret-free detail of the event (Req 36.5): e.g. the completion notes or the
    /// work-order type/priority. Not inline evidence bytes.
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>
    /// The user who performed the event, from the request tenant context (Req 36.5). Null for a
    /// system actor — e.g. a preventive work order completed by an automated flow with no interactive
    /// user.
    /// </summary>
    public string? ActingUserId { get; set; }

    /// <summary>When the event occurred, in UTC, from the injected clock (Req 36.5).</summary>
    public DateTime OccurredAtUtc { get; set; }
}

/// <summary>
/// Well-known <c>EquipmentHistoryEntry.EventType</c> codes (Req 36.5). Seeded-free constants kept so
/// the service records an event without a magic string. <b>Data</b>, not a behavioural enum — the
/// set is open; a later maintenance module (inspections / AMC / meters, Task 21.3) can record its own
/// history verb simply by appending a row with a new code (steering: no hardcoding).
/// </summary>
public static class EquipmentHistoryEventTypes
{
    /// <summary>A work order was completed against the asset (Req 36.5).</summary>
    public const string WorkOrderCompleted = "work_order_completed";

    /// <summary>An inspection checklist was completed against the asset (Req 37.1).</summary>
    public const string InspectionCompleted = "inspection_completed";

    /// <summary>A meter reading was recorded for the asset's equipment counter (Req 37.4).</summary>
    public const string MeterReadingRecorded = "meter_reading_recorded";
}
