using App.Application.Abstractions;
using App.Application.Exceptions;
using App.Application.Identity;
using App.Domain.Identity;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Identity;

public sealed class MenuService(
    AppDbContext db,
    ITenantContext tenant,
    IClock clock,
    IEffectivePermissionQuery effectivePermissions) : IMenuService
{
    private static IReadOnlyList<int> ParseActions(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var v) ? v : -1)
                .Where(v => v >= 0)
                .Distinct()
                .ToList();

    private static string? FormatActions(IReadOnlyList<int>? actions) =>
        actions is { Count: > 0 } ? string.Join(',', actions.Distinct()) : null;

    private static MenuItemDto ToDto(MenuItem m) => new(
        m.Id, m.ParentId, m.Key, m.Label, m.Icon, m.Route, m.Surface,
        ParseActions(m.RequiredPermissionActions), m.RequiredPermissionResource, m.SortOrder, m.IsActive, m.ShowInSidebar);

    public async Task<IReadOnlyList<MenuItemDto>> ListAllAsync(MenuSurface? surface, CancellationToken cancellationToken = default)
    {
        var query = db.MenuItems.AsNoTracking();
        if (surface is { } s)
        {
            query = query.Where(m => m.Surface == s);
        }
        var items = await query
            .OrderBy(m => m.Surface)
            .ThenBy(m => m.SortOrder)
            .ToListAsync(cancellationToken);
        return items.Select(ToDto).ToList();
    }

    public async Task<MenuItemDto> CreateAsync(CreateMenuItemRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = request.Key?.Trim() ?? string.Empty;
        if (key.Length == 0)
        {
            throw new ValidationException("key", "Key is required.");
        }
        if (string.IsNullOrWhiteSpace(request.Label))
        {
            throw new ValidationException("label", "Label is required.");
        }
        if (await db.MenuItems.AnyAsync(m => m.Key == key, cancellationToken))
        {
            throw new ConflictException("A menu item with that key already exists.");
        }

        var item = new MenuItem
        {
            ParentId = request.ParentId,
            Key = key,
            Label = request.Label.Trim(),
            Icon = request.Icon?.Trim(),
            Route = string.IsNullOrWhiteSpace(request.Route) ? null : request.Route.Trim(),
            Surface = request.Surface,
            RequiredPermissionActions = FormatActions(request.RequiredPermissionActions),
            RequiredPermissionResource = string.IsNullOrWhiteSpace(request.RequiredPermissionResource) ? null : request.RequiredPermissionResource.Trim(),
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
            ShowInSidebar = request.ShowInSidebar,
            CreatedAtUtc = clock.UtcNow,
        };
        db.MenuItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(item);
    }

    public async Task<MenuItemDto> UpdateAsync(Guid id, UpdateMenuItemRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var item = await db.MenuItems.FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
            ?? throw new NotFoundException("Menu item not found.");

        if (string.IsNullOrWhiteSpace(request.Label))
        {
            throw new ValidationException("label", "Label is required.");
        }
        if (request.ParentId == id)
        {
            throw new ValidationException("parentId", "A menu item cannot be its own parent.");
        }

        item.ParentId = request.ParentId;
        item.Label = request.Label.Trim();
        item.Icon = request.Icon?.Trim();
        item.Route = string.IsNullOrWhiteSpace(request.Route) ? null : request.Route.Trim();
        item.Surface = request.Surface;
        item.RequiredPermissionActions = FormatActions(request.RequiredPermissionActions);
        item.RequiredPermissionResource = string.IsNullOrWhiteSpace(request.RequiredPermissionResource) ? null : request.RequiredPermissionResource.Trim();
        item.SortOrder = request.SortOrder;
        item.IsActive = request.IsActive;
        item.ShowInSidebar = request.ShowInSidebar;
        item.UpdatedAtUtc = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(item);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await db.MenuItems.FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
            ?? throw new NotFoundException("Menu item not found.");

        var hasChildren = await db.MenuItems.AnyAsync(m => m.ParentId == id, cancellationToken);
        if (hasChildren)
        {
            throw new ValidationException("id", "Remove or reassign child items before deleting this menu item.");
        }

        item.IsDeleted = true;
        item.DeletedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MenuNodeDto>> GetMenuAsync(CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(tenant.UserId, out var userId))
        {
            return [];
        }

        var isSuperAdmin = tenant.IsPlatformSuperAdmin;

        var surfaces = isSuperAdmin
            ? new HashSet<MenuSurface> { MenuSurface.Admin }
            : await ResolveSurfacesAsync(userId, cancellationToken);

        if (surfaces.Count == 0)
        {
            return [];
        }

        var items = await db.MenuItems
            .AsNoTracking()
            .Where(m => m.IsActive && m.ShowInSidebar && surfaces.Contains(m.Surface))
            .OrderBy(m => m.SortOrder)
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
        {
            return [];
        }

        var grantedActions = isSuperAdmin
            ? null
            : (await effectivePermissions.GetEffectivePermissionsAsync(userId, tenant.AuthorizedCommunityIds, cancellationToken))
                .Select(p => p.Action)
                .ToHashSet();

        bool HasPermission(MenuItem m)
        {
            var actions = ParseActions(m.RequiredPermissionActions);
            if (actions.Count == 0)
            {
                return true;
            }
            if (grantedActions is null)
            {
                return true;
            }
            return actions.Any(a => grantedActions.Contains((PermissionAction)a));
        }

        var byParent = items.ToLookup(m => m.ParentId);

        IReadOnlyList<MenuNodeDto> Build(Guid? parentId)
        {
            var nodes = new List<MenuNodeDto>();
            foreach (var m in byParent[parentId])
            {
                var children = Build(m.Id);
                var selfVisible = !string.IsNullOrEmpty(m.Route) && HasPermission(m);
                if (!selfVisible && children.Count == 0)
                {
                    continue;
                }
                nodes.Add(new MenuNodeDto(m.Key, m.Label, m.Icon, m.Route, children));
            }
            return nodes;
        }

        return Build(null);
    }

    private async Task<HashSet<MenuSurface>> ResolveSurfacesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var roleSurfaces = await (
            from ur in db.UserRoles.AsNoTracking()
            where ur.UserId == userId
            join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            select r.Surface).Distinct().ToListAsync(cancellationToken);

        var surfaces = new HashSet<MenuSurface>();
        foreach (var rs in roleSurfaces)
        {
            if (rs is RoleSurface.App or RoleSurface.Both)
            {
                surfaces.Add(MenuSurface.App);
            }
            if (rs is RoleSurface.Admin or RoleSurface.Both)
            {
                surfaces.Add(MenuSurface.Admin);
            }
        }
        return surfaces;
    }
}
