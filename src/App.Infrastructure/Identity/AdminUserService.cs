using App.Application.Auth;
using App.Application.Exceptions;
using App.Application.Identity;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Identity;

public sealed class AdminUserService(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    IPageSizePolicy pageSizePolicy) : IAdminUserService
{
    public async Task<PagedData<AdminUserDto>> ListAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var adminUserIds = await (
            from ur in db.UserRoles.AsNoTracking()
            join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where r.Surface == RoleSurface.Admin || r.Surface == RoleSurface.Both
            select ur.UserId).Distinct().ToListAsync(cancellationToken);

        var search = parameters.Search?.Trim();

        var usersQuery = db.Users.AsNoTracking().Where(u => adminUserIds.Contains(u.Id));
        if (!string.IsNullOrEmpty(search))
        {
            usersQuery = usersQuery.Where(u =>
                (u.Email != null && u.Email.Contains(search)) ||
                (u.Phone != null && u.Phone.Contains(search)));
        }

        var total = await usersQuery.CountAsync(cancellationToken);

        var page = parameters.Page is > 0 ? parameters.Page!.Value : 1;
        var pageSize = Math.Min(parameters.PageSize is > 0 ? parameters.PageSize!.Value : pageSizePolicy.DefaultPageSize, pageSizePolicy.MaxPageSize);

        var users = await usersQuery
            .OrderBy(u => u.Email ?? u.Phone)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var pageUserIds = users.Select(u => u.Id).ToList();

        var assignments = await (
            from ur in db.UserRoles.AsNoTracking()
            where pageUserIds.Contains(ur.UserId)
            join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            select new { ur.UserId, ur.RoleId, r.Name, ur.CommunityId }).ToListAsync(cancellationToken);

        var items = users.Select(u =>
        {
            var mine = assignments.Where(a => a.UserId == u.Id).ToList();
            return new AdminUserDto(
                u.Id,
                u.Name,
                u.Email,
                u.Phone,
                u.Status.ToString(),
                mine.Select(a => a.Name).Distinct().ToList(),
                mine.Select(a => new AdminUserRoleScopeDto(a.RoleId, a.Name, a.CommunityId)).ToList());
        }).ToList();

        return new PagedData<AdminUserDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
        };
    }

    public async Task<AdminUserDto> CreateAsync(CreateAdminUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var identifier = request.Identifier?.Trim() ?? string.Empty;
        if (identifier.Length == 0)
        {
            throw new ValidationException("identifier", "An email or phone is required.");
        }
        if (string.IsNullOrEmpty(request.Password))
        {
            throw new ValidationException("password", "A password is required.");
        }

        var roleName = request.RoleName?.Trim() ?? string.Empty;
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == roleName, cancellationToken)
            ?? throw new ValidationException("roleName", "Unknown role.");
        if (role.Surface != RoleSurface.Admin && role.Surface != RoleSurface.Both)
        {
            throw new ValidationException("roleName", "Only admin roles can be assigned here.");
        }

        var communityIds = await ResolveScopeCommunityIdsAsync(roleName, request.OrganizationId, request.CommunityId, cancellationToken);

        var isEmail = identifier.Contains('@', StringComparison.Ordinal);
        var user = new User
        {
            Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(),
            Email = isEmail ? identifier : null,
            Phone = isEmail ? null : identifier,
            PasswordHash = passwordHasher.Hash(request.Password),
            Status = UserStatus.Active,
        };
        db.Users.Add(user);

        if (communityIds.Count == 0)
        {
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, CommunityId = null });
        }
        else
        {
            foreach (var cid in communityIds.Distinct())
            {
                db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, CommunityId = cid });
            }
        }

        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken),
            "That email or phone is already in use by another login.");

        var assignments = communityIds.Count == 0
            ? new List<AdminUserRoleScopeDto> { new(role.Id, role.Name, null) }
            : communityIds.Distinct().Select(cid => new AdminUserRoleScopeDto(role.Id, role.Name, cid)).ToList();

        return new AdminUserDto(user.Id, user.Name, user.Email, user.Phone, user.Status.ToString(), [role.Name], assignments);
    }

    public async Task<AdminUserDto> UpdateAsync(Guid userId, UpdateAdminUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        var roleName = request.RoleName?.Trim() ?? string.Empty;
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == roleName, cancellationToken)
            ?? throw new ValidationException("roleName", "Unknown role.");
        if (role.Surface != RoleSurface.Admin && role.Surface != RoleSurface.Both)
        {
            throw new ValidationException("roleName", "Only admin roles can be assigned here.");
        }

        if (!Enum.TryParse<UserStatus>(request.Status?.Trim(), ignoreCase: true, out var status))
        {
            throw new ValidationException("status", "Unknown status.");
        }

        var communityIds = await ResolveScopeCommunityIdsAsync(roleName, request.OrganizationId, request.CommunityId, cancellationToken);

        user.Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        user.Status = status;
        if (!string.IsNullOrEmpty(request.Password))
        {
            user.PasswordHash = passwordHasher.Hash(request.Password);
        }

        var existing = await db.UserRoles.Where(ur => ur.UserId == userId).ToListAsync(cancellationToken);
        foreach (var ur in existing)
        {
            ur.IsDeleted = true;
        }
        await db.SaveChangesAsync(cancellationToken);

        if (communityIds.Count == 0)
        {
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, CommunityId = null });
        }
        else
        {
            foreach (var cid in communityIds.Distinct())
            {
                db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, CommunityId = cid });
            }
        }

        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken),
            "That role scope is already assigned.");

        var assignments = communityIds.Count == 0
            ? new List<AdminUserRoleScopeDto> { new(role.Id, role.Name, null) }
            : communityIds.Distinct().Select(cid => new AdminUserRoleScopeDto(role.Id, role.Name, cid)).ToList();

        return new AdminUserDto(user.Id, user.Name, user.Email, user.Phone, user.Status.ToString(), [role.Name], assignments);
    }

    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        var roleRows = await db.UserRoles.Where(ur => ur.UserId == userId).ToListAsync(cancellationToken);
        foreach (var ur in roleRows)
        {
            ur.IsDeleted = true;
        }
        user.IsDeleted = true;

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<Guid>> ResolveScopeCommunityIdsAsync(
        string roleName, Guid? organizationId, Guid? communityId, CancellationToken cancellationToken)
    {
        if (string.Equals(roleName, "PowerUser", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        if (string.Equals(roleName, "OrganizationAdmin", StringComparison.OrdinalIgnoreCase))
        {
            if (organizationId is not { } orgId)
            {
                throw new ValidationException("organizationId", "An organization is required for an organization admin.");
            }
            var ids = await db.Communities.IgnoreQueryFilters()
                .Where(c => c.OrganizationId == orgId && !c.IsDeleted)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);
            if (ids.Count == 0)
            {
                throw new ValidationException("organizationId", "The organization has no communities to scope to.");
            }
            return ids;
        }

        if (communityId is not { } cid)
        {
            throw new ValidationException("communityId", "A community is required for a community admin.");
        }
        return [cid];
    }
}
