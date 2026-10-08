using CommunityOS.Application.Abstractions;
using CommunityOS.Application.Config;
using CommunityOS.Application.Querying;
using CommunityOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using CommunityOS.Domain.Config;

namespace CommunityOS.Infrastructure.Querying;

/// <summary>
/// Config-driven <see cref="IPageSizePolicy"/> that completes the "no hardcoding" seam end to end
/// (Req 65.2, 9.4): the default/max page size can be overridden <b>per community</b> via
/// Config_Service, falling back to the platform-wide default from <see cref="PaginationOptions"/>
/// when no community override exists.
///
/// <para>Replaces <c>OptionsPageSizePolicy</c>. The shared pagination helper still depends only on
/// <see cref="IPageSizePolicy"/>, so wiring the override in changed nothing at the call sites — the
/// whole point of the seam that was left in Task 2.3.</para>
///
/// <para><b>Resolution.</b> The policy's community is the single authorized community of the current
/// scoped request (<see cref="ITenantContext"/>). Values are read from the config store and cached
/// in <see cref="IMemoryCache"/> keyed per community so one tenant's override cannot leak to
/// another (steering). The read uses a <i>synchronous</i> EF query — not a blocked async call —
/// because <see cref="IPageSizePolicy"/> exposes synchronous properties; it is index-backed
/// (IX_Configuration_CommunityId_Key) and touches at most two tiny rows.</para>
/// </summary>
public sealed class ConfigPageSizePolicy : IPageSizePolicy
{
    private readonly int _defaultPageSize;
    private readonly int _maxPageSize;

    public ConfigPageSizePolicy(
        IOptionsSnapshot<PaginationOptions> options,
        AppDbContext db,
        IMemoryCache cache,
        ITenantContext tenant)
    {
        var platform = options.Value;
        var communityId = SingleCommunityOrNull(tenant);

        _defaultPageSize = ResolveInt(db, cache, ConfigKeys.PaginationDefaultPageSize, communityId, platform.DefaultPageSize);
        var resolvedMax = ResolveInt(db, cache, ConfigKeys.PaginationMaxPageSize, communityId, platform.MaxPageSize);

        // Guard: default must never exceed max, whatever config says.
        _maxPageSize = Math.Max(1, resolvedMax);
        _defaultPageSize = Math.Clamp(_defaultPageSize, 1, _maxPageSize);
    }

    public int DefaultPageSize => _defaultPageSize;

    public int MaxPageSize => _maxPageSize;

    private static Guid? SingleCommunityOrNull(ITenantContext tenant)
        => tenant.AuthorizedCommunityIds.Count == 1 ? tenant.AuthorizedCommunityIds.First() : null;

    /// <summary>
    /// Community override wins, else platform config value, else the supplied platform default from
    /// <see cref="PaginationOptions"/>. Cached per (key, community) with a short TTL; config edits
    /// go through Config_Service which invalidates its own cache, and this derived value self-heals
    /// within the TTL.
    /// </summary>
    private static int ResolveInt(AppDbContext db, IMemoryCache cache, string key, Guid? communityId, int platformDefault)
    {
        var cacheKey = $"pagesize:{key}:{(communityId?.ToString() ?? "platform")}";
        return cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

            var rows = db.Set<Configuration>()
                .AsNoTracking()
                .Where(c => c.Key == key &&
                            (c.CommunityId == null || (communityId != null && c.CommunityId == communityId)))
                .Select(c => new { c.CommunityId, c.Value })
                .ToList();

            var raw = rows.FirstOrDefault(r => r.CommunityId == communityId && communityId != null)?.Value
                      ?? rows.FirstOrDefault(r => r.CommunityId == null)?.Value;

            return int.TryParse(raw, out var v) && v >= 1 ? v : platformDefault;
        });
    }
}
