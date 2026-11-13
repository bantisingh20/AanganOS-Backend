using App.Application.Querying;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Querying;

/// <summary>
/// Default <see cref="IPageSizePolicy"/>: the platform-wide default/max page size bound from
/// configuration via <see cref="PaginationOptions"/> (options pattern — no hardcoded literals,
/// Req 9.4). <see cref="IOptionsSnapshot{T}"/> so a config change is picked up per scope.
///
/// Task 3.1 (Config_Service) can later register a replacement that returns per-community
/// overrides; the shared query helper depends only on <see cref="IPageSizePolicy"/> and is
/// unaffected.
/// </summary>
public sealed class OptionsPageSizePolicy(IOptionsSnapshot<PaginationOptions> options) : IPageSizePolicy
{
    private readonly PaginationOptions _options = options.Value;

    public int DefaultPageSize => _options.DefaultPageSize;

    public int MaxPageSize => _options.MaxPageSize;
}
