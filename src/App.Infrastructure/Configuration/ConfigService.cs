using System.Globalization;
using App.Application.Abstractions;
using App.Application.Abstractions.Audit;
using App.Application.Config;
using App.Application.Exceptions;
using App.Domain.Config;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace App.Infrastructure.Config;

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

    private const string ValueGenCacheKey = "cfg:v:gen";

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
        // Resolve the community's organization so the org tier can apply. A platform-only read
        // (communityId null) has no org tier. The lookup is cached per community id.
        Guid? organizationId = communityId is { } cid ? await ResolveCommunityOrganizationAsync(cid, cancellationToken) : null;

        // Pull the platform row + the community row + the org row in one query, then apply the
        // fallback in memory — avoids multiple round trips without an N+1 risk.
        var rows = await db.Set<Configuration>()
            .AsNoTracking()
            .Where(c => c.Key == key &&
                        (c.CommunityId == null && c.OrganizationId == null ||
                         (communityId != null && c.CommunityId == communityId) ||
                         (organizationId != null && c.OrganizationId == organizationId)))
            .Select(c => new { c.CommunityId, c.OrganizationId, c.Value })
            .ToListAsync(cancellationToken);

        // Community override wins; else the organization override; else the platform default; else null.
        var community = rows.FirstOrDefault(r => communityId != null && r.CommunityId == communityId);
        if (community is not null)
        {
            return community.Value;
        }

        var organization = rows.FirstOrDefault(r => organizationId != null && r.OrganizationId == organizationId);
        if (organization is not null)
        {
            return organization.Value;
        }

        return rows.FirstOrDefault(r => r.CommunityId == null && r.OrganizationId == null)?.Value;
    }

    private async Task<Guid?> ResolveCommunityOrganizationAsync(Guid communityId, CancellationToken cancellationToken)
    {
        var cacheKey = $"cfg:org-of:{communityId}";
        if (cache.TryGetValue(cacheKey, out Guid? cached))
        {
            return cached;
        }

        var orgId = await db.Communities.AsNoTracking().IgnoreQueryFilters()
            .Where(c => c.Id == communityId)
            .Select(c => (Guid?)c.OrganizationId)
            .FirstOrDefaultAsync(cancellationToken);

        cache.Set(cacheKey, orgId, CacheTtl);
        return orgId;
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

    public async Task<IReadOnlyList<ConfigRow>> ListConfigAsync(string? search = null, CancellationToken cancellationToken = default)
    {
        var query = db.Set<Configuration>().AsNoTracking();
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(c => c.Key.Contains(term) || c.Value.Contains(term));
        }
        return await query
            .OrderBy(c => c.Key).ThenBy(c => c.Scope)
            .Take(500)
            .Select(c => new ConfigRow(c.Id, c.Key, c.Scope, c.OrganizationId, c.CommunityId, c.Value))
            .ToListAsync(cancellationToken);
    }

    public async Task<ConfigRow> UpsertConfigAsync(UpsertConfigRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = request.Key?.Trim() ?? string.Empty;
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(request.Value);

        Guid? organizationId = request.Scope == ConfigScope.Organization ? request.OrganizationId : null;
        Guid? communityId = request.Scope == ConfigScope.Community ? request.CommunityId : null;
        if (request.Scope == ConfigScope.Organization && organizationId is null)
        {
            throw new ValidationException("organizationId", "An organization is required for an organization-scoped config.");
        }
        if (request.Scope == ConfigScope.Community && communityId is null)
        {
            throw new ValidationException("communityId", "A community is required for a community-scoped config.");
        }

        var existing = await db.Set<Configuration>().FirstOrDefaultAsync(
            c => c.Key == key && c.CommunityId == communityId && c.OrganizationId == organizationId, cancellationToken);
        var oldValue = existing?.Value;

        if (existing is null)
        {
            existing = new Configuration
            {
                Key = key,
                Scope = request.Scope,
                OrganizationId = organizationId,
                CommunityId = communityId,
                Value = request.Value,
            };
            db.Set<Configuration>().Add(existing);
        }
        else
        {
            existing.Value = request.Value;
        }

        await db.SaveChangesAsync(cancellationToken);
        InvalidateValue(key, communityId);

        await audit.RecordConfigChangeAsync(
            new ConfigChange("Configuration", key, communityId, oldValue, request.Value, tenant.UserId),
            cancellationToken);

        return new ConfigRow(existing.Id, existing.Key, existing.Scope, existing.OrganizationId, existing.CommunityId, existing.Value);
    }

    public async Task DeleteConfigAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.Set<Configuration>().FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Configuration not found.");

        var key = row.Key;
        var communityId = row.CommunityId;
        db.Set<Configuration>().Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        InvalidateValue(key, communityId);

        await audit.RecordConfigChangeAsync(
            new ConfigChange("Configuration", key, communityId, row.Value, null, tenant.UserId),
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

    private string ValueCacheKey(string key, Guid? communityId)
    {
        var gen = cache.TryGetValue(ValueGenCacheKey, out long g) ? g : 0;
        return $"cfg:v:{gen}:{key}:{(communityId?.ToString() ?? "platform")}";
    }

    private static string ListCacheKey(string listKey, Guid? communityId, bool includeInactive)
        => $"cfg:l:{listKey}:{(communityId?.ToString() ?? "platform")}:{(includeInactive ? "all" : "active")}";

    private void InvalidateValue(string key, Guid? communityId)
    {
        // Bump a global generation so EVERY cached value read misses and re-resolves. A platform or
        // organization edit changes the fallback many communities see, and those community reads are
        // cached under their own keys — enumerating them is not possible, so the generation token
        // invalidates the whole value namespace at once (correct over clever; config writes are rare).
        var gen = cache.TryGetValue(ValueGenCacheKey, out long g) ? g : 0;
        cache.Set(ValueGenCacheKey, gen + 1);
    }

    private void InvalidateList(string listKey, Guid? communityId)
    {
        cache.Remove(ListCacheKey(listKey, communityId, includeInactive: false));
        cache.Remove(ListCacheKey(listKey, communityId, includeInactive: true));
    }
}
