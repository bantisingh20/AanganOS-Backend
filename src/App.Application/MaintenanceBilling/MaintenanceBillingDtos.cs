namespace App.Application.MaintenanceBilling;

public sealed record MaintenanceChargeDto(
    Guid Id,
    Guid CommunityId,
    Guid UnitId,
    decimal Amount,
    string Label,
    bool IsActive,
    string? UnitNumber = null);

public sealed record MaintenanceDefaulterDto(
    Guid UnitId,
    string UnitNumber,
    string ResidentName,
    int UnpaidCount);

public sealed record MaintenanceCollectionSummaryDto(
    decimal TotalCollected,
    decimal TotalOutstanding,
    int PaidBillCount,
    int UnpaidBillCount,
    IReadOnlyList<MaintenanceCollectionByCommunityDto> ByCommunity,
    decimal TotalReceived = 0m);

public sealed record MaintenanceCollectionByCommunityDto(
    Guid CommunityId,
    string CommunityName,
    string OrganizationName,
    decimal Collected,
    decimal Outstanding,
    decimal Spent = 0m,
    decimal Received = 0m);

public sealed record MaintenanceExpenseDto(
    Guid Id,
    Guid CommunityId,
    string Direction,
    string Category,
    string Title,
    decimal Amount,
    DateTime SpentOnUtc,
    string Vendor,
    string Notes);

public sealed record SaveMaintenanceExpenseRequest(
    Guid CommunityId,
    string Direction,
    string Category,
    string Title,
    decimal Amount,
    DateTime SpentOnUtc,
    string? Vendor,
    string? Notes);

public sealed record SetMaintenanceChargeRequest(
    Guid CommunityId,
    Guid UnitId,
    decimal Amount,
    string? Label,
    bool IsActive);

public sealed record BulkSetMaintenanceChargeRequest(
    Guid CommunityId,
    IReadOnlyList<Guid> UnitIds,
    decimal Amount,
    string? Label,
    bool IsActive,
    bool ApplyToAllUnits);

public sealed record BulkSetMaintenanceChargeResult(
    Guid CommunityId,
    int Created,
    int Updated);

public sealed record MaintenanceBillDto(
    Guid Id,
    Guid CommunityId,
    Guid UnitId,
    int PeriodYear,
    int PeriodMonth,
    decimal Amount,
    decimal AmountPaid,
    decimal AmountDue,
    string Label,
    string Status,
    DateTime? DueDateUtc,
    DateTime IssuedAtUtc,
    bool PendingApproval = false,
    string? UnitNumber = null,
    string? CommunityName = null,
    string? OrganizationName = null);

public sealed record GenerateBillsRequest(
    Guid CommunityId,
    int PeriodYear,
    int PeriodMonth,
    DateTime? DueDateUtc);

public sealed record GenerateBillsResult(
    Guid CommunityId,
    int PeriodYear,
    int PeriodMonth,
    int BillsCreated,
    int UnitsSkipped);

public sealed record MaintenancePaymentDto(
    Guid Id,
    Guid CommunityId,
    Guid MaintenanceBillId,
    Guid UnitId,
    decimal Amount,
    string Method,
    string Reference,
    DateTime PaidAtUtc,
    string Status,
    bool SubmittedByResident,
    DateTime? ApprovedAtUtc);

public sealed record RecordMaintenancePaymentRequest(
    Guid MaintenanceBillId,
    decimal Amount,
    string Method,
    string? Reference,
    DateTime? PaidAtUtc,
    bool SubmittedByResident = false,
    bool AutoApprove = true);

public sealed record PendingPaymentDto(
    Guid Id,
    Guid CommunityId,
    Guid MaintenanceBillId,
    Guid UnitId,
    int PeriodYear,
    int PeriodMonth,
    decimal Amount,
    string Method,
    string Reference,
    DateTime PaidAtUtc,
    bool SubmittedByResident);
