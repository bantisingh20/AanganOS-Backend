using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Config;

/// <summary>
/// A single configuration value identified by <see cref="Key"/> and scoped platform-wide or to one
/// community (Req 8.1). Community-scoped rows override the platform default for the same key; when
/// a community row is absent Config_Service falls back to the platform row (Req 8.3).
///
/// <para><b>Tenant-filter modeling choice.</b> Configuration derives from
/// <see cref="PlatformEntity"/> — <i>not</i> <see cref="TenantOwnedEntity"/> — on purpose. The
/// global tenant query filter hides any row whose <c>CommunityId</c> is not in the caller's
/// authorized set, which would make platform-wide defaults (and other communities' rows needed for
/// nothing here, but platform rows specifically) invisible and break the fallback in Req 8.3.
/// A platform-scoped entity gets only the soft-delete filter, so both platform rows and
/// community rows remain physically readable. Tenant isolation of <i>community-scoped</i> rows is
/// therefore enforced one layer up, inside Config_Service: it only ever resolves a community row
/// for the community id the caller passed, and the cache is keyed per (key, communityId) so one
/// tenant's override can never be served to another (steering: cache must not leak across tenants).
/// </para>
/// </summary>
public class Configuration : PlatformEntity
{
    /// <summary>Stable identifier of the setting (e.g. <c>pagination.maxPageSize</c>). Required.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Platform-wide default or a per-community override (Req 8.1).</summary>
    public ConfigScope Scope { get; set; }

    /// <summary>
    /// Owning community for a <see cref="ConfigScope.Community"/> row; null for a
    /// <see cref="ConfigScope.Platform"/> row. Part of the Unique(Key, Scope, CommunityId) index.
    /// </summary>
    public Guid? CommunityId { get; set; }

    /// <summary>The stored value as a string; typed getters parse it to the requested CLR type.</summary>
    public string Value { get; set; } = string.Empty;
}
