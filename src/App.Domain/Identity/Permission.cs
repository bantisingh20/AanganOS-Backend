using App.Domain.Common;

namespace App.Domain.Identity;

/// <summary>
/// A single authorization to perform one <see cref="PermissionAction"/> on one
/// <see cref="ResourceType"/> (Req 3.2). The platform seeds the known catalog of
/// (ResourceType, Action) pairs as editable data; a <see cref="Role"/> grants a set of them via
/// <see cref="RolePermission"/>, and the Task 5.3 evaluator checks a request's required
/// (Action, resourceType) against the union a user's roles confer.
///
/// <para>Platform-scoped (not community-owned): the permission catalog is a platform-wide
/// vocabulary, identical for every community — scoping of <i>who</i> holds a permission is on the
/// UserRole, not on the permission definition. Unique(ResourceType, Action) (Req 3.2) prevents a
/// duplicate catalog entry.</para>
/// </summary>
public class Permission : PlatformEntity
{
    /// <summary>The resource type the action targets, e.g. <c>User</c>, <c>Role</c>, <c>Configuration</c> (Req 3.2). Required.</summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>The granular verb conferred (Req 3.2).</summary>
    public PermissionAction Action { get; set; }
}
