using App.Domain.Identity;

namespace App.Application.Identity;


public sealed record CreateUserRequest(string? Email, string? Phone, string? PasswordHash = null, bool MfaEnabled = false);

public sealed record UpdateUserRequest(string? Email = null, string? Phone = null, UserStatus? Status = null, bool? MfaEnabled = null);

public sealed record UserDto(Guid Id, string? Email, string? Phone, UserStatus Status, bool MfaEnabled, bool IsDeleted);

public sealed record PermissionGrant(PermissionAction Action, string ResourceType);

public sealed record DefineRoleRequest(
    string Name,
    RoleScope Scope,
    Guid? CommunityId,
    IReadOnlyCollection<PermissionGrant> Permissions,
    RoleSurface Surface = RoleSurface.App);

public sealed record RoleDto(
    Guid Id,
    string Name,
    RoleScope Scope,
    Guid? CommunityId,
    IReadOnlyCollection<PermissionGrant> Permissions,
    bool IsDeleted);

public sealed record PermissionDto(Guid Id, string ResourceType, PermissionAction Action);

public sealed record EffectivePermission(string ResourceType, PermissionAction Action, Guid? CommunityId);
