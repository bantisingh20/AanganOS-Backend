using App.Application.Abstractions;
using App.Application.Subscription;
using App.Domain.Subscription;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Subscription;

public sealed class SubscriptionGate(AppDbContext db, IClock clock) : ISubscriptionGate
{
    public async Task<bool> IsExpiredForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var expiresAtUtc = await db.OrgSubscriptions.AsNoTracking().IgnoreQueryFilters()
            .Where(s => s.OrganizationId == organizationId && !s.IsDeleted)
            .Select(s => (DateTime?)s.ExpiresAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return expiresAtUtc is not { } expiry || expiry <= clock.UtcNow;
    }
}
