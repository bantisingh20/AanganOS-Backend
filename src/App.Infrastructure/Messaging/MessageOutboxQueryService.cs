using App.Application.Messaging;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Messaging;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Messaging;

public sealed class MessageOutboxQueryService(
    AppDbContext db,
    IPageSizePolicy pageSizePolicy,
    IAsyncQueryExecutor executor) : IMessageOutboxQueryService
{
    private static readonly ListQueryConfiguration<MessageOutboxMessage> QueryConfig =
        new ListQueryConfiguration<MessageOutboxMessage>()
            .WithDefaultSort(m => m.CreatedAtUtc, SortDirection.Descending)
            .Sortable("createdAt", m => m.CreatedAtUtc)
            .Sortable("status", m => m.Status)
            .Sortable("channel", m => m.Channel)
            .Sortable("attempts", m => m.Attempts)
            .Filterable("channel", m => m.Channel)
            .Filterable("status", m => m.Status)
            .Filterable("recipient", m => m.Recipient)
            .Filterable("createdAt", m => m.CreatedAtUtc)
            .Searchable(m => m.Recipient)
            .Searchable(m => m.Subject);

    public async Task<PagedData<MessageOutboxLogDto>> QueryAsync(
        ListQueryParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        return await db.MessageOutbox
            .AsNoTracking()
            .ToPagedResultAsync(
                parameters,
                QueryConfig,
                m => new MessageOutboxLogDto(
                    m.Id,
                    m.Channel.ToString(),
                    m.Recipient,
                    m.Subject,
                    m.Body,
                    m.Status.ToString(),
                    m.Attempts,
                    m.NextAttemptUtc,
                    m.LastError,
                    m.CreatedAtUtc),
                pageSizePolicy,
                executor,
                cancellationToken);
    }
}
