using App.Application.Querying;
using App.Contracts.Diagnostics;
using App.Contracts.Envelope;

namespace App.Application.Diagnostics;

public interface IErrorLogService
{
    Task<PagedData<ErrorLogSummaryDto>> QueryAsync(
        ListQueryParameters parameters,
        int? statusCode = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default);

    Task<ErrorLogDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> ClearAsync(int? olderThanDays = null, CancellationToken cancellationToken = default);
}
