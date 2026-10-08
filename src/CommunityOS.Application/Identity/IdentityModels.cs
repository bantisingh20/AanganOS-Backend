using CommunityOS.Domain.Identity;

namespace CommunityOS.Application.Identity;

// Transport-agnostic request/response models for the Identity/RBAC management services (Task 5.1).
// Controllers (Task 5.4) bind to these; the services never expose EF entities to callers. Password
// handling is Task 5.2 — these models carry no plaintext password (steering: never log/pass secrets
// where not needed). The one write that touches the hash column accepts an already-hashed value.

/// <summary>Attributes to create a <see cref="User"/> (Req 3.1).</summary>
/// <param name="Email">Login email, unique when present.</param>
/// <param name="Phone">Login phone, unique when present.</param>
/// <param name="PasswordHash">Pre-hashed password produced by Auth_Service (Task 5.2); null to create without a credential.</param>
/// <param name="MfaEnabled">Whether MFA is enabled.</param>
public sealed record CreateUserRequest(string? Email, string? Phone, string? PasswordHash = null, bool MfaEnabled = false);

/// <summary>Editable attributes of a <see cref="User"/> (Req 3.1). Null fields are left unchanged.</summary>
public sealed record UpdateUserRequest(string? Email = null, string? Phone = null, UserStatus? Status = null, bool? MfaEnabled = null);

/// <summary>Client-facing view of a user. Never includes the password hash.</summary>
public sealed record UserDto(Guid Id, string? Email, string? Phone, UserStatus Status, bool MfaEnabled, bool IsDeleted);

/// <summary>A permission expressed as an (Action, ResourceType) pair — the role-definition grain (Req 3.5).</summary>
public sealed record PermissionGrant(PermissionAction Action, string ResourceType);

/// <summary>Attributes to define a <see cref="Role"/> and its permission set (Req 3.5, 3.6).</summary>
/// <param name="Name">Role name, unique within scope (Req 3.6).</param>
/// <param name="Scope">Platform or community scope.</param>
/// <param name="CommunityId">Owning community for a community role; null for a platform role.</param>
/// <param name="Permissions">The permissions this role grants, each an (Action, ResourceType) (Req 3.5).</param>
public sealed record DefineRoleRequest(
    string Name,
    RoleScope Scope,
    Guid? CommunityId,
    IReadOnlyCollection<PermissionGrant> Permissions);

/// <summary>Client-facing view of a role and its granted permissions.</summary>
public sealed record RoleDto(
    Guid Id,
    string Name,
    RoleScope Scope,
    Guid? CommunityId,
    IReadOnlyCollection<PermissionGrant> Permissions,
    bool IsDeleted);

/// <summary>Client-facing view of a catalog permission.</summary>
public sealed record PermissionDto(Guid Id, string ResourceType, PermissionAction Action);

/// <summary>A single effective permission a user holds, scoped to the community it was granted in (Req 3.3, 4.1).</summary>
/// <param name="ResourceType">The resource type.</param>
/// <param name="Action">The action.</param>
/// <param name="CommunityId">The community the grant applies to, or null for a platform-wide grant.</param>
public sealed record EffectivePermission(string ResourceType, PermissionAction Action, Guid? CommunityId);
