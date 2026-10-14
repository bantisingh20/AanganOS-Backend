using CommunityOS.Application.Abstractions;
using CommunityOS.Application.Notifications;
using CommunityOS.Application.Querying;
using CommunityOS.Contracts.Envelope;
using CommunityOS.Domain.Notifications;
using CommunityOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommunityOS.Infrastructure.Notifications;

/// <summary>
/// EF-backed <see cref="INotificationInboxService"/> — the in-app INBOX read side over the existing
/// transactional-outbox table (Task 20261008). The rows are written by the ONE common helper
/// (<see cref="INotificationService.EnqueueAsync"/>) that every feature already calls, so this
/// service adds NO per-feature save path — it only reads a recipient's own rows and flips their
/// read flag.
///
/// <para><b>Self-scoped.</b> Every query is pinned to <c>RecipientUserId == </c> the current token's
/// user id, so a caller only ever sees/clears their OWN notifications (Req 11.3, 64.6). The tenant
/// global filter still applies on top (the row carries its originating community); combined, a user
/// sees their own notifications across the communities they belong to. Reads use <c>AsNoTracking</c>.</para>
/// </summary>
public sealed class NotificationInboxService(AppDbContext db, ITenantContext tenantContext) : INotificationInboxService
{
    public async Task<PagedData<NotificationInboxItemDto>> ListAsync(
        ListQueryParameters parameters,
        bool unreadOnly = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!TryGetUserId(out var userId))
        {
            return Empty(parameters);
        }

        var page = parameters.Page is null or < 1 ? 1 : parameters.Page.Value;
        var size = parameters.PageSize is null or < 1 ? 20 : Math.Min(parameters.PageSize.Value, 100);

        var query = db.NotificationOutbox.AsNoTracking().Where(m => m.RecipientUserId == userId);
        if (unreadOnly)
        {
            query = query.Where(m => !m.IsRead);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(m => m.CreatedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(m => new NotificationInboxItemDto(
                m.Id, m.Category, m.Title, m.Body, m.PayloadJson, m.IsRead, m.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedData<NotificationInboxItemDto>
        {
            Items = items,
            Page = page,
            PageSize = size,
            TotalCount = total,
        };
    }

    public async Task<int> UnreadCountAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return 0;
        }
        return await db.NotificationOutbox.AsNoTracking()
            .CountAsync(m => m.RecipientUserId == userId && !m.IsRead, cancellationToken);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return;
        }
        // Tracked load pinned to the caller's own row (a foreign/unknown id → no-op, idempotent).
        var row = await db.NotificationOutbox
            .FirstOrDefaultAsync(m => m.Id == id && m.RecipientUserId == userId, cancellationToken);
        if (row is null || row.IsRead)
        {
            return;
        }
        row.IsRead = true;
        row.ReadAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return 0;
        }
        var now = DateTime.UtcNow;
        // Bounded by the recipient's own unread rows; set-based update avoids loading them all.
        return await db.NotificationOutbox
            .Where(m => m.RecipientUserId == userId && !m.IsRead)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.IsRead, true).SetProperty(m => m.ReadAtUtc, now),
                cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return;
        }
        // Tracked load pinned to the caller's own row. The global soft-delete filter means a row
        // already deleted won't be found → no-op (idempotent). Set IsDeleted (+ DeletedAtUtc) rather
        // than removing — the data is retained (Req 3.7) and the inbox query hides it automatically.
        var row = await db.NotificationOutbox
            .FirstOrDefaultAsync(m => m.Id == id && m.RecipientUserId == userId, cancellationToken);
        if (row is null)
        {
            return;
        }
        row.IsDeleted = true;
        row.DeletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return 0;
        }
        var now = DateTime.UtcNow;
        // Set-based soft delete of the caller's own (not-yet-deleted) rows. ExecuteUpdate runs against
        // the global soft-delete filter, so already-deleted rows are excluded automatically.
        return await db.NotificationOutbox
            .Where(m => m.RecipientUserId == userId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.IsDeleted, true).SetProperty(m => m.DeletedAtUtc, now),
                cancellationToken);
    }

    private bool TryGetUserId(out Guid userId) => Guid.TryParse(tenantContext.UserId, out userId);

    private static PagedData<NotificationInboxItemDto> Empty(ListQueryParameters parameters) => new()
    {
        Items = [],
        Page = parameters.Page is null or < 1 ? 1 : parameters.Page.Value,
        PageSize = parameters.PageSize is null or < 1 ? 20 : parameters.PageSize.Value,
        TotalCount = 0,
    };
}
