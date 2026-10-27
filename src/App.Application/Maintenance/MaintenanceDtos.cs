namespace App.Application.Maintenance;

/// <summary>Read shape for an <c>Asset</c> in the maintenance register (Req 35.1).</summary>
/// <param name="Id">The asset id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="Name">Human-readable asset name/label.</param>
/// <param name="SerialNumber">The asset's serial/tag number, unique within the community.</param>
/// <param name="Location">The asset's physical location within the community.</param>
/// <param name="AssetType">Configurable asset-type code (Req 35.1).</param>
/// <param name="Category">Configurable asset-category code, or null (Req 35.1).</param>
/// <param name="Status">Configurable asset lifecycle-status code (active on registration, Req 35.1).</param>
/// <param name="QrToken">The opaque, community-unique scannable QR token identifying the asset (Req 35.2).</param>
/// <param name="UnitId">The optional unit the asset is tied to, or null for a common-area/community asset.</param>
/// <param name="WarrantyProvider">The warranty provider / vendor, or null.</param>
/// <param name="WarrantyReference">The warranty reference / policy number, or null.</param>
/// <param name="WarrantyStartUtc">When the warranty began, or null.</param>
/// <param name="WarrantyEndUtc">When the warranty expires, or null.</param>
/// <param name="InstalledOnUtc">When the asset was installed/commissioned, or null.</param>
/// <param name="Notes">Optional free-text notes, or null.</param>
/// <param name="CreatedAtUtc">When the asset was registered.</param>
public sealed record AssetDto(
    Guid Id,
    Guid CommunityId,
    string Name,
    string SerialNumber,
    string Location,
    string AssetType,
    string? Category,
    string Status,
    string QrToken,
    Guid? UnitId,
    string? WarrantyProvider,
    string? WarrantyReference,
    DateTime? WarrantyStartUtc,
    DateTime? WarrantyEndUtc,
    DateTime? InstalledOnUtc,
    string? Notes,
    DateTime CreatedAtUtc);

/// <summary>
/// Register an asset in the maintenance register (Req 35.1). The caller supplies the owning
/// community, a name, a serial number (unique within the community), a location, a configurable
/// asset type and optional category/warranty/install details. The service validates the type
/// (and category, when supplied) against the editable <c>Asset_Type</c> / <c>Asset_Category</c>
/// lists, sets the lifecycle status to <c>active</c>, and generates an opaque, community-unique QR
/// token identifying the asset (Req 35.2).
/// </summary>
/// <param name="CommunityId">The owning community (must be authorized for the caller). Required.</param>
/// <param name="Name">Human-readable asset name/label. Required.</param>
/// <param name="SerialNumber">The asset's serial/tag number (unique within the community). Required.</param>
/// <param name="Location">The asset's physical location. Required.</param>
/// <param name="AssetType">Asset-type code from the Asset_Type list. Required.</param>
/// <param name="Category">Optional asset-category code from the Asset_Category list.</param>
/// <param name="UnitId">Optional unit the asset is tied to; null for a common-area/community asset.</param>
/// <param name="WarrantyProvider">Optional warranty provider / vendor.</param>
/// <param name="WarrantyReference">Optional warranty reference / policy number.</param>
/// <param name="WarrantyStartUtc">Optional warranty start.</param>
/// <param name="WarrantyEndUtc">Optional warranty end/expiry.</param>
/// <param name="InstalledOnUtc">Optional install/commission date.</param>
/// <param name="Notes">Optional free-text notes.</param>
public sealed record RegisterAssetRequest(
    Guid CommunityId,
    string Name,
    string SerialNumber,
    string Location,
    string AssetType,
    string? Category,
    Guid? UnitId,
    string? WarrantyProvider,
    string? WarrantyReference,
    DateTime? WarrantyStartUtc,
    DateTime? WarrantyEndUtc,
    DateTime? InstalledOnUtc,
    string? Notes);

/// <summary>
/// Update an asset's editable details and/or lifecycle status (Req 35.1). All fields are optional —
/// a null leaves the corresponding value unchanged. The service validates any supplied type /
/// category against the editable <c>Asset_Type</c> / <c>Asset_Category</c> lists and any supplied
/// <see cref="Status"/> against the editable <c>Asset_Status</c> list (an unknown/retired code →
/// VALIDATION_ERROR, nothing mutated). 21.1 validates the status as a member of the list; it does
/// not enforce a transition allow-list for the asset lifecycle.
/// </summary>
/// <param name="Name">New name, or null to leave unchanged.</param>
/// <param name="Location">New location, or null to leave unchanged.</param>
/// <param name="AssetType">New asset-type code (from Asset_Type), or null to leave unchanged.</param>
/// <param name="Category">New asset-category code (from Asset_Category), or null to leave unchanged.</param>
/// <param name="Status">New lifecycle-status code (from Asset_Status), or null to leave unchanged.</param>
/// <param name="UnitId">New tied unit, or null to leave unchanged (use <see cref="ClearUnit"/> to detach).</param>
/// <param name="ClearUnit">When true, detaches the asset from its unit (sets UnitId null); overrides <see cref="UnitId"/>.</param>
/// <param name="WarrantyProvider">New warranty provider, or null to leave unchanged.</param>
/// <param name="WarrantyReference">New warranty reference, or null to leave unchanged.</param>
/// <param name="WarrantyStartUtc">New warranty start, or null to leave unchanged.</param>
/// <param name="WarrantyEndUtc">New warranty end, or null to leave unchanged.</param>
/// <param name="InstalledOnUtc">New install date, or null to leave unchanged.</param>
/// <param name="Notes">New notes, or null to leave unchanged.</param>
public sealed record UpdateAssetRequest(
    string? Name,
    string? Location,
    string? AssetType,
    string? Category,
    string? Status,
    Guid? UnitId,
    bool ClearUnit,
    string? WarrantyProvider,
    string? WarrantyReference,
    DateTime? WarrantyStartUtc,
    DateTime? WarrantyEndUtc,
    DateTime? InstalledOnUtc,
    string? Notes);

// ---------------------------------------------------------------------------------------------
// Task 21.2 — preventive/corrective work orders with parts/labour (Req 36.1-36.5).
// ---------------------------------------------------------------------------------------------

/// <summary>Read shape for a <c>MaintenancePlan</c> (Req 36.1).</summary>
/// <param name="Id">The plan id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="AssetId">The asset this plan schedules maintenance for.</param>
/// <param name="Name">Human-readable plan name/label.</param>
/// <param name="Description">Optional description of the preventive work, or null.</param>
/// <param name="Frequency">Configurable frequency code (Maintenance_Plan_Frequency).</param>
/// <param name="IntervalDays">The concrete spacing in days between occurrences.</param>
/// <param name="Priority">The Work_Order_Priority code stamped on each generated work order.</param>
/// <param name="NextDueAtUtc">The next occurrence instant the sweep generates a work order for.</param>
/// <param name="LastGeneratedAtUtc">When the sweep last generated a work order, or null.</param>
/// <param name="LastGeneratedForDueAtUtc">The occurrence the last generation was for, or null.</param>
/// <param name="IsActive">Whether the plan is active and swept.</param>
/// <param name="CreatedAtUtc">When the plan was created.</param>
public sealed record MaintenancePlanDto(
    Guid Id,
    Guid CommunityId,
    Guid AssetId,
    string Name,
    string? Description,
    string Frequency,
    int IntervalDays,
    string Priority,
    DateTime NextDueAtUtc,
    DateTime? LastGeneratedAtUtc,
    DateTime? LastGeneratedForDueAtUtc,
    bool IsActive,
    DateTime CreatedAtUtc);

/// <summary>
/// Create a preventive <c>MaintenancePlan</c> for an asset (Req 36.1). The caller supplies the asset,
/// a name, a configurable frequency and a priority for the generated work orders; the first
/// occurrence (<see cref="FirstDueAtUtc"/>) defaults to now when omitted. For a <c>custom</c>
/// frequency the caller supplies <see cref="CustomIntervalDays"/>; for a well-known frequency the
/// interval is resolved from Config_Service (seeded per-frequency fallback), never hardcoded.
/// </summary>
/// <param name="AssetId">The asset to schedule maintenance for (must be in an authorized community). Required.</param>
/// <param name="Name">Human-readable plan name. Required.</param>
/// <param name="Description">Optional description of the preventive work.</param>
/// <param name="Frequency">Frequency code from Maintenance_Plan_Frequency. Required.</param>
/// <param name="Priority">Work_Order_Priority code for generated work orders. Required.</param>
/// <param name="FirstDueAtUtc">The first occurrence instant; defaults to now when null.</param>
/// <param name="CustomIntervalDays">Interval in days for a <c>custom</c> frequency; ignored otherwise.</param>
public sealed record CreateMaintenancePlanRequest(
    Guid AssetId,
    string Name,
    string? Description,
    string Frequency,
    string Priority,
    DateTime? FirstDueAtUtc,
    int? CustomIntervalDays);

/// <summary>
/// Update a <c>MaintenancePlan</c>'s editable details and/or active flag (Req 36.1). All fields are
/// optional — a null leaves the corresponding value unchanged. Changing the frequency re-resolves the
/// interval; <see cref="IsActive"/> pauses/resumes sweeping.
/// </summary>
/// <param name="Name">New name, or null to leave unchanged.</param>
/// <param name="Description">New description, or null to leave unchanged.</param>
/// <param name="Frequency">New frequency code, or null to leave unchanged.</param>
/// <param name="Priority">New generated-work-order priority, or null to leave unchanged.</param>
/// <param name="CustomIntervalDays">New interval for a <c>custom</c> frequency, or null to leave unchanged.</param>
/// <param name="NextDueAtUtc">New next-occurrence instant, or null to leave unchanged.</param>
/// <param name="IsActive">New active flag (pause/resume), or null to leave unchanged.</param>
public sealed record UpdateMaintenancePlanRequest(
    string? Name,
    string? Description,
    string? Frequency,
    string? Priority,
    int? CustomIntervalDays,
    DateTime? NextDueAtUtc,
    bool? IsActive);

/// <summary>Read shape for a <c>WorkOrder</c> (Req 36.1-36.5).</summary>
/// <param name="Id">The work order id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="AssetId">The asset the work order is for.</param>
/// <param name="MaintenancePlanId">The generating plan when preventive, else null.</param>
/// <param name="SourceTicketId">The source ticket when raised from one, else null.</param>
/// <param name="Title">Short title/summary.</param>
/// <param name="Description">Description of the work.</param>
/// <param name="WorkOrderType">Configurable type code (preventive/corrective).</param>
/// <param name="Priority">Configurable priority code.</param>
/// <param name="Status">Configurable status code.</param>
/// <param name="AssignedToUserId">The assigned maintenance-staff user, or null while unassigned.</param>
/// <param name="AssignedAtUtc">When assigned, or null.</param>
/// <param name="CompletedAtUtc">When completed, or null.</param>
/// <param name="ScheduledForUtc">The scheduled occurrence for a preventive order, or null.</param>
/// <param name="CompletionNotes">Completion notes, or null.</param>
/// <param name="CreatedAtUtc">When the work order was created.</param>
public sealed record WorkOrderDto(
    Guid Id,
    Guid CommunityId,
    Guid AssetId,
    Guid? MaintenancePlanId,
    Guid? SourceTicketId,
    string Title,
    string Description,
    string WorkOrderType,
    string Priority,
    string Status,
    Guid? AssignedToUserId,
    DateTime? AssignedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? ScheduledForUtc,
    string? CompletionNotes,
    DateTime CreatedAtUtc);

/// <summary>
/// Raise a corrective <c>WorkOrder</c> directly (Req 36.2). The caller supplies the asset, a title,
/// a description and an optional priority (falls back to the configured community default). An
/// optional <see cref="SourceTicketId"/> links the order to the helpdesk ticket it was raised from.
/// </summary>
/// <param name="AssetId">The asset the work order is for (must be in an authorized community). Required.</param>
/// <param name="Title">Short title/summary. Required.</param>
/// <param name="Description">Description of the work. Required.</param>
/// <param name="Priority">Optional Work_Order_Priority code; defaults to the configured community default.</param>
/// <param name="SourceTicketId">Optional helpdesk ticket this corrective order was raised from.</param>
public sealed record CreateWorkOrderRequest(
    Guid AssetId,
    string Title,
    string Description,
    string? Priority,
    Guid? SourceTicketId);

/// <summary>Assign (or re-assign) a <c>WorkOrder</c> to a maintenance-staff member (Req 36.3).</summary>
/// <param name="AssignedToUserId">The maintenance-staff user to assign the work order to. Required.</param>
public sealed record AssignWorkOrderRequest(Guid AssignedToUserId);

/// <summary>Change a <c>WorkOrder</c>'s status through the configurable transition allow-list (Req 36.1-36.5).</summary>
/// <param name="ToStatus">The target Work_Order_Status code. Required.</param>
/// <param name="CompletionNotes">Optional completion notes recorded when moving to <c>completed</c>.</param>
public sealed record ChangeWorkOrderStatusRequest(string ToStatus, string? CompletionNotes);

/// <summary>Read shape for a <c>WorkOrderPart</c> (Req 36.4).</summary>
public sealed record WorkOrderPartDto(
    Guid Id,
    Guid CommunityId,
    Guid WorkOrderId,
    string PartName,
    string? PartNumber,
    decimal Quantity,
    decimal UnitCost,
    decimal LineTotal);

/// <summary>Record a part/material used against a <c>WorkOrder</c> (Req 36.4). The line total is computed server-side.</summary>
/// <param name="PartName">Human-readable part/material name. Required.</param>
/// <param name="PartNumber">Optional part/SKU reference.</param>
/// <param name="Quantity">Quantity used (non-negative).</param>
/// <param name="UnitCost">Cost per unit (non-negative).</param>
public sealed record AddWorkOrderPartRequest(
    string PartName,
    string? PartNumber,
    decimal Quantity,
    decimal UnitCost);

/// <summary>Read shape for a <c>WorkOrderLabour</c> entry (Req 36.4).</summary>
public sealed record WorkOrderLabourDto(
    Guid Id,
    Guid CommunityId,
    Guid WorkOrderId,
    Guid? WorkerUserId,
    string WorkerName,
    decimal Hours,
    decimal HourlyRate,
    decimal LabourCost,
    string? Notes);

/// <summary>Record a labour entry against a <c>WorkOrder</c> (Req 36.4). The labour cost is computed server-side.</summary>
/// <param name="WorkerUserId">Optional user who performed the labour.</param>
/// <param name="WorkerName">Human-readable worker/technician name. Required.</param>
/// <param name="Hours">Hours spent (non-negative).</param>
/// <param name="HourlyRate">Hourly rate (non-negative).</param>
/// <param name="Notes">Optional note describing the labour.</param>
public sealed record AddWorkOrderLabourRequest(
    Guid? WorkerUserId,
    string WorkerName,
    decimal Hours,
    decimal HourlyRate,
    string? Notes);

/// <summary>Read shape for an <c>EquipmentHistoryEntry</c> in an asset's equipment history (Req 36.5).</summary>
public sealed record EquipmentHistoryEntryDto(
    Guid Id,
    Guid CommunityId,
    Guid AssetId,
    Guid? WorkOrderId,
    string EventType,
    string Summary,
    string? Detail,
    string? ActingUserId,
    DateTime OccurredAtUtc);

// ---------------------------------------------------------------------------------------------
// Task 21.3 — inspections, AMC contracts and meter history (Req 37.1-37.5).
// ---------------------------------------------------------------------------------------------

/// <summary>Read shape for one line of a completed inspection (Req 37.1).</summary>
public sealed record InspectionChecklistItemDto(
    Guid Id,
    string ItemText,
    string Result,
    string? Remark,
    int SortOrder);

/// <summary>Read shape for a completed <c>InspectionChecklist</c> (Req 37.1).</summary>
/// <param name="Id">The inspection id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="AssetId">The inspected asset.</param>
/// <param name="WorkOrderId">The work order the inspection was performed under, or null.</param>
/// <param name="Name">Human-readable checklist name/label.</param>
/// <param name="OverallResult">Configurable overall-result code (Inspection_Result).</param>
/// <param name="InspectedByUserId">The user who performed the inspection, or null for a system actor.</param>
/// <param name="InspectedAtUtc">When the inspection was completed.</param>
/// <param name="Notes">Optional notes, or null.</param>
/// <param name="Items">The checklist items and their results.</param>
public sealed record InspectionChecklistDto(
    Guid Id,
    Guid CommunityId,
    Guid AssetId,
    Guid? WorkOrderId,
    string Name,
    string OverallResult,
    string? InspectedByUserId,
    DateTime InspectedAtUtc,
    string? Notes,
    IReadOnlyList<InspectionChecklistItemDto> Items);

/// <summary>One checklist line to record on a new inspection (Req 37.1).</summary>
/// <param name="ItemText">The checklist line/question. Required.</param>
/// <param name="Result">The per-item result code (from Inspection_Result). Required.</param>
/// <param name="Remark">Optional per-item remark/observation.</param>
public sealed record InspectionChecklistItemInput(string ItemText, string Result, string? Remark);

/// <summary>
/// Complete an <c>InspectionChecklist</c> against an asset (optionally under a work order, Req 37.1).
/// The caller supplies the asset, a checklist name, the overall result and the per-item results. The
/// service validates the overall result (and each item result) against the editable
/// <c>Inspection_Result</c> list, stamps the inspecting user + timestamp, and appends a single entry
/// to the asset's equipment history (Req 37.5). The inspected-at instant defaults to now when omitted.
/// </summary>
/// <param name="AssetId">The inspected asset (must be in an authorized community). Required.</param>
/// <param name="WorkOrderId">Optional work order the inspection was performed under.</param>
/// <param name="Name">Human-readable checklist name. Required.</param>
/// <param name="OverallResult">Overall-result code from Inspection_Result. Required.</param>
/// <param name="InspectedAtUtc">When the inspection was completed; defaults to now when null.</param>
/// <param name="Notes">Optional notes.</param>
/// <param name="Items">The checklist items and their results (at least one required).</param>
public sealed record RecordInspectionRequest(
    Guid AssetId,
    Guid? WorkOrderId,
    string Name,
    string OverallResult,
    DateTime? InspectedAtUtc,
    string? Notes,
    IReadOnlyList<InspectionChecklistItemInput> Items);

/// <summary>Read shape for an <c>AmcContract</c> (Req 37.2).</summary>
/// <param name="Id">The contract id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="AssetId">The covered asset.</param>
/// <param name="ContractReference">Human-readable contract name/reference.</param>
/// <param name="VendorName">The maintenance vendor.</param>
/// <param name="VendorContact">Optional vendor contact, or null.</param>
/// <param name="StartDateUtc">When coverage begins.</param>
/// <param name="EndDateUtc">When coverage ends / expires.</param>
/// <param name="ResponsibleUserId">The user responsible for the contract (notified on expiry).</param>
/// <param name="Status">Configurable status code (Amc_Contract_Status).</param>
/// <param name="ContractValue">Optional annual contract value, or null.</param>
/// <param name="Notes">Optional notes, or null.</param>
/// <param name="CreatedAtUtc">When the contract was created.</param>
public sealed record AmcContractDto(
    Guid Id,
    Guid CommunityId,
    Guid AssetId,
    string ContractReference,
    string VendorName,
    string? VendorContact,
    DateTime StartDateUtc,
    DateTime EndDateUtc,
    Guid ResponsibleUserId,
    string Status,
    decimal? ContractValue,
    string? Notes,
    DateTime CreatedAtUtc);

/// <summary>
/// Record an <c>AmcContract</c> covering an asset with a vendor (Req 37.2). The caller supplies the
/// asset, a contract reference, the vendor, the start/end dates and the responsible user. The service
/// validates the dates (end after start), sets the status to <c>active</c>, and leaves the contract
/// ready for the expiry-reminder sweep (Req 37.3).
/// </summary>
/// <param name="AssetId">The covered asset (must be in an authorized community). Required.</param>
/// <param name="ContractReference">Human-readable contract reference. Required.</param>
/// <param name="VendorName">The maintenance vendor. Required.</param>
/// <param name="VendorContact">Optional vendor contact.</param>
/// <param name="StartDateUtc">When coverage begins. Required.</param>
/// <param name="EndDateUtc">When coverage ends / expires (must be after start). Required.</param>
/// <param name="ResponsibleUserId">The user responsible for the contract (notified on expiry). Required.</param>
/// <param name="ContractValue">Optional annual contract value (non-negative).</param>
/// <param name="Notes">Optional notes.</param>
public sealed record RecordAmcContractRequest(
    Guid AssetId,
    string ContractReference,
    string VendorName,
    string? VendorContact,
    DateTime StartDateUtc,
    DateTime EndDateUtc,
    Guid ResponsibleUserId,
    decimal? ContractValue,
    string? Notes);

/// <summary>
/// Update an <c>AmcContract</c>'s editable details and/or status (Req 37.2). All fields are optional —
/// a null leaves the corresponding value unchanged. Extending <see cref="EndDateUtc"/> (a renewal)
/// re-arms the expiry reminder for the next window. A supplied <see cref="Status"/> is validated
/// against the editable <c>Amc_Contract_Status</c> list.
/// </summary>
/// <param name="ContractReference">New reference, or null to leave unchanged.</param>
/// <param name="VendorName">New vendor, or null to leave unchanged.</param>
/// <param name="VendorContact">New vendor contact, or null to leave unchanged.</param>
/// <param name="EndDateUtc">New end/expiry date (must be after start), or null to leave unchanged.</param>
/// <param name="ResponsibleUserId">New responsible user, or null to leave unchanged.</param>
/// <param name="Status">New status code (from Amc_Contract_Status), or null to leave unchanged.</param>
/// <param name="ContractValue">New contract value, or null to leave unchanged.</param>
/// <param name="Notes">New notes, or null to leave unchanged.</param>
public sealed record UpdateAmcContractRequest(
    string? ContractReference,
    string? VendorName,
    string? VendorContact,
    DateTime? EndDateUtc,
    Guid? ResponsibleUserId,
    string? Status,
    decimal? ContractValue,
    string? Notes);

/// <summary>Read shape for a <c>MeterReading</c> (Req 37.4).</summary>
/// <param name="Id">The reading id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="AssetId">The asset the meter is on.</param>
/// <param name="MeterType">Configurable meter-type code (Meter_Type).</param>
/// <param name="Unit">Configurable unit code (Meter_Unit).</param>
/// <param name="Value">The recorded counter value.</param>
/// <param name="ReadingAtUtc">When the reading was taken.</param>
/// <param name="RecordedByUserId">The user who recorded the reading, or null for a system actor.</param>
/// <param name="Notes">Optional note, or null.</param>
public sealed record MeterReadingDto(
    Guid Id,
    Guid CommunityId,
    Guid AssetId,
    string MeterType,
    string Unit,
    decimal Value,
    DateTime ReadingAtUtc,
    string? RecordedByUserId,
    string? Notes);

/// <summary>
/// Record a <c>MeterReading</c> for an asset's equipment counter (Req 37.4). The caller supplies the
/// asset, the meter type, the unit and the value. The service validates the type/unit against the
/// editable <c>Meter_Type</c> / <c>Meter_Unit</c> lists, rejects a value below the latest recorded
/// reading for the same asset + meter type (equipment counters are non-decreasing), stamps the
/// recording user + timestamp, and appends a single entry to the asset's equipment history (Req 37.5).
/// The reading-at instant defaults to now when omitted.
/// </summary>
/// <param name="AssetId">The asset the meter is on (must be in an authorized community). Required.</param>
/// <param name="MeterType">Meter-type code from Meter_Type. Required.</param>
/// <param name="Unit">Unit code from Meter_Unit. Required.</param>
/// <param name="Value">The recorded counter value (non-negative, &gt;= the latest reading).</param>
/// <param name="ReadingAtUtc">When the reading was taken; defaults to now when null.</param>
/// <param name="Notes">Optional note.</param>
public sealed record RecordMeterReadingRequest(
    Guid AssetId,
    string MeterType,
    string Unit,
    decimal Value,
    DateTime? ReadingAtUtc,
    string? Notes);
