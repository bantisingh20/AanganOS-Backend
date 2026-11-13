using System.ComponentModel.DataAnnotations;
using App.Api.Endpoints;
using App.Api.Security;
using App.Application.Exceptions;
using App.Application.Maintenance;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
public sealed class MaintenanceController(IMaintenanceService maintenance) : ControllerBase
{
    [HttpPost("api/v1/assets")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "Asset")]
    public async Task<IActionResult> RegisterAsset([FromBody] RegisterAssetBody body, CancellationToken ct)
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
        return Ok(ApiResult<AssetDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/assets")]
    [RequirePermission(PermissionAction.View, "Asset")]
    public async Task<IActionResult> ListAssets(
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] string? status,
        [FromQuery] string? assetType,
        [FromQuery] string? category,
        [FromQuery] Guid? communityId,
        [FromQuery] Guid? unitId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        AddEquals(filters, "status", status);
        AddEquals(filters, "assetType", assetType);
        AddEquals(filters, "category", category);
        AddEquals(filters, "communityId", communityId);
        AddEquals(filters, "unitId", unitId);

        var data = await maintenance.ListAssetsAsync(BuildParameters(page, pageSize, search, sort, filters), ct);
        return Ok(ApiResult<PagedData<AssetDto>>.Ok(data, string.Empty));
    }

    [HttpGet("api/v1/assets/{id:guid}")]
    [RequirePermission(PermissionAction.View, "Asset")]
    public async Task<IActionResult> GetAsset(Guid id, CancellationToken ct)
    {
        var dto = await maintenance.GetAssetAsync(id, ct)
            ?? throw new NotFoundException("Asset not found.");
        return Ok(ApiResult<AssetDto>.Ok(dto, string.Empty));
    }

    [HttpPut("api/v1/assets/{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Asset")]
    public async Task<IActionResult> UpdateAsset(Guid id, [FromBody] UpdateAssetBody body, CancellationToken ct)
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
        return Ok(ApiResult<AssetDto>.Ok(dto, string.Empty));
    }

    [HttpDelete("api/v1/assets/{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Delete, "Asset")]
    public async Task<IActionResult> DeleteAsset(Guid id, CancellationToken ct)
    {
        await maintenance.DeleteAssetAsync(id, ct);
        return Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
    }

    [HttpGet("api/v1/assets/{id:guid}/equipment-history")]
    [RequirePermission(PermissionAction.View, "WorkOrder")]
    public async Task<IActionResult> GetEquipmentHistory(Guid id, CancellationToken ct)
    {
        var items = await maintenance.GetEquipmentHistoryAsync(id, ct);
        return Ok(ApiResult<IReadOnlyList<EquipmentHistoryEntryDto>>.Ok(items, string.Empty));
    }

    [HttpGet("api/v1/assets/{id:guid}/inspections")]
    [RequirePermission(PermissionAction.View, "InspectionChecklist")]
    public async Task<IActionResult> GetAssetInspections(Guid id, CancellationToken ct)
    {
        var items = await maintenance.GetAssetInspectionsAsync(id, ct);
        return Ok(ApiResult<IReadOnlyList<InspectionChecklistDto>>.Ok(items, string.Empty));
    }

    [HttpGet("api/v1/assets/{id:guid}/amc-contracts")]
    [RequirePermission(PermissionAction.View, "AmcContract")]
    public async Task<IActionResult> GetAssetAmcContracts(Guid id, CancellationToken ct)
    {
        var items = await maintenance.GetAssetAmcContractsAsync(id, ct);
        return Ok(ApiResult<IReadOnlyList<AmcContractDto>>.Ok(items, string.Empty));
    }

    [HttpGet("api/v1/assets/{id:guid}/meter-readings")]
    [RequirePermission(PermissionAction.View, "MeterReading")]
    public async Task<IActionResult> GetAssetMeterReadings(Guid id, [FromQuery] string? meterType, CancellationToken ct)
    {
        var items = await maintenance.GetAssetMeterReadingsAsync(id, meterType, ct);
        return Ok(ApiResult<IReadOnlyList<MeterReadingDto>>.Ok(items, string.Empty));
    }

    [HttpPost("api/v1/maintenance-plans")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "MaintenancePlan")]
    public async Task<IActionResult> CreateMaintenancePlan([FromBody] CreateMaintenancePlanBody body, CancellationToken ct)
    {
        var dto = await maintenance.CreateMaintenancePlanAsync(new CreateMaintenancePlanRequest(
            body.AssetId,
            body.Name!,
            body.Description,
            body.Frequency!,
            body.Priority!,
            body.FirstDueAtUtc,
            body.CustomIntervalDays), ct);
        return Ok(ApiResult<MaintenancePlanDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/maintenance-plans")]
    [RequirePermission(PermissionAction.View, "MaintenancePlan")]
    public async Task<IActionResult> ListMaintenancePlans(
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] string? frequency,
        [FromQuery] string? priority,
        [FromQuery] Guid? assetId,
        [FromQuery] bool? isActive,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
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
        return Ok(ApiResult<PagedData<MaintenancePlanDto>>.Ok(data, string.Empty));
    }

    [HttpGet("api/v1/maintenance-plans/{id:guid}")]
    [RequirePermission(PermissionAction.View, "MaintenancePlan")]
    public async Task<IActionResult> GetMaintenancePlan(Guid id, CancellationToken ct)
    {
        var dto = await maintenance.GetMaintenancePlanAsync(id, ct)
            ?? throw new NotFoundException("Maintenance plan not found.");
        return Ok(ApiResult<MaintenancePlanDto>.Ok(dto, string.Empty));
    }

    [HttpPut("api/v1/maintenance-plans/{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "MaintenancePlan")]
    public async Task<IActionResult> UpdateMaintenancePlan(Guid id, [FromBody] UpdateMaintenancePlanBody body, CancellationToken ct)
    {
        var dto = await maintenance.UpdateMaintenancePlanAsync(id, new UpdateMaintenancePlanRequest(
            body.Name,
            body.Description,
            body.Frequency,
            body.Priority,
            body.CustomIntervalDays,
            body.NextDueAtUtc,
            body.IsActive), ct);
        return Ok(ApiResult<MaintenancePlanDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/work-orders")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "WorkOrder")]
    public async Task<IActionResult> CreateWorkOrder([FromBody] CreateWorkOrderBody body, CancellationToken ct)
    {
        var dto = await maintenance.CreateWorkOrderAsync(new CreateWorkOrderRequest(
            body.AssetId,
            body.Title!,
            body.Description!,
            body.Priority,
            body.SourceTicketId), ct);
        return Ok(ApiResult<WorkOrderDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/work-orders")]
    [RequirePermission(PermissionAction.View, "WorkOrder")]
    public async Task<IActionResult> ListWorkOrders(
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] string? status,
        [FromQuery] string? workOrderType,
        [FromQuery] string? priority,
        [FromQuery] Guid? assetId,
        [FromQuery] Guid? assignedToUserId,
        [FromQuery] Guid? maintenancePlanId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        AddEquals(filters, "status", status);
        AddEquals(filters, "workOrderType", workOrderType);
        AddEquals(filters, "priority", priority);
        AddEquals(filters, "assetId", assetId);
        AddEquals(filters, "assignedToUserId", assignedToUserId);
        AddEquals(filters, "maintenancePlanId", maintenancePlanId);

        var data = await maintenance.ListWorkOrdersAsync(BuildParameters(page, pageSize, search, sort, filters), ct);
        return Ok(ApiResult<PagedData<WorkOrderDto>>.Ok(data, string.Empty));
    }

    [HttpGet("api/v1/work-orders/{id:guid}")]
    [RequirePermission(PermissionAction.View, "WorkOrder")]
    public async Task<IActionResult> GetWorkOrder(Guid id, CancellationToken ct)
    {
        var dto = await maintenance.GetWorkOrderAsync(id, ct)
            ?? throw new NotFoundException("Work order not found.");
        return Ok(ApiResult<WorkOrderDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/work-orders/{id:guid}/assign")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Assign, "WorkOrder")]
    public async Task<IActionResult> AssignWorkOrder(Guid id, [FromBody] AssignWorkOrderBody body, CancellationToken ct)
    {
        var dto = await maintenance.AssignWorkOrderAsync(id, new AssignWorkOrderRequest(body.AssignedToUserId), ct);
        return Ok(ApiResult<WorkOrderDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/work-orders/{id:guid}/status")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "WorkOrder")]
    public async Task<IActionResult> ChangeWorkOrderStatus(Guid id, [FromBody] ChangeWorkOrderStatusBody body, CancellationToken ct)
    {
        var dto = await maintenance.ChangeWorkOrderStatusAsync(id, new ChangeWorkOrderStatusRequest(body.ToStatus!, body.CompletionNotes), ct);
        return Ok(ApiResult<WorkOrderDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/work-orders/{id:guid}/parts")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "WorkOrder")]
    public async Task<IActionResult> AddWorkOrderPart(Guid id, [FromBody] AddWorkOrderPartBody body, CancellationToken ct)
    {
        var dto = await maintenance.AddWorkOrderPartAsync(id, new AddWorkOrderPartRequest(
            body.PartName!,
            body.PartNumber,
            body.Quantity,
            body.UnitCost), ct);
        return Ok(ApiResult<WorkOrderPartDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/work-orders/{id:guid}/parts")]
    [RequirePermission(PermissionAction.View, "WorkOrder")]
    public async Task<IActionResult> GetWorkOrderParts(Guid id, CancellationToken ct)
    {
        var items = await maintenance.GetWorkOrderPartsAsync(id, ct);
        return Ok(ApiResult<IReadOnlyList<WorkOrderPartDto>>.Ok(items, string.Empty));
    }

    [HttpPost("api/v1/work-orders/{id:guid}/labour")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "WorkOrder")]
    public async Task<IActionResult> AddWorkOrderLabour(Guid id, [FromBody] AddWorkOrderLabourBody body, CancellationToken ct)
    {
        var dto = await maintenance.AddWorkOrderLabourAsync(id, new AddWorkOrderLabourRequest(
            body.WorkerUserId,
            body.WorkerName!,
            body.Hours,
            body.HourlyRate,
            body.Notes), ct);
        return Ok(ApiResult<WorkOrderLabourDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/work-orders/{id:guid}/labour")]
    [RequirePermission(PermissionAction.View, "WorkOrder")]
    public async Task<IActionResult> GetWorkOrderLabour(Guid id, CancellationToken ct)
    {
        var items = await maintenance.GetWorkOrderLabourAsync(id, ct);
        return Ok(ApiResult<IReadOnlyList<WorkOrderLabourDto>>.Ok(items, string.Empty));
    }

    [HttpPost("api/v1/inspections")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "InspectionChecklist")]
    public async Task<IActionResult> RecordInspection([FromBody] RecordInspectionBody body, CancellationToken ct)
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
        return Ok(ApiResult<InspectionChecklistDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/inspections/{id:guid}")]
    [RequirePermission(PermissionAction.View, "InspectionChecklist")]
    public async Task<IActionResult> GetInspection(Guid id, CancellationToken ct)
    {
        var dto = await maintenance.GetInspectionAsync(id, ct)
            ?? throw new NotFoundException("Inspection not found.");
        return Ok(ApiResult<InspectionChecklistDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/amc-contracts")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "AmcContract")]
    public async Task<IActionResult> RecordAmcContract([FromBody] RecordAmcContractBody body, CancellationToken ct)
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
        return Ok(ApiResult<AmcContractDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/amc-contracts/{id:guid}")]
    [RequirePermission(PermissionAction.View, "AmcContract")]
    public async Task<IActionResult> GetAmcContract(Guid id, CancellationToken ct)
    {
        var dto = await maintenance.GetAmcContractAsync(id, ct)
            ?? throw new NotFoundException("AMC contract not found.");
        return Ok(ApiResult<AmcContractDto>.Ok(dto, string.Empty));
    }

    [HttpPut("api/v1/amc-contracts/{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "AmcContract")]
    public async Task<IActionResult> UpdateAmcContract(Guid id, [FromBody] UpdateAmcContractBody body, CancellationToken ct)
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
        return Ok(ApiResult<AmcContractDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/meter-readings")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "MeterReading")]
    public async Task<IActionResult> RecordMeterReading([FromBody] RecordMeterReadingBody body, CancellationToken ct)
    {
        var dto = await maintenance.RecordMeterReadingAsync(new RecordMeterReadingRequest(
            body.AssetId,
            body.MeterType!,
            body.Unit!,
            body.Value,
            body.ReadingAtUtc,
            body.Notes), ct);
        return Ok(ApiResult<MeterReadingDto>.Ok(dto, string.Empty));
    }

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
