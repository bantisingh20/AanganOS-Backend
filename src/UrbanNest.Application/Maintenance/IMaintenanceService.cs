namespace UrbanNest.Application.Maintenance;

/// <summary>
/// Maintenance_Service (Task 21.1, Req 35.1–35.4): the asset register with QR identification and a
/// configurable lifecycle status, plus the tenant-scoped read surface. This is the single cohesive
/// seam for the Maintenance area — preventive/corrective work orders with parts/labour (Task 21.2),
/// inspections / AMC contracts / meter history (Task 21.3) and the HTTP controllers (Task 21.4)
/// extend this same interface in later tasks (mirrors how <c>IHelpdeskService</c> is extended across
/// its tasks).
///
/// <para><b>Registration (Req 35.1, 35.2).</b> A Facility Manager with the <c>Create</c> permission
/// on <c>Asset</c> registers an asset for a community they are authorized for: the serial number is
/// unique within the community (a duplicate → CONFLICT via the DB unique index, no read-then-write),
/// the type/category validate against the editable <c>Asset_Type</c> / <c>Asset_Category</c> lists,
/// the lifecycle status starts <c>active</c>, and the service generates an opaque, community-unique
/// QR token that identifies the asset when scanned (DB <c>Unique(CommunityId, QrToken)</c> is the
/// source of truth, regenerate-on-collision). Audited.</para>
///
/// <para><b>Lifecycle + edit (Req 35.1).</b> An <c>Edit</c> on <c>Asset</c> updates the editable
/// details and/or drives the lifecycle status: any supplied status is validated as an active
/// <c>Asset_Status</c> code (21.1 does not enforce a transition allow-list). The update records
/// exactly once under the asset <c>RowVersion</c> (a concurrency loser → CONFLICT centrally).</para>
///
/// <para><b>Tenant scope + soft-delete (Req 35.3, 35.4).</b> <c>Asset</c> is a tenant-owned entity
/// (NOT <c>IUnitScoped</c> — community infrastructure), so reads are bounded to the caller's
/// authorized communities by the global tenant filter. The paginated/filtered/sorted/searchable
/// list uses an allow-listed query config + <c>AsNoTracking</c>. A delete is a Soft_Delete: the row
/// stays in the table (traceable) but is hidden from ordinary reads. Mutations are audited.</para>
/// </summary>
public interface IMaintenanceService
{
    /// <summary>
    /// Register an asset in the maintenance register (Req 35.1, 35.2). Requires the <c>Create</c>
    /// permission on <c>Asset</c> + an authorized community; validates the type/category against the
    /// configurable lists; enforces serial-number uniqueness within the community via the DB unique
    /// index (a duplicate → CONFLICT, no read-then-write); sets the status to <c>active</c>; and
    /// generates an opaque, community-unique QR token (regenerate-on-collision against the DB
    /// <c>Unique(CommunityId, QrToken)</c> index). Returns the created asset. Audited.
    /// </summary>
    Task<AssetDto> RegisterAssetAsync(RegisterAssetRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an asset's editable details and/or lifecycle status (Req 35.1). Requires the
    /// <c>Edit</c> permission on <c>Asset</c> + an authorized community. Validates any supplied
    /// type/category/status against the configurable <c>Asset_Type</c> / <c>Asset_Category</c> /
    /// <c>Asset_Status</c> lists (an unknown/retired code → VALIDATION_ERROR, nothing mutated). The
    /// update records exactly once under the asset <c>RowVersion</c> (a concurrency loser →
    /// CONFLICT). Returns the updated asset. Audited.
    /// </summary>
    Task<AssetDto> UpdateAssetAsync(Guid assetId, UpdateAssetRequest request, CancellationToken cancellationToken = default);

    /// <summary>Fetch one asset by id, tenant-scoped; unknown/foreign/soft-deleted id → null (Req 35.3, 35.4).</summary>
    Task<AssetDto?> GetAssetAsync(Guid assetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated/filtered/sorted/searchable asset list (Req 35.3). Requires the <c>View</c>
    /// permission on <c>Asset</c>. Tenant-scoped by the global query filter (only assets in the
    /// caller's authorized communities; soft-deleted/archived assets are hidden); the allow-listed
    /// <c>ListQueryConfiguration</c> fixes which fields a client may sort/filter/search on, so no
    /// arbitrary client LINQ reaches the query. <c>AsNoTracking</c> projection, page size clamped by
    /// the shared policy — mirrors the other module list endpoints.
    /// </summary>
    Task<UrbanNest.Contracts.Envelope.PagedData<AssetDto>> ListAssetsAsync(
        UrbanNest.Application.Querying.ListQueryParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-delete (archive) an asset (Req 35.4). Requires the <c>Delete</c> permission on
    /// <c>Asset</c> + an authorized community. The asset is marked deleted (the row stays in the
    /// table and remains traceable, hidden from ordinary reads); the delete records under the asset
    /// <c>RowVersion</c> (a concurrency loser → CONFLICT). An unknown/foreign id → not found.
    /// Audited.
    /// </summary>
    Task DeleteAssetAsync(Guid assetId, CancellationToken cancellationToken = default);

    // --- Task 21.2: preventive/corrective work orders with parts/labour (Req 36.1-36.5) -----------

    /// <summary>
    /// Create a preventive <c>MaintenancePlan</c> for an asset (Req 36.1). Requires <c>Create</c> on
    /// <c>MaintenancePlan</c> + an authorized community (the asset must be in it). Validates the
    /// frequency against the configurable <c>Maintenance_Plan_Frequency</c> list and resolves the
    /// interval in days (from Config_Service, or the request's custom interval for a <c>custom</c>
    /// frequency — never hardcoded), validates the generated-work-order priority against
    /// <c>Work_Order_Priority</c>, and sets the first occurrence (defaulting to now). Returns the
    /// created plan. Audited.
    /// </summary>
    Task<MaintenancePlanDto> CreateMaintenancePlanAsync(CreateMaintenancePlanRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a <c>MaintenancePlan</c>'s editable details and/or active flag (Req 36.1). Requires
    /// <c>Edit</c> on <c>MaintenancePlan</c>. Validates any supplied frequency/priority against the
    /// configurable lists; records exactly once under the plan <c>RowVersion</c> (loser → CONFLICT).
    /// Returns the updated plan. Audited.
    /// </summary>
    Task<MaintenancePlanDto> UpdateMaintenancePlanAsync(Guid planId, UpdateMaintenancePlanRequest request, CancellationToken cancellationToken = default);

    /// <summary>Fetch one maintenance plan by id, tenant-scoped; unknown/foreign/soft-deleted id → null (Req 36.1).</summary>
    Task<MaintenancePlanDto?> GetMaintenancePlanAsync(Guid planId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated/filtered/sorted/searchable maintenance-plan list (Req 36.1). Requires <c>View</c> on
    /// <c>MaintenancePlan</c>. Tenant-scoped by the global filter; allow-listed query config +
    /// <c>AsNoTracking</c>.
    /// </summary>
    Task<UrbanNest.Contracts.Envelope.PagedData<MaintenancePlanDto>> ListMaintenancePlansAsync(
        UrbanNest.Application.Querying.ListQueryParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate preventive work orders for every active plan whose next occurrence is due (Req 36.1).
    /// Invoked by the background preventive sweep worker under a cross-tenant super-admin context.
    /// Processes a bounded batch (clamped by the service), and for each due plan generates ONE work
    /// order for the current occurrence, stamps the plan's last-generated bookkeeping and advances
    /// <c>NextDueAtUtc</c> by the plan's interval — all under the plan <c>RowVersion</c> and saved per
    /// plan, so two concurrent sweeps racing the same due plan can't both generate for the same
    /// occurrence (the loser → CONFLICT and is skipped). Returns the number of work orders generated so
    /// the worker knows whether to keep sweeping or sleep. Each generation is audited.
    /// </summary>
    Task<int> GenerateDuePreventiveWorkOrdersAsync(int maxPlans, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raise a corrective <c>WorkOrder</c> directly (Req 36.2). Requires <c>Create</c> on
    /// <c>WorkOrder</c> + an authorized community (the asset must be in it). Validates the priority
    /// against <c>Work_Order_Priority</c> (defaulting to the configured community default), sets the
    /// type to <c>corrective</c> and the status to <c>open</c>. Returns the created work order. Audited.
    /// </summary>
    Task<WorkOrderDto> CreateWorkOrderAsync(CreateWorkOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assign (or re-assign) a <c>WorkOrder</c> to a maintenance-staff member (Req 36.3). Requires
    /// <c>Assign</c> on <c>WorkOrder</c>. The assignee must be a staff user holding <c>Edit</c> on
    /// <c>WorkOrder</c> in the community (RBAC data, no hardcoded recipient). Moves the status to
    /// <c>assigned</c> through the configurable transition allow-list, records the assignment, and
    /// enqueues a <c>work-order.assigned</c> push to the assignee via the transactional outbox — all
    /// committed atomically under the work-order <c>RowVersion</c> (loser → CONFLICT). Returns the
    /// updated work order. Audited.
    /// </summary>
    Task<WorkOrderDto> AssignWorkOrderAsync(Guid workOrderId, AssignWorkOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Change a <c>WorkOrder</c>'s status (Req 36.1-36.5). Requires <c>Edit</c> on <c>WorkOrder</c>.
    /// The target must be a valid <c>Work_Order_Status</c> code and the <c>from-&gt;to</c> transition
    /// must be in the editable <c>Work_Order_Status_Transition</c> allow-list (an illegal transition →
    /// VALIDATION_ERROR, nothing mutated — no hardcoded table). On the transition into <c>completed</c>
    /// the completion instant is stamped and a single append-only <c>EquipmentHistoryEntry</c> is
    /// appended to the asset's history (Req 36.5), committed atomically under the work-order
    /// <c>RowVersion</c> (loser → CONFLICT). Returns the updated work order. Audited.
    /// </summary>
    Task<WorkOrderDto> ChangeWorkOrderStatusAsync(Guid workOrderId, ChangeWorkOrderStatusRequest request, CancellationToken cancellationToken = default);

    /// <summary>Fetch one work order by id, tenant-scoped; unknown/foreign/soft-deleted id → null (Req 36.2).</summary>
    Task<WorkOrderDto?> GetWorkOrderAsync(Guid workOrderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated/filtered/sorted/searchable work-order list (Req 36.2, 36.3). Requires <c>View</c> on
    /// <c>WorkOrder</c>. Tenant-scoped by the global filter; allow-listed query config +
    /// <c>AsNoTracking</c>.
    /// </summary>
    Task<UrbanNest.Contracts.Envelope.PagedData<WorkOrderDto>> ListWorkOrdersAsync(
        UrbanNest.Application.Querying.ListQueryParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Record a part/material used against a <c>WorkOrder</c> (Req 36.4). Requires <c>Edit</c> on
    /// <c>WorkOrder</c> (parts are managed via the work order's Edit grant). Validates the quantity and
    /// unit cost are non-negative and computes the line total server-side. The part carries the work
    /// order's community. Returns the recorded part. Audited.
    /// </summary>
    Task<WorkOrderPartDto> AddWorkOrderPartAsync(Guid workOrderId, AddWorkOrderPartRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Record a labour entry against a <c>WorkOrder</c> (Req 36.4). Requires <c>Edit</c> on
    /// <c>WorkOrder</c>. Validates the hours and hourly rate are non-negative and computes the labour
    /// cost server-side. The labour entry carries the work order's community. Returns the recorded
    /// entry. Audited.
    /// </summary>
    Task<WorkOrderLabourDto> AddWorkOrderLabourAsync(Guid workOrderId, AddWorkOrderLabourRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// List a work order's recorded parts (Req 36.4). Requires <c>View</c> on <c>WorkOrder</c>.
    /// Tenant-scoped; chronological indexed read; an unknown/foreign id → empty list.
    /// </summary>
    Task<IReadOnlyList<WorkOrderPartDto>> GetWorkOrderPartsAsync(Guid workOrderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List a work order's recorded labour (Req 36.4). Requires <c>View</c> on <c>WorkOrder</c>.
    /// Tenant-scoped; chronological indexed read; an unknown/foreign id → empty list.
    /// </summary>
    Task<IReadOnlyList<WorkOrderLabourDto>> GetWorkOrderLabourAsync(Guid workOrderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read an asset's append-only equipment history in chronological order (Req 36.5). Requires
    /// <c>View</c> on <c>WorkOrder</c>. Tenant-scoped by the global filter; <c>AsNoTracking</c>; an
    /// unknown/foreign asset id → empty list.
    /// </summary>
    Task<IReadOnlyList<EquipmentHistoryEntryDto>> GetEquipmentHistoryAsync(Guid assetId, CancellationToken cancellationToken = default);

    // --- Task 21.3: inspections, AMC contracts and meter history (Req 37.1-37.5) ------------------

    /// <summary>
    /// Complete an <c>InspectionChecklist</c> against an asset (optionally under a work order, Req 37.1).
    /// Requires the <c>Create</c> permission on <c>InspectionChecklist</c> + an authorized community (the
    /// asset must be in it). Validates the overall result and each item result against the editable
    /// <c>Inspection_Result</c> list (an unknown code → VALIDATION_ERROR, nothing stored), stamps the
    /// inspecting user (from the tenant context) and the timestamp, and appends a SINGLE append-only
    /// <c>EquipmentHistoryEntry</c> (<c>inspection_completed</c>) to the asset's history — committed
    /// atomically. Returns the stored inspection with its items. Audited.
    /// </summary>
    Task<InspectionChecklistDto> RecordInspectionAsync(RecordInspectionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Fetch one inspection (with its items) by id, tenant-scoped; unknown/foreign/soft-deleted id → null (Req 37.1).</summary>
    Task<InspectionChecklistDto?> GetInspectionAsync(Guid inspectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List an asset's completed inspections in chronological order (Req 37.1). Requires <c>View</c> on
    /// <c>InspectionChecklist</c>. Tenant-scoped by the global filter; <c>AsNoTracking</c>; an
    /// unknown/foreign asset id → empty list. Includes each inspection's items.
    /// </summary>
    Task<IReadOnlyList<InspectionChecklistDto>> GetAssetInspectionsAsync(Guid assetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Record an <c>AmcContract</c> covering an asset with a vendor (Req 37.2). Requires the
    /// <c>Create</c> permission on <c>AmcContract</c> + an authorized community (the asset must be in
    /// it). Validates the dates (end strictly after start) and the non-negative contract value, sets
    /// the status to <c>active</c>, and leaves the contract ready for the expiry-reminder sweep. Returns
    /// the created contract. Audited.
    /// </summary>
    Task<AmcContractDto> RecordAmcContractAsync(RecordAmcContractRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an <c>AmcContract</c>'s editable details and/or status (Req 37.2). Requires <c>Edit</c>
    /// on <c>AmcContract</c>. Validates any supplied status against the editable
    /// <c>Amc_Contract_Status</c> list and that the end date stays after the start date; records
    /// exactly once under the contract <c>RowVersion</c> (loser → CONFLICT). Extending the end date (a
    /// renewal) re-arms the expiry reminder for the next window. Returns the updated contract. Audited.
    /// </summary>
    Task<AmcContractDto> UpdateAmcContractAsync(Guid contractId, UpdateAmcContractRequest request, CancellationToken cancellationToken = default);

    /// <summary>Fetch one AMC contract by id, tenant-scoped; unknown/foreign/soft-deleted id → null (Req 37.2).</summary>
    Task<AmcContractDto?> GetAmcContractAsync(Guid contractId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List an asset's AMC contracts (Req 37.2). Requires <c>View</c> on <c>AmcContract</c>.
    /// Tenant-scoped; <c>AsNoTracking</c>; indexed read ordered by end date; an unknown/foreign asset
    /// id → empty list.
    /// </summary>
    Task<IReadOnlyList<AmcContractDto>> GetAssetAmcContractsAsync(Guid assetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send expiry reminders for every active AMC contract currently within its configured
    /// expiry-reminder window and not yet reminded for its current end date (Req 37.3). Invoked by the
    /// background AMC-expiry sweep worker under a cross-tenant super-admin context. Processes a bounded
    /// batch (clamped by the service), and for each due contract enqueues an <c>amc.expiry-reminder</c>
    /// notification to the responsible user via the transactional outbox AND stamps the at-most-once
    /// <c>ExpiryReminderSentForEndDateUtc</c> marker to the contract's end date — all under the contract
    /// <c>RowVersion</c> and saved per contract, so two concurrent sweeps racing the same contract can't
    /// both remind for the same window (the loser → CONFLICT and is skipped). The reminder-window days
    /// are resolved per community via Config_Service (never hardcoded). Returns the number of reminders
    /// sent so the worker knows whether to keep sweeping or sleep. Each reminder is audited.
    /// </summary>
    Task<int> SendDueAmcExpiryRemindersAsync(int maxContracts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Record a <c>MeterReading</c> for an asset's equipment counter (Req 37.4). Requires the
    /// <c>Create</c> permission on <c>MeterReading</c> + an authorized community (the asset must be in
    /// it). Validates the meter type/unit against the editable <c>Meter_Type</c> / <c>Meter_Unit</c>
    /// lists and rejects a value below the latest recorded reading for the same asset + meter type
    /// (equipment counters are non-decreasing → VALIDATION_ERROR, nothing stored), stamps the recording
    /// user + timestamp, and appends a SINGLE append-only <c>EquipmentHistoryEntry</c>
    /// (<c>meter_reading_recorded</c>) to the asset's history — committed atomically. Returns the stored
    /// reading. Audited.
    /// </summary>
    Task<MeterReadingDto> RecordMeterReadingAsync(RecordMeterReadingRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// List an asset's meter readings for a meter type in chronological order (Req 37.4, 37.5). Requires
    /// <c>View</c> on <c>MeterReading</c>. Tenant-scoped; <c>AsNoTracking</c>; indexed chronological
    /// read; an unknown/foreign asset id → empty list. A null <paramref name="meterType"/> returns all
    /// of the asset's readings across meters.
    /// </summary>
    Task<IReadOnlyList<MeterReadingDto>> GetAssetMeterReadingsAsync(Guid assetId, string? meterType = null, CancellationToken cancellationToken = default);
}
