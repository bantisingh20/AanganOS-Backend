using App.Domain.Common;

namespace App.Domain.Identity;

public enum MenuSurface
{
    App = 0,
    Admin = 1,
}

public class MenuItem : PlatformEntity
{
    public Guid? ParentId { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string? Icon { get; set; }

    public string? Route { get; set; }

    public MenuSurface Surface { get; set; }

    public string? RequiredPermissionActions { get; set; }

    public string? RequiredPermissionResource { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public bool ShowInSidebar { get; set; } = true;
}
