namespace UrbanNest.Domain.Identity;

/// <summary>
/// Whether a <see cref="Role"/> is defined platform-wide or for a single community (Req 3.6).
/// A <see cref="Platform"/> role (e.g. a platform-administration role) carries a null
/// <c>CommunityId</c>; a <see cref="Community"/> role is scoped to one community and carries that
/// community's id. The scope is part of the Unique(Name, Scope, CommunityId) guarantee so the same
/// role name can exist once per scope.
/// </summary>
public enum RoleScope
{
    /// <summary>Platform-wide role. CommunityId is null.</summary>
    Platform = 0,

    /// <summary>Per-community role. CommunityId is the owning community.</summary>
    Community = 1,
}
