using App.Domain.Common;

namespace App.Domain.Maintenance;

/// <summary>
/// A completed inspection recorded against an <see cref="Asset"/> (optionally tied to a
/// <see cref="WorkOrder"/>, Req 37.1): when a Facility Manager / technician runs an inspection they
/// record the overall result, the inspecting user and the timestamp, with one
/// <see cref="InspectionChecklistItem"/> row per checklist line and its result. Completion appends a
/// single append-only <see cref="EquipmentHistoryEntry"/> to the asset's history
/// (<see cref="EquipmentHistoryEventTypes.InspectionCompleted"/>) so inspections flow into the same
/// chronological equipment history as work orders (Req 37.5).
///
/// <para><b>Community infrastructure, NOT resident self-view data.</b> Like <see cref="Asset"/> /
/// <see cref="WorkOrder"/>, an inspection belongs to the community, so it is a
/// <see cref="TenantOwnedEntity"/> deliberately NOT <see cref="IUnitScoped"/> — a Facility-Manager
/// record bounded by the community (the tenant global filter), not pinned to one unit.</para>
///
/// <para><b>Configurable overall result (steering: no hardcoding, Req 37.1).</b>
/// <see cref="OverallResult"/> resolves against the editable <c>Inspection_Result</c> master-data
/// list (pass / fail / pass_with_observations / ...), and each item's
/// <see cref="InspectionChecklistItem.Result"/> against the same list — stored as stable codes, never
/// frozen enums.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token (Req 22.4), audit columns.</para>
/// </summary>
public class InspectionChecklist : TenantOwnedEntity
{
    /// <summary>The asset this inspection was performed against (Req 37.1). Required.</summary>
    public Guid AssetId { get; set; }

    /// <summary>
    /// The work order this inspection was performed under (Req 37.1), or null for a standalone
    /// inspection not tied to a work order.
    /// </summary>
    public Guid? WorkOrderId { get; set; }

    /// <summary>Human-readable checklist name/label (e.g. "Monthly Lift Safety Check"). Required (Req 37.1).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Configurable overall-result code resolved against the <c>Inspection_Result</c> master-data
    /// list (Req 37.1). Stored as the stable code, never a frozen enum.
    /// </summary>
    public string OverallResult { get; set; } = string.Empty;

    /// <summary>
    /// The user who performed the inspection (Req 37.1), from the request tenant context, or null
    /// for a system actor.
    /// </summary>
    public string? InspectedByUserId { get; set; }

    /// <summary>When the inspection was completed, in UTC, from the injected clock (Req 37.1).</summary>
    public DateTime InspectedAtUtc { get; set; }

    /// <summary>Optional free-text notes about the inspection (Req 37.1).</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// The checklist items and their individual results (Req 37.1). Owned children carrying the
    /// parent's <see cref="TenantOwnedEntity.CommunityId"/>.
    /// </summary>
    public List<InspectionChecklistItem> Items { get; set; } = [];
}

/// <summary>
/// One line of a completed <see cref="InspectionChecklist"/> (Req 37.1): the checklist item and the
/// result recorded for it. Carries the parent inspection's community so it stays tenant-consistent.
/// Derives from <see cref="TenantOwnedEntity"/>.
/// </summary>
public class InspectionChecklistItem : TenantOwnedEntity
{
    /// <summary>The parent inspection this item belongs to (Req 37.1). Required.</summary>
    public Guid InspectionChecklistId { get; set; }

    /// <summary>Human-readable checklist line/question (e.g. "Emergency brake functional?"). Required (Req 37.1).</summary>
    public string ItemText { get; set; } = string.Empty;

    /// <summary>
    /// Configurable per-item result code resolved against the <c>Inspection_Result</c> master-data
    /// list (Req 37.1). Stored as the stable code, never a frozen enum.
    /// </summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>Optional per-item remark/observation (Req 37.1).</summary>
    public string? Remark { get; set; }

    /// <summary>Stable ordering of the item within its checklist (Req 37.1).</summary>
    public int SortOrder { get; set; }
}
