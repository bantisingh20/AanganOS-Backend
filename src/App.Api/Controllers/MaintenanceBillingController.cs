using System.ComponentModel.DataAnnotations;
using App.Api.Endpoints;
using App.Api.Security;
using App.Application.Config;
using App.Application.Exceptions;
using App.Application.MaintenanceBilling;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
public sealed class MaintenanceBillingController(IMaintenanceBillingService svc) : ControllerBase
{
    // --- Charges ---
    [HttpPost("api/v1/maintenance-charges")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "MaintenanceCharge")]
    public async Task<IActionResult> SetCharge([FromBody] SetChargeBody body, CancellationToken ct)
    {
        var dto = await svc.SetChargeAsync(new SetMaintenanceChargeRequest(
            body.CommunityId, body.UnitId, body.Amount, body.Label, body.IsActive), ct);
        return Ok(ApiResult<MaintenanceChargeDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/maintenance-charges/bulk")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "MaintenanceCharge")]
    public async Task<IActionResult> BulkSetCharge([FromBody] BulkSetChargeBody body, CancellationToken ct)
    {
        var result = await svc.BulkSetChargesAsync(new BulkSetMaintenanceChargeRequest(
            body.CommunityId, body.UnitIds ?? [], body.Amount, body.Label, body.IsActive, body.ApplyToAllUnits), ct);
        return Ok(ApiResult<BulkSetMaintenanceChargeResult>.Ok(result, string.Empty));
    }

    [HttpGet("api/v1/maintenance-charges")]
    [RequirePermission(PermissionAction.View, "MaintenanceCharge")]
    public async Task<IActionResult> ListCharges([FromQuery] Guid communityId, CancellationToken ct)
    {
        var items = await svc.ListChargesAsync(communityId, ct);
        return Ok(ApiResult<IReadOnlyList<MaintenanceChargeDto>>.Ok(items, string.Empty));
    }

    [HttpDelete("api/v1/maintenance-charges/{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "MaintenanceCharge")]
    public async Task<IActionResult> DeactivateCharge(Guid id, CancellationToken ct)
    {
        var dto = await svc.DeactivateChargeAsync(id, ct);
        return Ok(ApiResult<MaintenanceChargeDto>.Ok(dto, string.Empty));
    }

    // --- Bills ---
    [HttpPost("api/v1/maintenance-bills/generate")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "MaintenanceBill")]
    public async Task<IActionResult> GenerateBills([FromBody] GenerateBillsBody body, CancellationToken ct)
    {
        var result = await svc.GenerateBillsAsync(new GenerateBillsRequest(
            body.CommunityId, body.PeriodYear, body.PeriodMonth, body.DueDateUtc), ct);
        return Ok(ApiResult<GenerateBillsResult>.Ok(result, string.Empty));
    }

    [HttpGet("api/v1/maintenance-bills")]
    [RequirePermission(PermissionAction.View, "MaintenanceBill")]
    public async Task<IActionResult> ListBills(
        [FromQuery] string? sort,
        [FromQuery] string? status,
        [FromQuery] Guid? unitId,
        [FromQuery] Guid? communityId,
        [FromQuery] int? periodYear,
        [FromQuery] int? periodMonth,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        AddEquals(filters, "status", status);
        AddEquals(filters, "unitId", unitId);
        AddEquals(filters, "communityId", communityId);
        AddEquals(filters, "periodYear", periodYear);
        AddEquals(filters, "periodMonth", periodMonth);

        var data = await svc.ListBillsAsync(BuildParameters(page, pageSize, sort, filters), ct);
        return Ok(ApiResult<PagedData<MaintenanceBillDto>>.Ok(data, string.Empty));
    }

    [HttpGet("api/v1/maintenance-bills/me")]
    [RequirePermission(PermissionAction.View, "MaintenanceBill")]
    public async Task<IActionResult> MyBills(
        [FromQuery] string? sort,
        [FromQuery] string? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        AddEquals(filters, "status", status);

        var data = await svc.ListMyBillsAsync(BuildParameters(page, pageSize, sort, filters), ct);
        return Ok(ApiResult<PagedData<MaintenanceBillDto>>.Ok(data, string.Empty));
    }

    [HttpGet("api/v1/maintenance-bills/defaulters")]
    [RequirePermission(PermissionAction.View, "MaintenanceBill")]
    public async Task<IActionResult> Defaulters(CancellationToken ct)
    {
        var items = await svc.ListDefaultersAsync(ct);
        return Ok(ApiResult<IReadOnlyList<MaintenanceDefaulterDto>>.Ok(items, string.Empty));
    }

    [HttpGet("api/v1/maintenance-bills/collection-summary")]
    [RequirePermission(PermissionAction.View, "MaintenanceBill")]
    public async Task<IActionResult> CollectionSummary([FromQuery] Guid? communityId, CancellationToken ct)
    {
        var dto = await svc.GetCollectionSummaryAsync(communityId, ct);
        return Ok(ApiResult<MaintenanceCollectionSummaryDto>.Ok(dto, string.Empty));
    }

    // --- Expenses ---
    [HttpGet("api/v1/maintenance-expenses")]
    [RequirePermission(PermissionAction.View, "MaintenanceBill")]
    public async Task<IActionResult> ListExpenses([FromQuery] Guid communityId, CancellationToken ct)
    {
        var items = await svc.ListExpensesAsync(communityId, ct);
        return Ok(ApiResult<IReadOnlyList<MaintenanceExpenseDto>>.Ok(items, string.Empty));
    }

    [HttpPost("api/v1/maintenance-expenses")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "MaintenanceBill")]
    public async Task<IActionResult> CreateExpense([FromBody] SaveExpenseBody body, CancellationToken ct)
    {
        var dto = await svc.CreateExpenseAsync(new SaveMaintenanceExpenseRequest(
            body.CommunityId, body.Category!, body.Title!, body.Amount, body.SpentOnUtc, body.Vendor, body.Notes), ct);
        return Ok(ApiResult<MaintenanceExpenseDto>.Ok(dto, string.Empty));
    }

    [HttpPut("api/v1/maintenance-expenses/{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "MaintenanceBill")]
    public async Task<IActionResult> UpdateExpense(Guid id, [FromBody] SaveExpenseBody body, CancellationToken ct)
    {
        var dto = await svc.UpdateExpenseAsync(id, new SaveMaintenanceExpenseRequest(
            body.CommunityId, body.Category!, body.Title!, body.Amount, body.SpentOnUtc, body.Vendor, body.Notes), ct);
        return Ok(ApiResult<MaintenanceExpenseDto>.Ok(dto, string.Empty));
    }

    [HttpDelete("api/v1/maintenance-expenses/{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "MaintenanceBill")]
    public async Task<IActionResult> DeleteExpense(Guid id, CancellationToken ct)
    {
        await svc.DeleteExpenseAsync(id, ct);
        return Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
    }

    [HttpGet("api/v1/maintenance-bills/me/due")]
    [RequirePermission(PermissionAction.View, "MaintenanceBill")]
    public async Task<IActionResult> MyDue([FromServices] IConfigService config, CancellationToken ct)
    {
        var days = await config.GetIntAsync(ConfigKeys.MaintenanceBillReminderDaysBeforeDue, 10, null, ct);
        var items = await svc.ListMyDueBillsAsync(days, ct);
        return Ok(ApiResult<IReadOnlyList<MaintenanceBillDto>>.Ok(items, string.Empty));
    }

    [HttpGet("api/v1/maintenance-bills/{id:guid}")]
    [RequirePermission(PermissionAction.View, "MaintenanceBill")]
    public async Task<IActionResult> GetBill(Guid id, CancellationToken ct)
    {
        var dto = await svc.GetBillAsync(id, ct) ?? throw new NotFoundException("Maintenance bill not found.");
        return Ok(ApiResult<MaintenanceBillDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/maintenance-bills/{id:guid}/payments")]
    [RequirePermission(PermissionAction.View, "MaintenancePayment")]
    public async Task<IActionResult> ListBillPayments(Guid id, CancellationToken ct)
    {
        var items = await svc.ListPaymentsForBillAsync(id, ct);
        return Ok(ApiResult<IReadOnlyList<MaintenancePaymentDto>>.Ok(items, string.Empty));
    }

    [HttpPost("api/v1/maintenance-bills/{id:guid}/waive")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "MaintenanceBill")]
    public async Task<IActionResult> Waive(Guid id, CancellationToken ct)
    {
        var dto = await svc.WaiveBillAsync(id, ct);
        return Ok(ApiResult<MaintenanceBillDto>.Ok(dto, string.Empty));
    }

    // --- Payments ---
    [HttpPost("api/v1/maintenance-payments")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "MaintenancePayment")]
    public async Task<IActionResult> RecordPayment([FromBody] RecordPaymentBody body, CancellationToken ct)
    {
        var dto = await svc.RecordPaymentAsync(new RecordMaintenancePaymentRequest(
            body.MaintenanceBillId, body.Amount, body.Method!, body.Reference, body.PaidAtUtc,
            SubmittedByResident: false, AutoApprove: true), ct);
        return Ok(ApiResult<MaintenancePaymentDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/maintenance-payments/submit")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "MaintenancePayment")]
    public async Task<IActionResult> SubmitPayment([FromBody] RecordPaymentBody body, CancellationToken ct)
    {
        var dto = await svc.RecordPaymentAsync(new RecordMaintenancePaymentRequest(
            body.MaintenanceBillId, body.Amount, body.Method!, body.Reference, body.PaidAtUtc,
            SubmittedByResident: true, AutoApprove: false), ct);
        return Ok(ApiResult<MaintenancePaymentDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/maintenance-payments/pending")]
    [RequirePermission(PermissionAction.Approve, "MaintenancePayment")]
    public async Task<IActionResult> ListPending([FromQuery] Guid communityId, CancellationToken ct)
    {
        var items = await svc.ListPendingPaymentsAsync(communityId, ct);
        return Ok(ApiResult<IReadOnlyList<PendingPaymentDto>>.Ok(items, string.Empty));
    }

    [HttpPost("api/v1/maintenance-payments/{id:guid}/approve")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Approve, "MaintenancePayment")]
    public async Task<IActionResult> ApprovePayment(Guid id, CancellationToken ct)
    {
        var dto = await svc.ApprovePaymentAsync(id, ct);
        return Ok(ApiResult<MaintenancePaymentDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/maintenance-payments/{id:guid}/reject")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Approve, "MaintenancePayment")]
    public async Task<IActionResult> RejectPayment(Guid id, CancellationToken ct)
    {
        var dto = await svc.RejectPaymentAsync(id, ct);
        return Ok(ApiResult<MaintenancePaymentDto>.Ok(dto, string.Empty));
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

    private static void AddEquals(List<FilterTerm> filters, string field, int? value)
    {
        if (value is { } v)
        {
            filters.Add(new FilterTerm(field, FilterOperator.Equals, v.ToString()));
        }
    }

    private static ListQueryParameters BuildParameters(int? page, int? pageSize, string? sort, List<FilterTerm> filters) =>
        new()
        {
            Page = page,
            PageSize = pageSize,
            Filters = filters,
            Sort = EndpointSort.Parse(sort),
        };

    public sealed class SetChargeBody
    {
        [Required] public Guid CommunityId { get; init; }
        [Required] public Guid UnitId { get; init; }
        [Range(0, 99999999)] public decimal Amount { get; init; }
        [MaxLength(200)] public string? Label { get; init; }
        public bool IsActive { get; init; } = true;
    }

    public sealed class BulkSetChargeBody
    {
        [Required] public Guid CommunityId { get; init; }
        public IReadOnlyList<Guid>? UnitIds { get; init; }
        [Range(0, 99999999)] public decimal Amount { get; init; }
        [MaxLength(200)] public string? Label { get; init; }
        public bool IsActive { get; init; } = true;
        public bool ApplyToAllUnits { get; init; }
    }

    public sealed class GenerateBillsBody
    {
        [Required] public Guid CommunityId { get; init; }
        [Range(2000, 3000)] public int PeriodYear { get; init; }
        [Range(1, 12)] public int PeriodMonth { get; init; }
        public DateTime? DueDateUtc { get; init; }
    }

    public sealed class SaveExpenseBody
    {
        [Required] public Guid CommunityId { get; init; }
        [Required(AllowEmptyStrings = false)] [MaxLength(60)] public string? Category { get; init; }
        [Required(AllowEmptyStrings = false)] [MaxLength(200)] public string? Title { get; init; }
        [Range(0.01, 999999999)] public decimal Amount { get; init; }
        [Required] public DateTime SpentOnUtc { get; init; }
        [MaxLength(200)] public string? Vendor { get; init; }
        [MaxLength(1000)] public string? Notes { get; init; }
    }

    public sealed class RecordPaymentBody
    {
        [Required] public Guid MaintenanceBillId { get; init; }
        [Range(0.01, 99999999)] public decimal Amount { get; init; }
        [Required(AllowEmptyStrings = false)] [MaxLength(50)] public string? Method { get; init; }
        [MaxLength(200)] public string? Reference { get; init; }
        public DateTime? PaidAtUtc { get; init; }
    }
}
