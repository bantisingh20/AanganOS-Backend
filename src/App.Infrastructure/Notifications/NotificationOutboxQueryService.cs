using App.Application.Notifications;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Notifications;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Notifications;

public sealed class NotificationOutboxQueryService(
    AppDbContext db,
    IPageSizePolicy pageSizePolicy,
    IAsyncQueryExecutor executor) : INotificationOutboxQueryService
{
    private static readonly ListQueryConfiguration<NotificationOutboxMessage> QueryConfig =
        new ListQueryConfiguration<NotificationOutboxMessage>()
            .WithDefaultSort(m => m.CreatedAtUtc, SortDirection.Descending)
            .Sortable("createdAt", m => m.CreatedAtUtc)
            .Sortable("status", m => m.Status)
            .Filterable("status", m => m.Status)
            .Filterable("category", m => m.Category)
            .Filterable("templateKey", m => m.TemplateKey)
            .Filterable("recipientUserId", m => m.RecipientUserId)
            .Filterable("createdAt", m => m.CreatedAtUtc)
            .Searchable(m => m.Title)
            .Searchable(m => m.TemplateKey);

    public async Task<PagedData<NotificationOutboxLogDto>> QueryAsync(
        ListQueryParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        return await db.NotificationOutbox
            .AsNoTracking()
            .ToPagedResultAsync(
                parameters,
                QueryConfig,
                m => new NotificationOutboxLogDto(
                    m.Id,
                    m.CommunityId,
                    m.TemplateKey,
                    m.Category,
                    m.Channels.ToString(),
                    m.RecipientUserId,
                    m.TargetPlayerId,
                    m.Title,
                    m.Body,
                    m.Status.ToString(),
                    m.Attempts,
                    m.NextAttemptUtc,
                    m.LastError,
                    m.IsRead,
                    m.CreatedAtUtc),
                pageSizePolicy,
                executor,
                cancellationToken);
    }
}
