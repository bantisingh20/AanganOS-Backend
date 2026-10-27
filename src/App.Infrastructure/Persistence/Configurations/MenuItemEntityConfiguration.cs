using App.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

public sealed class MenuItemEntityConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        builder.ToTable("MenuItems");

        builder.Property(m => m.Key).IsRequired().HasMaxLength(128);
        builder.Property(m => m.Label).IsRequired().HasMaxLength(128);
        builder.Property(m => m.Icon).HasMaxLength(64);
        builder.Property(m => m.Route).HasMaxLength(256);
        builder.Property(m => m.Surface).HasConversion<int>();
        builder.Property(m => m.RequiredPermissionActions).HasMaxLength(64);
        builder.Property(m => m.RequiredPermissionResource).HasMaxLength(128);
        builder.Property(m => m.ShowInSidebar).HasDefaultValue(true);

        builder.HasIndex(m => m.Key).IsUnique().HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_MenuItem_Key");

        builder.HasIndex(m => new { m.Surface, m.ParentId, m.SortOrder })
            .HasDatabaseName("IX_MenuItem_Surface_Parent_Sort");

        builder.HasData(Seed());
    }

    private static readonly DateTime SeedTimestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Guid DeterministicGuid(string input)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("menu|" + input));
        return new Guid(bytes);
    }

    private static MenuItem Item(
        string key,
        string label,
        MenuSurface surface,
        string? route,
        string? icon,
        int sortOrder,
        string? parentKey = null,
        PermissionAction[]? actions = null,
        string? resource = null) => new()
        {
            Id = DeterministicGuid(key),
            ParentId = parentKey is null ? null : DeterministicGuid(parentKey),
            Key = key,
            Label = label,
            Icon = icon,
            Route = route,
            Surface = surface,
            RequiredPermissionActions = actions is { Length: > 0 } ? string.Join(',', actions.Select(a => (int)a)) : null,
            RequiredPermissionResource = resource,
            SortOrder = sortOrder,
            IsActive = true,
            ShowInSidebar = true,
            CreatedAtUtc = SeedTimestamp,
            CreatedBy = "seed",
            RowVersion = [],
        };

    private static IEnumerable<MenuItem> Seed()
    {
        var admin = MenuSurface.Admin;
        var app = MenuSurface.App;

        return new[]
        {
            Item("admin.dashboard", "Dashboard", admin, "/", "AppstoreOutlined", 0),
            Item("admin.organizations", "Organizations", admin, "/organizations", "BankOutlined", 10, actions: [PermissionAction.View], resource: "Organization"),
            Item("admin.communities", "Communities", admin, "/communities", "ApartmentOutlined", 20, actions: [PermissionAction.View], resource: "Community"),
            Item("admin.units", "Towers & Units", admin, "/units", "HomeOutlined", 30, actions: [PermissionAction.View], resource: "Unit"),
            Item("admin.residents", "Residents", admin, "/residents", "TeamOutlined", 40, actions: [PermissionAction.View], resource: "Resident"),
            Item("admin.announcements", "Announcements", admin, "/announcements", "NotificationOutlined", 50, actions: [PermissionAction.View], resource: "Announcement"),
            Item("admin.maintenance", "Maintenance", admin, "/maintenance", "ToolOutlined", 60, actions: [PermissionAction.View], resource: "Asset"),
            Item("admin.visitors", "Visitors", admin, "/visitors", "IdcardOutlined", 70, actions: [PermissionAction.View], resource: "Visitor"),
            Item("admin.amenities", "Amenities", admin, "/amenities", "CoffeeOutlined", 80, actions: [PermissionAction.View], resource: "Amenity"),
            Item("admin.notifications", "Notifications", admin, "/notifications", "MessageOutlined", 90, actions: [PermissionAction.View], resource: "Audit"),
            Item("admin.users", "Users & Roles", admin, "/users", "UserOutlined", 100, actions: [PermissionAction.View], resource: "User"),
            Item("admin.adminusers", "Admin Users", admin, "/admin-users", "SafetyOutlined", 105, actions: [PermissionAction.Create], resource: "User"),
            Item("admin.menu", "Menu Management", admin, "/menu-management", "MenuOutlined", 110, actions: [PermissionAction.Edit], resource: "Role"),
            Item("admin.config", "Configuration", admin, "/config", "SettingOutlined", 115, actions: [PermissionAction.Edit], resource: "Configuration"),

            Item("app.home", "Home", app, "home", "home", 0),
            Item("app.services", "Services", app, "services", "grid", 10),
            Item("app.visitors", "Visitors", app, null, "people", 20, parentKey: "app.services"),
            Item("app.visitors.invite", "Invite Visitor", app, "inviteVisitor", "person-add", 0, parentKey: "app.visitors", actions: [PermissionAction.Create], resource: "VisitPass"),
            Item("app.visitors.my", "My Visitors", app, "myVisitors", "list", 10, parentKey: "app.visitors", actions: [PermissionAction.View], resource: "Visitor"),
            Item("app.visitors.approvals", "Approvals", app, "walkInApprovals", "checkmark-done", 20, parentKey: "app.visitors", actions: [PermissionAction.View], resource: "Visitor"),
            Item("app.parcels", "Parcels", app, "parcels", "cube", 30, parentKey: "app.services", actions: [PermissionAction.View], resource: "Parcel"),
            Item("app.helpdesk", "Helpdesk", app, "tickets", "construct", 40, parentKey: "app.services", actions: [PermissionAction.View], resource: "Ticket"),
            Item("app.announcements", "Announcements", app, "announcements", "megaphone", 50, parentKey: "app.services", actions: [PermissionAction.View], resource: "Announcement"),
            Item("app.profile", "Profile", app, "profile", "person", 60),
        };
    }
}
