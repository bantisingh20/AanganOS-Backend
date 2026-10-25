namespace UrbanNest.Application.Config;

/// <summary>
/// Config_Service (Req 8). Resolves configuration values and master-data lists with
/// community→platform fallback (Req 8.1, 8.3), persists edits and records them via the audit seam
/// (Req 8.2), exposes editable master-data lists (Req 8.4, 66.4), and caches safe reference/config
/// data with invalidation on edit (Req 65.2).
///
/// <para>Fallback rule for every getter: when <paramref name="communityId"/> is supplied and a
/// community-scoped value exists it wins; otherwise the platform-wide default for the same key is
/// returned; otherwise the caller's provided default (or null) is returned.</para>
///
/// <para>Tenant safety: a community value is only ever resolved for the community id the caller
/// passes, and the cache is keyed per (key, communityId) so an override never leaks across tenants.</para>
/// </summary>
public interface IConfigService
{
    /// <summary>
    /// Raw string resolution with community→platform fallback (Req 8.1, 8.3). Returns null when
    /// neither a community nor a platform value exists for the key.
    /// </summary>
    Task<string?> GetAsync(string key, Guid? communityId = null, CancellationToken cancellationToken = default);

    /// <summary>String getter with a caller-supplied fallback used when no value is configured.</summary>
    Task<string> GetStringAsync(string key, string defaultValue, Guid? communityId = null, CancellationToken cancellationToken = default);

    /// <summary>Int getter; returns <paramref name="defaultValue"/> when unset or unparseable (safe parsing).</summary>
    Task<int> GetIntAsync(string key, int defaultValue, Guid? communityId = null, CancellationToken cancellationToken = default);

    /// <summary>Bool getter; returns <paramref name="defaultValue"/> when unset or unparseable (safe parsing).</summary>
    Task<bool> GetBoolAsync(string key, bool defaultValue, Guid? communityId = null, CancellationToken cancellationToken = default);

    /// <summary>Decimal getter (invariant culture); returns <paramref name="defaultValue"/> when unset or unparseable.</summary>
    Task<decimal> GetDecimalAsync(string key, decimal defaultValue, Guid? communityId = null, CancellationToken cancellationToken = default);

    /// <summary>Enum getter (case-insensitive); returns <paramref name="defaultValue"/> when unset or unparseable.</summary>
    Task<TEnum> GetEnumAsync<TEnum>(string key, TEnum defaultValue, Guid? communityId = null, CancellationToken cancellationToken = default)
        where TEnum : struct, Enum;

    /// <summary>
    /// Persist a configuration value (Req 8.2). When <paramref name="communityId"/> is null the
    /// platform default is written; otherwise the community override is written. Records the change
    /// via the audit seam and invalidates the cache so the next read returns the new value (Req 65.2).
    /// </summary>
    Task SetAsync(string key, string value, Guid? communityId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve a master-data list for the given community (Req 8.4, 66.4): the platform defaults
    /// plus any community-scoped entries, community entries overriding platform entries with the
    /// same code, retired (inactive) entries excluded unless <paramref name="includeInactive"/>.
    /// </summary>
    Task<IReadOnlyList<MasterDataEntry>> GetMasterDataAsync(string listKey, Guid? communityId = null, bool includeInactive = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add or update a master-data entry (Req 8.4). Platform or community scope mirrors
    /// <see cref="SetAsync"/>. Records the change via the audit seam and invalidates the list cache.
    /// </summary>
    Task SetMasterDataAsync(string listKey, MasterDataEntry entry, Guid? communityId = null, CancellationToken cancellationToken = default);
}
