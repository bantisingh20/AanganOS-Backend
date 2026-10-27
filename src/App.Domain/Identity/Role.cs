using App.Domain.Common;

namespace App.Domain.Identity;

public enum RoleSurface
{
    App = 0,
    Admin = 1,
    Both = 2,
}

public class Role : PlatformEntity
{
    public string Name { get; set; } = string.Empty;

    public RoleScope Scope { get; set; }

    public Guid? CommunityId { get; set; }

    public RoleSurface Surface { get; set; } = RoleSurface.App;

    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}
