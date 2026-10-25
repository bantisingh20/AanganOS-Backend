using System.ComponentModel.DataAnnotations;
using UrbanNest.Api.Security;
using UrbanNest.Api.Validation;
using UrbanNest.Application.Exceptions;
using UrbanNest.Application.Maintenance;
using UrbanNest.Application.Querying;
using UrbanNest.Contracts.Envelope;
using UrbanNest.Domain.Identity;

namespace UrbanNest.Api.Endpoints;

/// <summary>
/// HTTP surface over the Phase 5 Maintenance_Service (<see cref="IMaintenanceService"/>) — the asset
/// register with QR identification + lifecycle (Req 35.x), preventive/corrective work orders with
/// parts/labour + equipment history (Req 36.x) and inspections / AMC contracts / meter readings (Req
/// 37.x). Mirrors the established minimal-API style of <see cref="ParcelEndpoints"/> /
/// <see cref="GateEndpoints"/>: the <see cref="ApiResult{T}"/> envelope (Req 9.1), a
/// <see cref="RequirePermissionAttribute"/> on the correct (Action, ResourceType) (Req 4.1),
/// <see cref="DataAnnotationsValidationFilter"/> on write bodies (Req 9.2), the "write" rate-limit
/// policy on every mutation (Req 65.5) and the shared paginated list infra
/// (<see cref="ListQueryParameters"/> + the service's allow-listed <c>ListQueryConfiguration</c>s,
/// Req 9.3). Lists are scoped to the caller's authorized communities by the EF global tenant query
/// filter — no bespoke predicate here.
///
/// <para><b>Permission pairs mirror the service's defense-in-depth checks (steering: security
/// first, no hardcoding).</b> Asset register/edit/delete = <c>(Create|Edit|Delete, Asset)</c>, asset
/// reads = <c>(View, Asset)</c>; plan create/edit = <c>(Create|Edit, MaintenancePlan)</c>, reads =
/// <c>(View, MaintenancePlan)</c>; work-order create/status/parts/labour = <c>(Create|Edit,
/// WorkOrder)</c>, assign = <c>(Assign, WorkOrder)</c>, reads + equipment history =
/// <c>(View, WorkOrder)</c>; inspection record = <c>(Create, InspectionChecklist)</c>, reads =
/// <c>(View, InspectionChecklist)</c>; AMC create/edit = <c>(Create|Edit, AmcContract)</c>, reads =
/// <c>(View, AmcContract)</c>; meter record = <c>(Create, MeterReading)</c>, reads =
/// <c>(View, MeterReading)</c>. The service re-checks the same permission as defense-in-depth;
/// CONFLICT / VALIDATION_ERROR / NOT_FOUND / FORBIDDEN surface through the central exception envelope.</para>
///
/// <para><b>Worker-only operations are intentionally off the HTTP surface.</b>
/// <see cref="IMaintenanceService.GenerateDuePreventiveWorkOrdersAsync"/> and
/// <see cref="IMaintenanceService.SendDueAmcExpiryRemindersAsync"/> run under the background sweep
/// workers' cross-tenant super-admin context, so they are NOT exposed as endpoints here.</para>
/// </summary>
public static class MaintenanceEndpoints
{
    public static IEndpointRouteBuilder MapMaintenanceEndpoints(this IEndpointRouteBuilder app)
    {
        MapAssetEndpoints(app);
        MapMaintenancePlanEndpoints(app);
        MapWorkOrderEndpoints(app);
        MapInspectionEndpoints(app);
        MapAmcContractEndpoints(app);
        MapMeterReadingEndpoints(app);
        return app;
    }

    // --- Assets (/api/v1/assets) — Req 35.x ------------------------------------------------------
    private static void MapAssetEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/assets");

        // POST — register an asset (Req 35.1, 35.2). Duplicate serial → CONFLICT; QR token generated.
        group.MapPost("/", async (
                RegisterAssetBody body,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var dto = await maintenance.RegisterAssetAsync(new RegisterAssetRequest(
                    body.CommunityId,
                    body.Name!,
                    body.SerialNumber!,
                    body.Location!,
                    body.AssetType!,
                    body.Category,
                    body.UnitId,
                    body.WarrantyProvider,
                    body.WarrantyReference,
                    body.WarrantyStartUtc,
                    body.WarrantyEndUtc,
                    body.InstalledOnUtc,
                    body.Notes), ct);
                return Results.Ok(ApiResult<AssetDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "Asset"));

        // GET — paginated/filter/sort/search list, tenant-scoped (Req 35.3).
        group.MapGet("/", async (
                string? search,
                string? sort,
                string? status,
                string? assetType,
                string? category,
                Guid? communityId,
                Guid? unitId,
                int? page,
                int? pageSize,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                AddEquals(filters, "status", status);
                AddEquals(filters, "assetType", assetType);
                AddEquals(filters, "category", category);
                AddEquals(filters, "communityId", communityId);
                AddEquals(filters, "unitId", unitId);

                var data = await maintenance.ListAssetsAsync(BuildParameters(page, pageSize, search, sort, filters), ct);
                return Results.Ok(ApiResult<PagedData<AssetDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Asset"));

        // GET /{id} — fetch one, tenant-scoped. Unknown/foreign id → NOT_FOUND.
        group.MapGet("/{id:guid}", async (
                Guid id,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var dto = await maintenance.GetAssetAsync(id, ct)
                    ?? throw new NotFoundException("Asset not found.");
                return Results.Ok(ApiResult<AssetDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Asset"));

        // PUT /{id} — update the asset's editable details and/or lifecycle status (Req 35.1).
        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdateAssetBody body,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var dto = await maintenance.UpdateAssetAsync(id, new UpdateAssetRequest(
                    body.Name,
                    body.Location,
                    body.AssetType,
                    body.Category,
                    body.Status,
                    body.UnitId,
                    body.ClearUnit,
                    body.WarrantyProvider,
                    body.WarrantyReference,
                    body.WarrantyStartUtc,
                    body.WarrantyEndUtc,
                    body.InstalledOnUtc,
                    body.Notes), ct);
                return Results.Ok(ApiResult<AssetDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Asset"));

        // DELETE /{id} — soft-delete (archive) the asset (Req 35.4).
        group.MapDelete("/{id:guid}", async (
                Guid id,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                await maintenance.DeleteAssetAsync(id, ct);
                return Results.Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Delete, "Asset"));

        // GET /{id}/equipment-history — the asset's append-only equipment history (Req 36.5).
        group.MapGet("/{id:guid}/equipment-history", async (
                Guid id,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var items = await maintenance.GetEquipmentHistoryAsync(id, ct);
                return Results.Ok(ApiResult<IReadOnlyList<EquipmentHistoryEntryDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "WorkOrder"));

        // GET /{id}/inspections — the asset's completed inspections, chronological (Req 37.1).
        group.MapGet("/{id:guid}/inspections", async (
                Guid id,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var items = await maintenance.GetAssetInspectionsAsync(id, ct);
                return Results.Ok(ApiResult<IReadOnlyList<InspectionChecklistDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "InspectionChecklist"));

        // GET /{id}/amc-contracts — the asset's AMC contracts, ordered by end date (Req 37.2).
        group.MapGet("/{id:guid}/amc-contracts", async (
                Guid id,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var items = await maintenance.GetAssetAmcContractsAsync(id, ct);
                return Results.Ok(ApiResult<IReadOnlyList<AmcContractDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "AmcContract"));

        // GET /{id}/meter-readings — the asset's meter readings, chronological; optional meterType (Req 37.4, 37.5).
        group.MapGet("/{id:guid}/meter-readings", async (
                Guid id,
                string? meterType,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var items = await maintenance.GetAssetMeterReadingsAsync(id, meterType, ct);
                return Results.Ok(ApiResult<IReadOnlyList<MeterReadingDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "MeterReading"));
    }

    // --- Maintenance plans (/api/v1/maintenance-plans) — Req 36.1 --------------------------------
    private static void MapMaintenancePlanEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/maintenance-plans");

        // POST — create a preventive maintenance plan for an asset (Req 36.1).
        group.MapPost("/", async (
                CreateMaintenancePlanBody body,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var dto = await maintenance.CreateMaintenancePlanAsync(new CreateMaintenancePlanRequest(
                    body.AssetId,
                    body.Name!,
                    body.Description,
                    body.Frequency!,
                    body.Priority!,
                    body.FirstDueAtUtc,
                    body.CustomIntervalDays), ct);
                return Results.Ok(ApiResult<MaintenancePlanDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "MaintenancePlan"));

        // GET — paginated/filter/sort/search list, tenant-scoped (Req 36.1).
        group.MapGet("/", async (
                string? search,
                string? sort,
                string? frequency,
                string? priority,
                Guid? assetId,
                bool? isActive,
                int? page,
                int? pageSize,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                AddEquals(filters, "frequency", frequency);
                AddEquals(filters, "priority", priority);
                AddEquals(filters, "assetId", assetId);
                if (isActive is { } a)
                {
                    filters.Add(new FilterTerm("isActive", FilterOperator.Equals, a ? "true" : "false"));
                }

                var data = await maintenance.ListMaintenancePlansAsync(BuildParameters(page, pageSize, search, sort, filters), ct);
                return Results.Ok(ApiResult<PagedData<MaintenancePlanDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "MaintenancePlan"));

        // GET /{id} — fetch one, tenant-scoped. Unknown/foreign id → NOT_FOUND.
        group.MapGet("/{id:guid}", async (
                Guid id,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var dto = await maintenance.GetMaintenancePlanAsync(id, ct)
                    ?? throw new NotFoundException("Maintenance plan not found.");
                return Results.Ok(ApiResult<MaintenancePlanDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "MaintenancePlan"));

        // PUT /{id} — update the plan's editable details and/or active flag (Req 36.1).
        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdateMaintenancePlanBody body,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var dto = await maintenance.UpdateMaintenancePlanAsync(id, new UpdateMaintenancePlanRequest(
                    body.Name,
                    body.Description,
                    body.Frequency,
                    body.Priority,
                    body.CustomIntervalDays,
                    body.NextDueAtUtc,
                    body.IsActive), ct);
                return Results.Ok(ApiResult<MaintenancePlanDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "MaintenancePlan"));
    }

    // --- Work orders (/api/v1/work-orders) — Req 36.2-36.5 ---------------------------------------
    private static void MapWorkOrderEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/work-orders");

        // POST — raise a corrective work order directly (Req 36.2).
        group.MapPost("/", async (
                CreateWorkOrderBody body,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var dto = await maintenance.CreateWorkOrderAsync(new CreateWorkOrderRequest(
                    body.AssetId,
                    body.Title!,
                    body.Description!,
                    body.Priority,
                    body.SourceTicketId), ct);
                return Results.Ok(ApiResult<WorkOrderDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "WorkOrder"));

        // GET — paginated/filter/sort/search list, tenant-scoped (Req 36.2, 36.3).
        group.MapGet("/", async (
                string? search,
                string? sort,
                string? status,
                string? workOrderType,
                string? priority,
                Guid? assetId,
                Guid? assignedToUserId,
                Guid? maintenancePlanId,
                int? page,
                int? pageSize,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                AddEquals(filters, "status", status);
                AddEquals(filters, "workOrderType", workOrderType);
                AddEquals(filters, "priority", priority);
                AddEquals(filters, "assetId", assetId);
                AddEquals(filters, "assignedToUserId", assignedToUserId);
                AddEquals(filters, "maintenancePlanId", maintenancePlanId);

                var data = await maintenance.ListWorkOrdersAsync(BuildParameters(page, pageSize, search, sort, filters), ct);
                return Results.Ok(ApiResult<PagedData<WorkOrderDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "WorkOrder"));

        // GET /{id} — fetch one, tenant-scoped. Unknown/foreign id → NOT_FOUND.
        group.MapGet("/{id:guid}", async (
                Guid id,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var dto = await maintenance.GetWorkOrderAsync(id, ct)
                    ?? throw new NotFoundException("Work order not found.");
                return Results.Ok(ApiResult<WorkOrderDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "WorkOrder"));

        // POST /{id}/assign — assign/re-assign the work order to maintenance staff (Req 36.3).
        group.MapPost("/{id:guid}/assign", async (
                Guid id,
                AssignWorkOrderBody body,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var dto = await maintenance.AssignWorkOrderAsync(id, new AssignWorkOrderRequest(body.AssignedToUserId), ct);
                return Results.Ok(ApiResult<WorkOrderDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Assign, "WorkOrder"));

        // POST /{id}/status — change the work order's status through the configurable allow-list (Req 36.1-36.5).
        group.MapPost("/{id:guid}/status", async (
                Guid id,
                ChangeWorkOrderStatusBody body,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var dto = await maintenance.ChangeWorkOrderStatusAsync(id, new ChangeWorkOrderStatusRequest(body.ToStatus!, body.CompletionNotes), ct);
                return Results.Ok(ApiResult<WorkOrderDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "WorkOrder"));

        // POST /{id}/parts — record a part/material used against the work order (Req 36.4).
        group.MapPost("/{id:guid}/parts", async (
                Guid id,
                AddWorkOrderPartBody body,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var dto = await maintenance.AddWorkOrderPartAsync(id, new AddWorkOrderPartRequest(
                    body.PartName!,
                    body.PartNumber,
                    body.Quantity,
                    body.UnitCost), ct);
                return Results.Ok(ApiResult<WorkOrderPartDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "WorkOrder"));

        // GET /{id}/parts — the work order's recorded parts, chronological (Req 36.4).
        group.MapGet("/{id:guid}/parts", async (
                Guid id,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var items = await maintenance.GetWorkOrderPartsAsync(id, ct);
                return Results.Ok(ApiResult<IReadOnlyList<WorkOrderPartDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "WorkOrder"));

        // POST /{id}/labour — record a labour entry against the work order (Req 36.4).
        group.MapPost("/{id:guid}/labour", async (
                Guid id,
                AddWorkOrderLabourBody body,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var dto = await maintenance.AddWorkOrderLabourAsync(id, new AddWorkOrderLabourRequest(
                    body.WorkerUserId,
                    body.WorkerName!,
                    body.Hours,
                    body.HourlyRate,
                    body.Notes), ct);
                return Results.Ok(ApiResult<WorkOrderLabourDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "WorkOrder"));

        // GET /{id}/labour — the work order's recorded labour, chronological (Req 36.4).
        group.MapGet("/{id:guid}/labour", async (
                Guid id,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var items = await maintenance.GetWorkOrderLabourAsync(id, ct);
                return Results.Ok(ApiResult<IReadOnlyList<WorkOrderLabourDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "WorkOrder"));
    }

    // --- Inspections (/api/v1/inspections) — Req 37.1 --------------------------------------------
    private static void MapInspectionEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/inspections");

        // POST — complete an inspection checklist against an asset (Req 37.1).
        group.MapPost("/", async (
                RecordInspectionBody body,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var items = (body.Items ?? [])
                    .Select(i => new InspectionChecklistItemInput(i.ItemText!, i.Result!, i.Remark))
                    .ToList();
                var dto = await maintenance.RecordInspectionAsync(new RecordInspectionRequest(
                    body.AssetId,
                    body.WorkOrderId,
                    body.Name!,
                    body.OverallResult!,
                    body.InspectedAtUtc,
                    body.Notes,
                    items), ct);
                return Results.Ok(ApiResult<InspectionChecklistDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "InspectionChecklist"));

        // GET /{id} — fetch one inspection with its items, tenant-scoped. Unknown/foreign id → NOT_FOUND.
        group.MapGet("/{id:guid}", async (
                Guid id,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var dto = await maintenance.GetInspectionAsync(id, ct)
                    ?? throw new NotFoundException("Inspection not found.");
                return Results.Ok(ApiResult<InspectionChecklistDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "InspectionChecklist"));
    }

    // --- AMC contracts (/api/v1/amc-contracts) — Req 37.2 ----------------------------------------
    private static void MapAmcContractEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/amc-contracts");

        // POST — record an AMC contract covering an asset with a vendor (Req 37.2).
        group.MapPost("/", async (
                RecordAmcContractBody body,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var dto = await maintenance.RecordAmcContractAsync(new RecordAmcContractRequest(
                    body.AssetId,
                    body.ContractReference!,
                    body.VendorName!,
                    body.VendorContact,
                    body.StartDateUtc,
                    body.EndDateUtc,
                    body.ResponsibleUserId,
                    body.ContractValue,
                    body.Notes), ct);
                return Results.Ok(ApiResult<AmcContractDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "AmcContract"));

        // GET /{id} — fetch one, tenant-scoped. Unknown/foreign id → NOT_FOUND.
        group.MapGet("/{id:guid}", async (
                Guid id,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var dto = await maintenance.GetAmcContractAsync(id, ct)
                    ?? throw new NotFoundException("AMC contract not found.");
                return Results.Ok(ApiResult<AmcContractDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "AmcContract"));

        // PUT /{id} — update the contract's editable details and/or status (Req 37.2).
        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdateAmcContractBody body,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var dto = await maintenance.UpdateAmcContractAsync(id, new UpdateAmcContractRequest(
                    body.ContractReference,
                    body.VendorName,
                    body.VendorContact,
                    body.EndDateUtc,
                    body.ResponsibleUserId,
                    body.Status,
                    body.ContractValue,
                    body.Notes), ct);
                return Results.Ok(ApiResult<AmcContractDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "AmcContract"));
    }

    // --- Meter readings (/api/v1/meter-readings) — Req 37.4, 37.5 --------------------------------
    private static void MapMeterReadingEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/meter-readings");

        // POST — record a meter reading for an asset's equipment counter (Req 37.4). Non-decreasing.
        group.MapPost("/", async (
                RecordMeterReadingBody body,
                IMaintenanceService maintenance,
                CancellationToken ct) =>
            {
                var dto = await maintenance.RecordMeterReadingAsync(new RecordMeterReadingRequest(
                    body.AssetId,
                    body.MeterType!,
                    body.Unit!,
                    body.Value,
                    body.ReadingAtUtc,
                    body.Notes), ct);
                return Results.Ok(ApiResult<MeterReadingDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "MeterReading"));
    }

    // --- Shared helpers --------------------------------------------------------------------------

    /// <summary>Add an equals filter term only when the value is present (keeps the allow-list honest).</summary>
    private static void AddEquals(List<FilterTerm> filters, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            filters.Add(new FilterTerm(field, FilterOperator.Equals, value));
        }
    }

    private static void AddEquals(List<FilterTerm> filters, string field, Guid? value)
    {
        if (value is { } v)
        {
            filters.Add(new FilterTerm(field, FilterOperator.Equals, v.ToString()));
        }
    }

    private static ListQueryParameters BuildParameters(
        int? page, int? pageSize, string? search, string? sort, List<FilterTerm> filters) =>
        new()
        {
            Page = page,
            PageSize = pageSize,
            Search = string.IsNullOrWhiteSpace(search) ? null : search,
            Filters = filters,
            Sort = EndpointSort.Parse(sort),
        };

    // --- Request bodies (validated via DataAnnotationsValidationFilter) --------------------------

    public sealed class RegisterAssetBody
    {
        [Required(ErrorMessage = "communityId is required")]
        public Guid CommunityId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "name is required")]
        [MaxLength(256)]
        public string? Name { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "serialNumber is required")]
        [MaxLength(256)]
        public string? SerialNumber { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "location is required")]
        [MaxLength(256)]
        public string? Location { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "assetType is required")]
        [MaxLength(100)]
        public string? AssetType { get; init; }

        [MaxLength(100)]
        public string? Category { get; init; }

        public Guid? UnitId { get; init; }

        [MaxLength(256)]
        public string? WarrantyProvider { get; init; }

        [MaxLength(256)]
        public string? WarrantyReference { get; init; }

        public DateTime? WarrantyStartUtc { get; init; }

        public DateTime? WarrantyEndUtc { get; init; }

        public DateTime? InstalledOnUtc { get; init; }

        [MaxLength(2000)]
        public string? Notes { get; init; }
    }

    public sealed class UpdateAssetBody
    {
        [MaxLength(256)]
        public string? Name { get; init; }

        [MaxLength(256)]
        public string? Location { get; init; }

        [MaxLength(100)]
        public string? AssetType { get; init; }

        [MaxLength(100)]
        public string? Category { get; init; }

        [MaxLength(100)]
        public string? Status { get; init; }

        public Guid? UnitId { get; init; }

        public bool ClearUnit { get; init; }

        [MaxLength(256)]
        public string? WarrantyProvider { get; init; }

        [MaxLength(256)]
        public string? WarrantyReference { get; init; }

        public DateTime? WarrantyStartUtc { get; init; }

        public DateTime? WarrantyEndUtc { get; init; }

        public DateTime? InstalledOnUtc { get; init; }

        [MaxLength(2000)]
        public string? Notes { get; init; }
    }

    public sealed class CreateMaintenancePlanBody
    {
        [Required(ErrorMessage = "assetId is required")]
        public Guid AssetId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "name is required")]
        [MaxLength(256)]
        public string? Name { get; init; }

        [MaxLength(2000)]
        public string? Description { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "frequency is required")]
        [MaxLength(100)]
        public string? Frequency { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "priority is required")]
        [MaxLength(100)]
        public string? Priority { get; init; }

        public DateTime? FirstDueAtUtc { get; init; }

        public int? CustomIntervalDays { get; init; }
    }

    public sealed class UpdateMaintenancePlanBody
    {
        [MaxLength(256)]
        public string? Name { get; init; }

        [MaxLength(2000)]
        public string? Description { get; init; }

        [MaxLength(100)]
        public string? Frequency { get; init; }

        [MaxLength(100)]
        public string? Priority { get; init; }

        public int? CustomIntervalDays { get; init; }

        public DateTime? NextDueAtUtc { get; init; }

        public bool? IsActive { get; init; }
    }

    public sealed class CreateWorkOrderBody
    {
        [Required(ErrorMessage = "assetId is required")]
        public Guid AssetId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "title is required")]
        [MaxLength(256)]
        public string? Title { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "description is required")]
        [MaxLength(4000)]
        public string? Description { get; init; }

        [MaxLength(100)]
        public string? Priority { get; init; }

        public Guid? SourceTicketId { get; init; }
    }

    public sealed class AssignWorkOrderBody
    {
        [Required(ErrorMessage = "assignedToUserId is required")]
        public Guid AssignedToUserId { get; init; }
    }

    public sealed class ChangeWorkOrderStatusBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "toStatus is required")]
        [MaxLength(100)]
        public string? ToStatus { get; init; }

        [MaxLength(2000)]
        public string? CompletionNotes { get; init; }
    }

    public sealed class AddWorkOrderPartBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "partName is required")]
        [MaxLength(256)]
        public string? PartName { get; init; }

        [MaxLength(128)]
        public string? PartNumber { get; init; }

        [Range(0, double.MaxValue, ErrorMessage = "quantity must be non-negative")]
        public decimal Quantity { get; init; }

        [Range(0, double.MaxValue, ErrorMessage = "unitCost must be non-negative")]
        public decimal UnitCost { get; init; }
    }

    public sealed class AddWorkOrderLabourBody
    {
        public Guid? WorkerUserId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "workerName is required")]
        [MaxLength(256)]
        public string? WorkerName { get; init; }

        [Range(0, double.MaxValue, ErrorMessage = "hours must be non-negative")]
        public decimal Hours { get; init; }

        [Range(0, double.MaxValue, ErrorMessage = "hourlyRate must be non-negative")]
        public decimal HourlyRate { get; init; }

        [MaxLength(1000)]
        public string? Notes { get; init; }
    }

    public sealed class RecordInspectionBody
    {
        [Required(ErrorMessage = "assetId is required")]
        public Guid AssetId { get; init; }

        public Guid? WorkOrderId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "name is required")]
        [MaxLength(256)]
        public string? Name { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "overallResult is required")]
        [MaxLength(100)]
        public string? OverallResult { get; init; }

        public DateTime? InspectedAtUtc { get; init; }

        [MaxLength(2000)]
        public string? Notes { get; init; }

        [Required(ErrorMessage = "items is required")]
        [MinLength(1, ErrorMessage = "at least one checklist item is required")]
        public IReadOnlyList<InspectionItemBody>? Items { get; init; }
    }

    public sealed class InspectionItemBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "itemText is required")]
        [MaxLength(512)]
        public string? ItemText { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "result is required")]
        [MaxLength(100)]
        public string? Result { get; init; }

        [MaxLength(1000)]
        public string? Remark { get; init; }
    }

    public sealed class RecordAmcContractBody
    {
        [Required(ErrorMessage = "assetId is required")]
        public Guid AssetId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "contractReference is required")]
        [MaxLength(256)]
        public string? ContractReference { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "vendorName is required")]
        [MaxLength(256)]
        public string? VendorName { get; init; }

        [MaxLength(256)]
        public string? VendorContact { get; init; }

        [Required(ErrorMessage = "startDateUtc is required")]
        public DateTime StartDateUtc { get; init; }

        [Required(ErrorMessage = "endDateUtc is required")]
        public DateTime EndDateUtc { get; init; }

        [Required(ErrorMessage = "responsibleUserId is required")]
        public Guid ResponsibleUserId { get; init; }

        public decimal? ContractValue { get; init; }

        [MaxLength(2000)]
        public string? Notes { get; init; }
    }

    public sealed class UpdateAmcContractBody
    {
        [MaxLength(256)]
        public string? ContractReference { get; init; }

        [MaxLength(256)]
        public string? VendorName { get; init; }

        [MaxLength(256)]
        public string? VendorContact { get; init; }

        public DateTime? EndDateUtc { get; init; }

        public Guid? ResponsibleUserId { get; init; }

        [MaxLength(100)]
        public string? Status { get; init; }

        public decimal? ContractValue { get; init; }

        [MaxLength(2000)]
        public string? Notes { get; init; }
    }

    public sealed class RecordMeterReadingBody
    {
        [Required(ErrorMessage = "assetId is required")]
        public Guid AssetId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "meterType is required")]
        [MaxLength(100)]
        public string? MeterType { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "unit is required")]
        [MaxLength(100)]
        public string? Unit { get; init; }

        [Range(0, double.MaxValue, ErrorMessage = "value must be non-negative")]
        public decimal Value { get; init; }

        public DateTime? ReadingAtUtc { get; init; }

        [MaxLength(1000)]
        public string? Notes { get; init; }
    }
}
