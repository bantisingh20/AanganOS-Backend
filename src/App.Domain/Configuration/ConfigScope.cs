namespace App.Domain.Config;


public enum ConfigScope
{
    /// <summary>Platform-wide default. CommunityId is null.</summary>
    Platform = 0,

    /// <summary>Per-community override. CommunityId is the owning community.</summary>
    Community = 1,

    /// <summary>Per-organization override applying to all of its communities. OrganizationId is the owning org.</summary>
    Organization = 2,
}
