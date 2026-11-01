using App.Application.Querying;
using App.Contracts.Envelope;

namespace App.Application.MaintenanceBilling;

public interface IMaintenanceBillingService
{
    Task<MaintenanceChargeDto> SetChargeAsync(SetMaintenanceChargeRequest request, CancellationToken cancellationToken = default);

    Task<BulkSetMaintenanceChargeResult> BulkSetChargesAsync(BulkSetMaintenanceChargeRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaintenanceChargeDto>> ListChargesAsync(Guid communityId, CancellationToken cancellationToken = default);

    Task<GenerateBillsResult> GenerateBillsAsync(GenerateBillsRequest request, CancellationToken cancellationToken = default);

    Task<PagedData<MaintenanceBillDto>> ListBillsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default);

    Task<MaintenanceBillDto?> GetBillAsync(Guid billId, CancellationToken cancellationToken = default);

    Task<MaintenancePaymentDto> RecordPaymentAsync(RecordMaintenancePaymentRequest request, CancellationToken cancellationToken = default);

    Task<MaintenancePaymentDto> ApprovePaymentAsync(Guid paymentId, CancellationToken cancellationToken = default);

    Task<MaintenancePaymentDto> RejectPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingPaymentDto>> ListPendingPaymentsAsync(Guid communityId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaintenancePaymentDto>> ListPaymentsForBillAsync(Guid billId, CancellationToken cancellationToken = default);

    Task<MaintenanceBillDto> WaiveBillAsync(Guid billId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaintenanceBillDto>> ListMyDueBillsAsync(int reminderDaysBeforeDue, CancellationToken cancellationToken = default);

    Task<PagedData<MaintenanceBillDto>> ListMyBillsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default);

    Task<MaintenanceChargeDto> DeactivateChargeAsync(Guid chargeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaintenanceDefaulterDto>> ListDefaultersAsync(CancellationToken cancellationToken = default);

    Task<MaintenanceCollectionSummaryDto> GetCollectionSummaryAsync(Guid? communityId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaintenanceExpenseDto>> ListExpensesAsync(Guid communityId, CancellationToken cancellationToken = default);

    Task<MaintenanceExpenseDto> CreateExpenseAsync(SaveMaintenanceExpenseRequest request, CancellationToken cancellationToken = default);

    Task<MaintenanceExpenseDto> UpdateExpenseAsync(Guid expenseId, SaveMaintenanceExpenseRequest request, CancellationToken cancellationToken = default);

    Task DeleteExpenseAsync(Guid expenseId, CancellationToken cancellationToken = default);
}
