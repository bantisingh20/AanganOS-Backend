using System.ComponentModel.DataAnnotations;
using App.Api.Endpoints;
using App.Api.Security;
using App.Application.Exceptions;
using App.Application.Gate;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
public sealed class GateController(IGateService gate) : ControllerBase
{
    [HttpPost("api/v1/visit-passes")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "VisitPass")]
    public async Task<IActionResult> CreateVisitPass([FromBody] CreateVisitPassBody body, CancellationToken ct)
    {
        var result = await gate.CreateVisitPassAsync(new CreateVisitPassRequest(
            body.HostUnitId,
            body.HostResidentId,
            body.VisitorName!,
            body.ValidFromUtc,
            body.ValidUntilUtc,
            body.MaxUses,
            body.Phone,
            body.Email,
            body.VehicleNumber,
            body.VehicleType,
            body.NumberOfPersons,
            body.WhomToMeet,
            body.VisitDateUtc,
            body.PhotoFileId), ct);
        return Ok(ApiResult<CreateVisitPassResult>.Ok(result, string.Empty));
    }

    [HttpPost("api/v1/visit-passes/verify")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "Visitor")]
    public async Task<IActionResult> VerifyVisitPass([FromBody] VerifyVisitPassBody body, CancellationToken ct)
    {
        var result = await gate.VerifyVisitPassAsync(new VerifyVisitPassRequest(body.QrToken, body.Otp), ct);
        return Ok(ApiResult<VerifyVisitPassResult>.Ok(result, string.Empty));
    }

    [HttpPost("api/v1/visit-passes/scan")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "EntryExitEvent")]
    public async Task<IActionResult> ScanAccess([FromBody] ScanAccessBody body, CancellationToken ct)
    {
        var result = await gate.ScanAccessAsync(
            new ScanAccessRequest(body.QrToken, body.Otp, body.GateId, body.GateLabel), ct);
        return Ok(ApiResult<ScanAccessResult>.Ok(result, string.Empty));
    }

    [HttpGet("api/v1/gate/vehicle-lookup")]
    [RequirePermission(PermissionAction.View, "Vehicle")]
    public async Task<IActionResult> VehicleLookup([FromQuery] string? plate, CancellationToken ct)
    {
        var result = await gate.LookupVehicleAsync(plate ?? string.Empty, ct);
        return Ok(ApiResult<VehicleLookupResult>.Ok(result, string.Empty));
    }

    [HttpGet("api/v1/visit-passes")]
    [RequirePermission(PermissionAction.View, "VisitPass")]
    public async Task<IActionResult> ListVisitPasses(
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] string? status,
        [FromQuery] Guid? hostUnitId,
        [FromQuery] Guid? organizationId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        AddEquals(filters, "status", status);
        AddEquals(filters, "hostUnitId", hostUnitId);

        var data = await gate.ListVisitPassesAsync(BuildParameters(page, pageSize, search, sort, filters), organizationId, ct);
        return Ok(ApiResult<PagedData<VisitPassDto>>.Ok(data, string.Empty));
    }

    [HttpGet("api/v1/visit-passes/{id:guid}")]
    [RequirePermission(PermissionAction.View, "VisitPass")]
    public async Task<IActionResult> GetVisitPass(Guid id, CancellationToken ct)
    {
        var dto = await gate.GetVisitPassAsync(id, ct)
            ?? throw new NotFoundException("Visit pass not found.");
        return Ok(ApiResult<VisitPassDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/visit-passes/{id:guid}/share")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "VisitPass")]
    public async Task<IActionResult> SharePass(Guid id, [FromBody] SharePassBody body, CancellationToken ct)
    {
        var result = await gate.SharePassAsync(id, new SharePassRequest(
            body.Sms, body.WhatsApp, body.Email, body.ToPhone, body.ToEmail), ct);
        return Ok(ApiResult<SharePassResult>.Ok(result, string.Empty));
    }

    [HttpGet("api/v1/public/visit-passes/{token}")]
    [EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    public async Task<IActionResult> GetPublicPass(string token, CancellationToken ct)
    {
        var dto = await gate.GetPublicPassAsync(token, ct)
            ?? throw new NotFoundException("This pass link is invalid or has expired.");
        return Ok(ApiResult<PublicVisitPassDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/visitors")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "Visitor")]
    public async Task<IActionResult> RegisterWalkIn([FromBody] RegisterWalkInBody body, CancellationToken ct)
    {
        var result = await gate.RegisterWalkInAsync(new RegisterWalkInRequest(
            body.HostUnitId,
            body.Name!,
            body.Phone,
            body.Category!,
            body.PhotoFileId,
            body.VehicleNumber,
            body.GateLocation,
            Email: body.Email,
            body.IdProofType,
            body.IdProofNumber,
            body.Purpose,
            body.WhomToMeet,
            body.ExpectedDurationMinutes), ct);
        return Ok(ApiResult<RegisterWalkInResult>.Ok(result, string.Empty));
    }

    [HttpPost("api/v1/visitors/{id:guid}/approve")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Approve, "Visitor")]
    public async Task<IActionResult> ApproveWalkIn(Guid id, [FromBody] ApproveWalkInBody body, CancellationToken ct)
    {
        var dto = await gate.ApproveWalkInAsync(id, body.ApprovingResidentId, ct);
        return Ok(ApiResult<VisitorDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/visitors/{id:guid}/reject")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Approve, "Visitor")]
    public async Task<IActionResult> RejectWalkIn(Guid id, [FromBody] RejectWalkInBody body, CancellationToken ct)
    {
        var dto = await gate.RejectWalkInAsync(id, body.RejectingResidentId, new RejectWalkInRequest(body.Reason), ct);
        return Ok(ApiResult<VisitorDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/visitors")]
    [RequirePermission(PermissionAction.View, "Visitor")]
    public async Task<IActionResult> ListVisitors(
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] string? status,
        [FromQuery] string? category,
        [FromQuery] Guid? hostUnitId,
        [FromQuery] Guid? organizationId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        AddEquals(filters, "status", status);
        AddEquals(filters, "category", category);
        AddEquals(filters, "hostUnitId", hostUnitId);
        if (fromDate is { } f)
        {
            filters.Add(new FilterTerm("createdAtUtc", FilterOperator.GreaterThanOrEqual, f.ToUniversalTime().ToString("o")));
        }
        if (toDate is { } t)
        {
            filters.Add(new FilterTerm("createdAtUtc", FilterOperator.LessThanOrEqual, t.ToUniversalTime().ToString("o")));
        }

        var data = await gate.ListVisitorsAsync(BuildParameters(page, pageSize, search, sort, filters), organizationId, ct);
        return Ok(ApiResult<PagedData<VisitorDto>>.Ok(data, string.Empty));
    }

    [HttpGet("api/v1/visitors/{id:guid}")]
    [RequirePermission(PermissionAction.View, "Visitor")]
    public async Task<IActionResult> GetVisitor(Guid id, CancellationToken ct)
    {
        var dto = await gate.GetVisitorAsync(id, ct)
            ?? throw new NotFoundException("Visitor not found.");
        return Ok(ApiResult<VisitorDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/entry-events/entry")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "EntryExitEvent")]
    public async Task<IActionResult> RecordEntry([FromBody] RecordEntryBody body, CancellationToken ct)
    {
        var dto = await gate.RecordEntryAsync(new RecordEntryRequest(
            body.VisitorId,
            body.VisitPassId,
            body.RecurringVisitorId,
            body.GateId,
            body.GateLabel,
            body.VehicleNumber), ct);
        return Ok(ApiResult<EntryExitEventDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/entry-events/exit")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "EntryExitEvent")]
    public async Task<IActionResult> RecordExit([FromBody] RecordExitBody body, CancellationToken ct)
    {
        var dto = await gate.RecordExitAsync(new RecordExitRequest(
            body.VisitorId,
            body.VisitPassId,
            body.RecurringVisitorId,
            body.GateId,
            body.GateLabel), ct);
        return Ok(ApiResult<EntryExitEventDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/entry-events")]
    [RequirePermission(PermissionAction.View, "EntryExitEvent")]
    public async Task<IActionResult> ListEntryExitEvents(
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] string? category,
        [FromQuery] bool? open,
        [FromQuery] Guid? destinationUnitId,
        [FromQuery] Guid? visitorId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        AddEquals(filters, "category", category);
        AddEquals(filters, "destinationUnitId", destinationUnitId);
        AddEquals(filters, "visitorId", visitorId);
        if (open is { } o)
        {
            filters.Add(new FilterTerm("open", FilterOperator.Equals, o ? "true" : "false"));
        }

        var data = await gate.ListEntryExitEventsAsync(BuildParameters(page, pageSize, search, sort, filters), ct);
        return Ok(ApiResult<PagedData<EntryExitEventDto>>.Ok(data, string.Empty));
    }

    [HttpPost("api/v1/recurring-visitors")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "RecurringVisitor")]
    public async Task<IActionResult> CreateRecurringVisitor([FromBody] CreateRecurringVisitorBody body, CancellationToken ct)
    {
        var dto = await gate.CreateRecurringVisitorAsync(new CreateRecurringVisitorRequest(
            body.HostUnitId,
            body.HostResidentId,
            body.Name!,
            body.Phone,
            body.Category!,
            body.DaysOfWeek,
            body.StartDate,
            body.EndDate,
            body.DailyStartMinutes,
            body.DailyEndMinutes,
            body.PhotoFileId), ct);
        return Ok(ApiResult<RecurringVisitorDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/recurring-visitors/{id:guid}/revoke")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "RecurringVisitor")]
    public async Task<IActionResult> RevokeRecurringVisitor(Guid id, CancellationToken ct)
    {
        var dto = await gate.RevokeRecurringVisitorAsync(id, ct);
        return Ok(ApiResult<RecurringVisitorDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/recurring-visitors/{id:guid}/extend")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "RecurringVisitor")]
    public async Task<IActionResult> ExtendRecurringVisitor(Guid id, [FromBody] ExtendRecurringVisitorBody body, CancellationToken ct)
    {
        var dto = await gate.ExtendRecurringVisitorAsync(id, body.EndDate, ct);
        return Ok(ApiResult<RecurringVisitorDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/recurring-visitors")]
    [RequirePermission(PermissionAction.View, "RecurringVisitor")]
    public async Task<IActionResult> ListRecurringVisitors(
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] string? status,
        [FromQuery] string? category,
        [FromQuery] Guid? hostUnitId,
        [FromQuery] bool? active,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        AddEquals(filters, "status", status);
        AddEquals(filters, "category", category);
        AddEquals(filters, "hostUnitId", hostUnitId);

        var data = await gate.ListRecurringVisitorsAsync(BuildParameters(page, pageSize, search, sort, filters), active, ct);
        return Ok(ApiResult<PagedData<RecurringVisitorDto>>.Ok(data, string.Empty));
    }

    [HttpGet("api/v1/recurring-visitors/{id:guid}")]
    [RequirePermission(PermissionAction.View, "RecurringVisitor")]
    public async Task<IActionResult> GetRecurringVisitor(Guid id, CancellationToken ct)
    {
        var dto = await gate.GetRecurringVisitorAsync(id, ct)
            ?? throw new NotFoundException("Recurring visitor not found.");
        return Ok(ApiResult<RecurringVisitorDto>.Ok(dto, string.Empty));
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

    public sealed class CreateVisitPassBody
    {
        [Required(ErrorMessage = "hostUnitId is required")]
        public Guid HostUnitId { get; init; }

        public Guid? HostResidentId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "visitorName is required")]
        [MaxLength(256)]
        public string? VisitorName { get; init; }

        public DateTime? ValidFromUtc { get; init; }

        public DateTime? ValidUntilUtc { get; init; }

        public int? MaxUses { get; init; }

        [MaxLength(50)]
        public string? Phone { get; init; }

        [EmailAddress(ErrorMessage = "email must be a valid email address")]
        [MaxLength(256)]
        public string? Email { get; init; }

        [MaxLength(50)]
        public string? VehicleNumber { get; init; }

        [MaxLength(100)]
        public string? VehicleType { get; init; }

        [Range(1, 100, ErrorMessage = "numberOfPersons must be between 1 and 100")]
        public int? NumberOfPersons { get; init; }

        [MaxLength(200)]
        public string? WhomToMeet { get; init; }

        public DateTime? VisitDateUtc { get; init; }

        public Guid? PhotoFileId { get; init; }
    }

    public sealed class VerifyVisitPassBody
    {
        [MaxLength(512)]
        public string? QrToken { get; init; }

        [MaxLength(32)]
        public string? Otp { get; init; }
    }

    public sealed class SharePassBody
    {
        public bool Sms { get; init; }
        public bool WhatsApp { get; init; }
        public bool Email { get; init; }

        [MaxLength(50)]
        public string? ToPhone { get; init; }

        [MaxLength(256)]
        public string? ToEmail { get; init; }
    }

    public sealed class ScanAccessBody
    {
        [MaxLength(512)]
        public string? QrToken { get; init; }

        [MaxLength(32)]
        public string? Otp { get; init; }

        public Guid? GateId { get; init; }

        [MaxLength(256)]
        public string? GateLabel { get; init; }
    }

    public sealed class RegisterWalkInBody
    {
        [Required(ErrorMessage = "hostUnitId is required")]
        public Guid HostUnitId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "name is required")]
        [MaxLength(256)]
        public string? Name { get; init; }

        [MaxLength(50)]
        public string? Phone { get; init; }

        [EmailAddress(ErrorMessage = "email must be a valid email address")]
        [MaxLength(256)]
        public string? Email { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "category is required")]
        [MaxLength(100)]
        public string? Category { get; init; }

        public Guid? PhotoFileId { get; init; }

        [MaxLength(50)]
        public string? VehicleNumber { get; init; }

        [MaxLength(256)]
        public string? GateLocation { get; init; }

        [MaxLength(100)]
        public string? IdProofType { get; init; }

        [MaxLength(100)]
        public string? IdProofNumber { get; init; }

        [MaxLength(500)]
        public string? Purpose { get; init; }

        [MaxLength(200)]
        public string? WhomToMeet { get; init; }

        [Range(1, 10080, ErrorMessage = "expectedDurationMinutes must be between 1 and 10080")]
        public int? ExpectedDurationMinutes { get; init; }
    }

    public sealed class ApproveWalkInBody
    {
        [Required(ErrorMessage = "approvingResidentId is required")]
        public Guid ApprovingResidentId { get; init; }
    }

    public sealed class RejectWalkInBody
    {
        [Required(ErrorMessage = "rejectingResidentId is required")]
        public Guid RejectingResidentId { get; init; }

        [MaxLength(1000)]
        public string? Reason { get; init; }
    }

    public sealed class RecordEntryBody
    {
        public Guid? VisitorId { get; init; }

        public Guid? VisitPassId { get; init; }

        public Guid? RecurringVisitorId { get; init; }

        public Guid? GateId { get; init; }

        [MaxLength(256)]
        public string? GateLabel { get; init; }

        [MaxLength(50)]
        public string? VehicleNumber { get; init; }
    }

    public sealed class RecordExitBody
    {
        public Guid? VisitorId { get; init; }

        public Guid? VisitPassId { get; init; }

        public Guid? RecurringVisitorId { get; init; }

        public Guid? GateId { get; init; }

        [MaxLength(256)]
        public string? GateLabel { get; init; }
    }

    public sealed class CreateRecurringVisitorBody
    {
        [Required(ErrorMessage = "hostUnitId is required")]
        public Guid HostUnitId { get; init; }

        [Required(ErrorMessage = "hostResidentId is required")]
        public Guid HostResidentId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "name is required")]
        [MaxLength(256)]
        public string? Name { get; init; }

        [MaxLength(50)]
        public string? Phone { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "category is required")]
        [MaxLength(100)]
        public string? Category { get; init; }

        [Range(1, 127, ErrorMessage = "daysOfWeek must be a 7-bit day mask (1-127)")]
        public int DaysOfWeek { get; init; }

        [Required(ErrorMessage = "startDate is required")]
        public DateTime StartDate { get; init; }

        [Required(ErrorMessage = "endDate is required")]
        public DateTime EndDate { get; init; }

        public int? DailyStartMinutes { get; init; }

        public int? DailyEndMinutes { get; init; }

        public Guid? PhotoFileId { get; init; }
    }

    public sealed class ExtendRecurringVisitorBody
    {
        [Required(ErrorMessage = "endDate is required")]
        public DateTime EndDate { get; init; }
    }
}
