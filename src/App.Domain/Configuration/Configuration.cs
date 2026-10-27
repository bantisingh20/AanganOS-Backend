using App.Domain.Common;

namespace App.Domain.Config;


public class Configuration : PlatformEntity
{
    public string Key { get; set; } = string.Empty;

    /// <summary>Platform-wide default or a per-community override (Req 8.1).</summary>
    public ConfigScope Scope { get; set; }

    public Guid? CommunityId { get; set; }

    public Guid? OrganizationId { get; set; }

    public string Value { get; set; } = string.Empty;
}
