using CommunityOS.Application.Notifications;
using CommunityOS.Domain.Notifications;
using CommunityOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommunityOS.Infrastructure.Notifications;

/// <summary>
/// Default <see cref="INotificationRecipientResolver"/>: maps the single intended recipient user to
/// the OneSignal player ids of their own active <see cref="Device"/> rows (Req 11.3, 64.6). Filters
/// in the database and projects only the player id column (Req 65.1 — no over-fetch, AsNoTracking).
///
/// <para><b>Confidentiality.</b> It returns targets for exactly the <c>recipientUserId</c> passed,
/// never any other user's devices, so a resolved notification can only reach its intended
/// recipient. An empty result (no registered device) is returned as-is; the Task 7.2 worker records
/// a non-delivery rather than broadcasting.</para>
///
/// <para><c>Device</c> is a platform-scoped entity, so the tenant query filter does not hide it;
/// targeting is by user identity, which is the correct boundary for a push target.</para>
/// </summary>
public sealed class DeviceRecipientResolver(AppDbContext db) : INotificationRecipientResolver
{
    public async Task<IReadOnlyCollection<string>> ResolvePlayerIdsAsync(
        Guid recipientUserId, CancellationToken cancellationToken = default)
    {
        if (recipientUserId == Guid.Empty)
        {
            return [];
        }

        return await db.Set<Device>()
            .AsNoTracking()
            .Where(d => d.UserId == recipientUserId && d.IsActive && d.OneSignalPlayerId != string.Empty)
            .Select(d => d.OneSignalPlayerId)
            .ToListAsync(cancellationToken);
    }
}
