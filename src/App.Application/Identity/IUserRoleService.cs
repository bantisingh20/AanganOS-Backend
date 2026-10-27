namespace App.Application.Identity;

/// <summary>
/// Role assignment management (Req 3.3, 3.4). Assigning a role to a user creates a
/// <c>UserRole</c> scoped to a community (Req 3.3); removing it revokes the assignment so the
/// role's permissions no longer apply (Req 3.4). Both are audited (Req 6.1). A revoke is a
/// soft-delete so the assignment history is retained (Req 3.7).
/// </summary>
public interface IUserRoleService
{
    /// <summary>
    /// Assign <paramref name="roleId"/> to <paramref name="userId"/>, scoped to
    /// <paramref name="communityId"/> (null = platform-wide) (Req 3.3). A duplicate active
    /// assignment → conflict; unknown user/role → not found.
    /// </summary>
    Task<Guid> AssignAsync(Guid userId, Guid roleId, Guid? communityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke the matching assignment so the role's permissions no longer apply to the user
    /// (Req 3.4). Soft-deletes the <c>UserRole</c> row (retained for audit, Req 3.7). Unknown
    /// assignment → not found.
    /// </summary>
    Task RevokeAsync(Guid userId, Guid roleId, Guid? communityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List a user's active (non-revoked) role assignments (Req 3.3). Each row carries the assigned
    /// role (id + name) and the community the assignment is scoped to (null = platform-wide). A
    /// single joined <c>AsNoTracking</c> read — no N+1.
    /// </summary>
    Task<IReadOnlyList<UserRoleDto>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Client-facing view of one of a user's role assignments (Req 3.3).</summary>
/// <param name="UserRoleId">The assignment id.</param>
/// <param name="RoleId">The assigned role's id.</param>
/// <param name="RoleName">The assigned role's name.</param>
/// <param name="CommunityId">The community the assignment is scoped to, or null for platform-wide.</param>
public sealed record UserRoleDto(Guid UserRoleId, Guid RoleId, string RoleName, Guid? CommunityId);
