namespace App.Application.Identity;

/// <summary>
/// The data/query the Task 5.3 permission evaluator builds on (Req 4.1, 3.4). Given a user id and
/// the communities a request is authorized for, it returns the <b>union of Role→Permission across
/// the user's active UserRoles</b> — exactly the effective permissions that must be checked against
/// a request's required (Action, resourceType). Task 5.3 wires this into
/// <c>IPermissionEvaluator</c>; this task provides only the query.
///
/// <para>Only active assignments count: a revoked (soft-deleted) UserRole, a soft-deleted role, or
/// a soft-deleted user contributes nothing, so removing a role immediately drops its permissions
/// (Req 3.4). The query is a single joined, <c>AsNoTracking</c> database query — no N+1, no
/// in-memory filtering (steering: scalable reads).</para>
/// </summary>
public interface IEffectivePermissionQuery
{
    /// <summary>
    /// Effective permissions for <paramref name="userId"/>. When <paramref name="communityIds"/> is
    /// non-empty, only assignments scoped to one of those communities (or platform-wide
    /// assignments, which apply everywhere) are considered; when empty, all of the user's active
    /// assignments are returned. The result is de-duplicated across roles.
    /// </summary>
    Task<IReadOnlyList<EffectivePermission>> GetEffectivePermissionsAsync(
        Guid userId,
        IReadOnlyCollection<Guid>? communityIds = null,
        CancellationToken cancellationToken = default);
}
