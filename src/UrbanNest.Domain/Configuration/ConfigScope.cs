namespace UrbanNest.Domain.Config;

/// <summary>
/// The scope at which a <see cref="Configuration"/> row (or <see cref="MasterDataItem"/>) applies
/// (Req 8.1). A <see cref="Platform"/> row is the platform-wide default and carries a null
/// <c>CommunityId</c>; a <see cref="Community"/> row overrides that default for one community and
/// carries that community's id.
/// </summary>
public enum ConfigScope
{
    /// <summary>Platform-wide default. CommunityId is null.</summary>
    Platform = 0,

    /// <summary>Per-community override. CommunityId is the owning community.</summary>
    Community = 1,
}
