namespace App.Application.Subscription;

public interface ISubscriptionService
{
    Task<SubscriptionDto> GetMineAsync(CancellationToken cancellationToken = default);

    Task<SubscriptionDto> GetByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);

    Task<SubscriptionDto> SetExpiryAsync(
        Guid organizationId,
        int planMonths,
        DateTime startsAtUtc,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default);

    Task<SubscriptionDto> RequestRenewalAsync(int planMonths, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RenewalRequestDto>> ListPendingRenewalsAsync(CancellationToken cancellationToken = default);

    Task<SubscriptionDto> ApproveRenewalAsync(
        Guid requestId,
        int planMonths,
        DateTime startsAtUtc,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default);
}
