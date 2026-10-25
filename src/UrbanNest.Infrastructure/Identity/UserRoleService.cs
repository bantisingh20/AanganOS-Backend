using UrbanNest.Application.Audit;
using UrbanNest.Application.Exceptions;
using UrbanNest.Application.Identity;
using UrbanNest.Domain.Identity;
using UrbanNest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace UrbanNest.Infrastructure.Identity;

/// <summary>
/// EF-backed <see cref="IUserRoleService"/> (Req 3.3, 3.4, 3.7). Assigns a role to a user scoped
/// to a community, or revokes the assignment.
///
/// <para><b>Assign (Req 3.3).</b> Creates a <see cref="UserRole"/> row. Duplicate active
/// assignment (same user, role, community scope) → conflict via the filtered unique index (no
/// read-then-write pre-check). Unknown user or role → not found.</para>
///
/// <para><b>Revoke (Req 3.4).</b> Soft-deletes the matching <c>UserRole</c> so the role's
/// permissions no longer apply: the Task 5.3 effective-permissions query only unions permissions
/// from existing (non-soft-deleted) UserRoles. The row is retained (Req 3.7) and traceable in
/// audit. Unknown assignment → not found.</para>
///
/// <para><b>Audit (Req 6.1).</b> Both operations append an audit entry.</para>
/// </summary>
public sealed class UserRoleService(AppDbContext db, IAuditService audit) : IUserRoleService
{
    public async Task<Guid> AssignAsync(Guid userId, Guid roleId, Guid? communityId, CancellationToken cancellationToken = default)
    {
        // Validate user and role exist (IgnoreQueryFilters is NOT needed — if they're soft-deleted
        // they shouldn't be assignable).
        if (!await db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
        {
            throw new NotFoundException("User not found.");
        }
        if (!await db.Roles.AnyAsync(r => r.Id == roleId, cancellationToken))
        {
            throw new NotFoundException("Role not found.");
        }

        var assignment = new UserRole
        {
            UserId = userId,
            RoleId = roleId,
            CommunityId = communityId,
        };
        db.UserRoles.Add(assignment);

        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken),
            "This role is already assigned to the user for the specified scope.");

        await audit.AppendAsync(
            new AuditEntry("Assign", "UserRole", assignment.Id.ToString(), communityId, Detail: null),
            cancellationToken);

        return assignment.Id;
    }

    public async Task RevokeAsync(Guid userId, Guid roleId, Guid? communityId, CancellationToken cancellationToken = default)
    {
        // Find the active (non-soft-deleted) assignment matching the triple.
        var assignment = await db.UserRoles
            .FirstOrDefaultAsync(ur =>
                ur.UserId == userId &&
                ur.RoleId == roleId &&
                ur.CommunityId == communityId, cancellationToken)
            ?? throw new NotFoundException("No active assignment found for this user/role/community combination.");

        // Remove is converted to a soft delete by the audit interceptor (Req 3.7). The row stays
        // in the table but is excluded from the soft-delete query filter, so the effective-permissions
        // query no longer sees it → permissions drop (Req 3.4).
        db.UserRoles.Remove(assignment);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Revoke", "UserRole", assignment.Id.ToString(), communityId, Detail: null),
            cancellationToken);
    }

    public async Task<IReadOnlyList<UserRoleDto>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Active assignments only: the soft-delete query filter already excludes revoked rows and
        // soft-deleted roles, so a revoke drops the role here too (Req 3.4). Single joined,
        // AsNoTracking read projecting just what the client sees — no N+1, no entity exposure.
        return await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(
                db.Roles,
                ur => ur.RoleId,
                r => r.Id,
                (ur, r) => new UserRoleDto(ur.Id, r.Id, r.Name, ur.CommunityId))
            .ToListAsync(cancellationToken);
    }
}
