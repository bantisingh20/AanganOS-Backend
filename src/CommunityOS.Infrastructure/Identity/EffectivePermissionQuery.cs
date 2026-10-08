using CommunityOS.Application.Identity;
using CommunityOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommunityOS.Infrastructure.Identity;

/// <summary>
/// EF-backed <see cref="IEffectivePermissionQuery"/> (Req 4.1, 3.4). Returns the union of
/// Role→Permission across a user's active <c>UserRole</c> assignments — the data the Task 5.3
/// evaluator checks a request's required (Action, resourceType) against.
///
/// <para><b>Single joined query, no N+1 (steering: scalable reads).</b> Starting from
/// <c>UserRoles</c>, it joins through <c>RolePermissions</c> to <c>Permissions</c> in one
/// <c>AsNoTracking</c> LINQ query pushed to SQL, projecting straight to the result shape. Nothing
/// is iterated per role in application code.</para>
///
/// <para><b>Only active grants count (Req 3.4).</b> The global soft-delete query filter already
/// excludes a soft-deleted (revoked) <c>UserRole</c>, a soft-deleted <c>Role</c> and a
/// soft-deleted <c>User</c>, so removing a role immediately drops its permissions from this result.
/// The <see cref="IEffectivePermissionQuery"/> contract is honoured: when
/// <paramref name="communityIds"/> is non-empty only assignments scoped to one of those
/// communities (or platform-wide assignments, which apply everywhere) are considered; the result
/// is de-duplicated across roles.</para>
/// </summary>
public sealed class EffectivePermissionQuery(AppDbContext db) : IEffectivePermissionQuery
{
    public async Task<IReadOnlyList<EffectivePermission>> GetEffectivePermissionsAsync(
        Guid userId,
        IReadOnlyCollection<Guid>? communityIds = null,
        CancellationToken cancellationToken = default)
    {
        // One join from the user's assignments → role → granted permissions. The soft-delete global
        // filter on UserRole/Role (and the FK to a live User) keeps this to active grants only.
        var query =
            from ur in db.UserRoles.AsNoTracking()
            where ur.UserId == userId
            join rp in db.RolePermissions.AsNoTracking() on ur.RoleId equals rp.RoleId
            join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            select new { p.ResourceType, p.Action, ur.CommunityId };

        if (communityIds is { Count: > 0 })
        {
            // Scoped assignments must match one of the requested communities; a platform-wide
            // assignment (null CommunityId) applies everywhere.
            query = query.Where(x => x.CommunityId == null || communityIds.Contains(x.CommunityId.Value));
        }

        var rows = await query
            .Distinct()
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new EffectivePermission(x.ResourceType, x.Action, x.CommunityId))
            .ToList();
    }
}
