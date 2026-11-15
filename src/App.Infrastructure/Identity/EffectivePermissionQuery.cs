using App.Application.Identity;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Identity;

public sealed class EffectivePermissionQuery(AppDbContext db) : IEffectivePermissionQuery
{
    public async Task<IReadOnlyList<EffectivePermission>> GetEffectivePermissionsAsync(
        Guid userId,
        IReadOnlyCollection<Guid>? communityIds = null,
        CancellationToken cancellationToken = default)
    {
        var query =
            from u in db.Users.AsNoTracking()
            where u.Id == userId && u.RoleId != null
            join rp in db.RolePermissions.AsNoTracking() on u.RoleId!.Value equals rp.RoleId
            join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            select new { p.ResourceType, p.Action, u.CommunityId };

        if (communityIds is { Count: > 0 })
        {
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
