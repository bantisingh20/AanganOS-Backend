using UrbanNest.Application.Querying;
using UrbanNest.Contracts.Envelope;

namespace UrbanNest.Application.Messaging;

public interface IMessageOutboxQueryService
{
    Task<PagedData<MessageOutboxLogDto>> QueryAsync(
        ListQueryParameters parameters,
        CancellationToken cancellationToken = default);
}
