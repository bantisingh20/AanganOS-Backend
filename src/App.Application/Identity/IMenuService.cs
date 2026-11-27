using App.Contracts.Envelope;
using App.Domain.Identity;

namespace App.Application.Identity;

/// <summary>Sidebar / tile node. PermissionResource + PermissionActions let the clients resolve a page's rights
/// from its route (the DB menu row) instead of hard-coding permission keys in each page.</summary>
public sealed record MenuNodeDto(
    string Key,
    string Label,
    string? Icon,
    string? Route,
    IReadOnlyList<MenuNodeDto> Children,
    string? PermissionResource = null,
    IReadOnlyList<int>? PermissionActions = null);

public sealed record MenuItemDto(
    Guid Id,
    Guid? ParentId,
    string Key,
    string Label,
    string? Icon,
    string? Route,
    MenuSurface Surface,
    IReadOnlyList<int> RequiredPermissionActions,
    string? RequiredPermissionResource,
    int SortOrder,
    bool IsActive,
    bool ShowInSidebar);

public sealed record CreateMenuItemRequest(
    Guid? ParentId,
    string Key,
    string Label,
    string? Icon,
    string? Route,
    MenuSurface Surface,
    IReadOnlyList<int>? RequiredPermissionActions,
    string? RequiredPermissionResource,
    int SortOrder,
    bool IsActive,
    bool ShowInSidebar);

public sealed record UpdateMenuItemRequest(
    Guid? ParentId,
    string Label,
    string? Icon,
    string? Route,
    MenuSurface Surface,
    IReadOnlyList<int>? RequiredPermissionActions,
    string? RequiredPermissionResource,
    int SortOrder,
    bool IsActive,
    bool ShowInSidebar);

public interface IMenuService
{
    Task<IReadOnlyList<MenuNodeDto>> GetMenuAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MenuItemDto>> ListAllAsync(MenuSurface? surface, CancellationToken cancellationToken = default);

    Task<MenuItemDto> CreateAsync(CreateMenuItemRequest request, CancellationToken cancellationToken = default);

    Task<MenuItemDto> UpdateAsync(Guid id, UpdateMenuItemRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
