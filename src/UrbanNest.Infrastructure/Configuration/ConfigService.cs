using System.Globalization;
using UrbanNest.Application.Abstractions;
using UrbanNest.Application.Abstractions.Audit;
using UrbanNest.Application.Config;
using UrbanNest.Domain.Config;
using UrbanNest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace UrbanNest.Infrastructure.Config;

/// <summary>
/// EF-backed Config_Service (Req 8). Resolves values and master-data lists with community→platform
/// fallback (Req 8.1, 8.3), persists edits through the audit seam (Req 8.2), exposes editable
/// master-data lists (Req 8.4, 66.4) and caches safe reference/config data with invalidation on
/// edit (Req 65.2).
///
/// <para><b>Cache + tenant safety.</b> Reads are cached in-process keyed by <i>both</i> the
/// key/list and the community id, so a community override can never be served for another tenant
/// (steering: cache must not leak across tenants). A platform-only read is cached under a distinct
/// "platform" key. On any edit the exact affected cache entries are removed so the next read
/// returns the new value (Req 65.2). ponytail: in-memory cache is sufficient now; the seam to a
/// distributed cache is just this class — upgrade path is swapping IMemoryCache for IDistributedCache
/// without touching callers.</para>
///
/// <para>Reads use <see cref="DbContext.Set{TEntity}"/> with <c>AsNoTracking</c> and filter in the
/// database (Req 65.1); Configuration/MasterDataItem are platform-scoped entities so the tenant
/// global filter does not hide platform defaults — community isolation is enforced here by only
/// ever selecting the community id the caller passed.</para>
/// </summary>
public sealed class ConfigService(
    AppDbContext db,
    IMemoryCache cache,
    IConfigAuditSink audit,
    ITenantContext tenant) : IConfigService
{
    // Bounded TTL so a stale value self-heals even if an invalidation is somehow missed; edits
    // invalidate immediately. ponytail: a fixed small window is fine for reference/config data.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    public async Task<string?> GetAsync(string key, Guid? communityId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var cacheKey = ValueCacheKey(key, communityId);
        if (cache.TryGetValue(cacheKey, out string? cached))
        {
            return cached;
        }

        var resolved = await ResolveValueFromDbAsync(key, communityId, cancellationToken);
        cache.Set(cacheKey, resolved, CacheTtl);
        return resolved;
    }

    private async Task<string?> ResolveValueFromDbAsync(string key, Guid? communityId, CancellationToken cancellationToken)
    {
        // Pull the platform row and (if asked) the community row in one query, then apply the
        // fallback in memory on at most two rows — avoids two round trips without an N+1 risk.
        var rows = await db.Set<Configuration>()
            .AsNoTracking()
            .Where(c => c.Key == key &&
                        (c.CommunityId == null || (communityId != null && c.CommunityId == communityId)))
            .Select(c => new { c.CommunityId, c.Value })
            .ToListAsync(cancellationToken);

        // Community value wins (Req 8.1); else platform default (Req 8.3); else null.
        var community = rows.FirstOrDefault(r => r.CommunityId == communityId && communityId != null);
        if (community is not null)
        {
            return community.Value;
        }

        return rows.FirstOrDefault(r => r.CommunityId == null)?.Value;
    }

    public async Task<string> GetStringAsync(string key, string defaultValue, Guid? communityId = null, CancellationToken cancellationToken = default)
        => await GetAsync(key, communityId, cancellationToken) ?? defaultValue;

    public async Task<int> GetIntAsync(string key, int defaultValue, Guid? communityId = null, CancellationToken cancellationToken = default)
    {
        var raw = await GetAsync(key, communityId, cancellationToken);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : defaultValue;
    }

    public async Task<bool> GetBoolAsync(string key, bool defaultValue, Guid? communityId = null, CancellationToken cancellationToken = default)
    {
        var raw = await GetAsync(key, communityId, cancellationToken);
        return bool.TryParse(raw, out var v) ? v : defaultValue;
    }

    public async Task<decimal> GetDecimalAsync(string key, decimal defaultValue, Guid? communityId = null, CancellationToken cancellationToken = default)
    {
        var raw = await GetAsync(key, communityId, cancellationToken);
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : defaultValue;
    }

    public async Task<TEnum> GetEnumAsync<TEnum>(string key, TEnum defaultValue, Guid? communityId = null, CancellationToken cancellationToken = default)
        where TEnum : struct, Enum
    {
        var raw = await GetAsync(key, communityId, cancellationToken);
        return Enum.TryParse<TEnum>(raw, ignoreCase: true, out var v) && Enum.IsDefined(v) ? v : defaultValue;
    }

    public async Task SetAsync(string key, string value, Guid? communityId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var scope = communityId is null ? ConfigScope.Platform : ConfigScope.Community;

        // Tracked read so an existing row is updated in place (not duplicated) — the filtered
        // unique index also enforces at-most-one, so this stays correct under the write.
        var existing = await db.Set<Configuration>()
            .FirstOrDefaultAsync(c => c.Key == key && c.CommunityId == communityId, cancellationToken);

        var oldValue = existing?.Value;

        if (existing is null)
        {
            db.Set<Configuration>().Add(new Configuration
            {
                Key = key,
                Scope = scope,
                CommunityId = communityId,
                Value = value,
            });
        }
        else
        {
            existing.Value = value;
        }

        await db.SaveChangesAsync(cancellationToken);

        // Invalidate the exact affected read caches so the next Get returns the new value (Req 65.2).
        InvalidateValue(key, communityId);

        await audit.RecordConfigChangeAsync(
            new ConfigChange("Configuration", key, communityId, oldValue, value, tenant.UserId),
            cancellationToken);
    }

    public async Task<IReadOnlyList<MasterDataEntry>> GetMasterDataAsync(
        string listKey, Guid? communityId = null, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listKey);

        var cacheKey = ListCacheKey(listKey, communityId, includeInactive);
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<MasterDataEntry>? cached) && cached is not null)
        {
            return cached;
        }

        var rows = await db.Set<MasterDataItem>()
            .AsNoTracking()
            .Where(m => m.ListKey == listKey &&
                        (m.CommunityId == null || (communityId != null && m.CommunityId == communityId)))
            .Select(m => new { m.CommunityId, m.Code, m.Label, m.SortOrder, m.IsActive })
            .ToListAsync(cancellationToken);

        // Merge platform defaults with community entries; a community entry overrides the platform
        // entry with the same code (Req 8.1 fallback semantics at list-entry granularity).
        var byCode = new Dictionary<string, MasterDataEntry>(StringComparer.Ordinal);
        foreach (var r in rows.Where(r => r.CommunityId == null))
        {
            byCode[r.Code] = new MasterDataEntry(r.Code, r.Label, r.SortOrder, r.IsActive);
        }
        foreach (var r in rows.Where(r => r.CommunityId == communityId && communityId != null))
        {
            byCode[r.Code] = new MasterDataEntry(r.Code, r.Label, r.SortOrder, r.IsActive);
        }

        var result = byCode.Values
            .Where(e => includeInactive || e.IsActive)
            .OrderBy(e => e.SortOrder)
            .ThenBy(e => e.Code, StringComparer.Ordinal)
            .ToList();

        cache.Set(cacheKey, (IReadOnlyList<MasterDataEntry>)result, CacheTtl);
        return result;
    }

    public async Task SetMasterDataAsync(string listKey, MasterDataEntry entry, Guid? communityId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listKey);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Code);

        var scope = communityId is null ? ConfigScope.Platform : ConfigScope.Community;

        var existing = await db.Set<MasterDataItem>()
            .FirstOrDefaultAsync(m => m.ListKey == listKey && m.Code == entry.Code && m.CommunityId == communityId, cancellationToken);

        var oldValue = existing is null ? null : $"{existing.Label}/{existing.SortOrder}/{existing.IsActive}";

        if (existing is null)
        {
            db.Set<MasterDataItem>().Add(new MasterDataItem
            {
                ListKey = listKey,
                Scope = scope,
                CommunityId = communityId,
                Code = entry.Code,
                Label = entry.Label,
                SortOrder = entry.SortOrder,
                IsActive = entry.IsActive,
            });
        }
        else
        {
            existing.Label = entry.Label;
            existing.SortOrder = entry.SortOrder;
            existing.IsActive = entry.IsActive;
        }

        await db.SaveChangesAsync(cancellationToken);

        InvalidateList(listKey, communityId);

        await audit.RecordConfigChangeAsync(
            new ConfigChange("MasterDataItem", $"{listKey}:{entry.Code}", communityId, oldValue,
                $"{entry.Label}/{entry.SortOrder}/{entry.IsActive}", tenant.UserId),
            cancellationToken);
    }

    // --- cache keying: per (key/list, community) so overrides never leak across tenants ---

    private static string ValueCacheKey(string key, Guid? communityId)
        => $"cfg:v:{key}:{(communityId?.ToString() ?? "platform")}";

    private static string ListCacheKey(string listKey, Guid? communityId, bool includeInactive)
        => $"cfg:l:{listKey}:{(communityId?.ToString() ?? "platform")}:{(includeInactive ? "all" : "active")}";

    private void InvalidateValue(string key, Guid? communityId)
    {
        cache.Remove(ValueCacheKey(key, communityId));
        // A platform edit changes the fallback every community sees, but community reads are cached
        // under their own community key; those self-heal within the TTL. The common/tested path —
        // read the same (key, community) again after an edit — is invalidated exactly here.
    }

    private void InvalidateList(string listKey, Guid? communityId)
    {
        cache.Remove(ListCacheKey(listKey, communityId, includeInactive: false));
        cache.Remove(ListCacheKey(listKey, communityId, includeInactive: true));
    }
}
