using App.Application.Audit;
using App.Application.Exceptions;
using App.Application.Identity;
using App.Domain.Identity;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Identity;

public sealed class UserRoleService(AppDbContext db, IAuditService audit) : IUserRoleService
{
    public async Task<Guid> AssignAsync(Guid userId, Guid roleId, Guid? communityId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        if (!await db.Roles.AnyAsync(r => r.Id == roleId, cancellationToken))
        {
            throw new NotFoundException("Role not found.");
        }

        user.RoleId = roleId;
        user.CommunityId = communityId;

        if (communityId.HasValue && !user.OrganizationId.HasValue)
        {
            var orgId = await db.Communities
                .Where(c => c.Id == communityId.Value)
                .Select(c => (Guid?)c.OrganizationId)
                .FirstOrDefaultAsync(cancellationToken);
            if (orgId.HasValue)
            {
                user.OrganizationId = orgId.Value;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Assign", "UserRole", user.Id.ToString(), communityId, Detail: null),
            cancellationToken);

        return user.Id;
    }

    public async Task RevokeAsync(Guid userId, Guid roleId, Guid? communityId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        if (user.RoleId != roleId)
        {
            throw new NotFoundException("No active assignment found for this user/role combination.");
        }

        user.RoleId = null;
        user.CommunityId = null;
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Revoke", "UserRole", user.Id.ToString(), communityId, Detail: null),
            cancellationToken);
    }

    public async Task<IReadOnlyList<UserRoleDto>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null || !user.RoleId.HasValue || user.Role is null)
        {
            return Array.Empty<UserRoleDto>();
        }

        return new List<UserRoleDto>
        {
            new(user.Id, user.RoleId.Value, user.Role.Name, user.CommunityId)
        };
    }
}
