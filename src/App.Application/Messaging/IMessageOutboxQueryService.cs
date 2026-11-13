using App.Application.Querying;
using App.Contracts.Envelope;

namespace App.Application.Messaging;

public interface IMessageOutboxQueryService
{
    Task<PagedData<MessageOutboxLogDto>> QueryAsync(
        ListQueryParameters parameters,
        CancellationToken cancellationToken = default);
}
