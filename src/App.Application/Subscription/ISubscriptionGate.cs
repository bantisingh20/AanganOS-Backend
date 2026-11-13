namespace App.Application.Subscription;

public interface ISubscriptionGate
{
    Task<bool> IsExpiredForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
