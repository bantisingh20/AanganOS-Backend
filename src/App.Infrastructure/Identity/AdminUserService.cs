using App.Application.Auth;
using App.Application.Config;
using App.Application.Exceptions;
using App.Application.Identity;
using App.Application.Notifications;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Identity;

public sealed class AdminUserService(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    IPageSizePolicy pageSizePolicy,
    INotificationService notifications,
    IConfigService config,
    ILogger<AdminUserService> logger) : IAdminUserService
{
    private const string PortalUrlKey = "admin.portalUrl";
    private const string DefaultPortalUrl = "http://localhost:5173";

    public async Task<PagedData<AdminUserDto>> ListAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var search = parameters.Search?.Trim();

        var usersQuery = db.Users.AsNoTracking()
            .Include(u => u.Role)
            .Where(u => !db.Residents.Any(r => r.UserId == u.Id))
            .Where(u => u.RoleId != null && (u.Role!.Surface == RoleSurface.Admin || u.Role.Surface == RoleSurface.Both));

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

        var items = users.Select(u =>
        {
            var roleName = u.Role?.Name ?? string.Empty;
            var roleNames = string.IsNullOrEmpty(roleName) ? new List<string>() : new List<string> { roleName };
            var assignments = u.RoleId.HasValue && !string.IsNullOrEmpty(roleName)
                ? new List<AdminUserRoleScopeDto> { new(u.RoleId.Value, roleName, u.CommunityId) }
                : new List<AdminUserRoleScopeDto>();

            return new AdminUserDto(
                u.Id,
                u.Name,
                u.Email,
                u.Phone,
                u.Status.ToString(),
                roleNames,
                assignments);
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
        var targetCommunityId = communityIds.Count > 0 ? communityIds[0] : (Guid?)null;

        var isEmail = identifier.Contains('@', StringComparison.Ordinal);
        var user = new User
        {
            Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(),
            Email = isEmail ? identifier : null,
            Phone = isEmail ? null : identifier,
            PasswordHash = passwordHasher.Hash(request.Password),
            Status = UserStatus.Active,
            RoleId = role.Id,
            CommunityId = targetCommunityId,
            OrganizationId = request.OrganizationId,
        };
        db.Users.Add(user);

        if (targetCommunityId.HasValue)
        {
            db.Set<App.Domain.Residents.Resident>().Add(new App.Domain.Residents.Resident
            {
                CommunityId = targetCommunityId.Value,
                OrganizationId = request.OrganizationId,
                UserId = user.Id,
                Name = user.Name ?? identifier,
                Email = user.Email,
                Phone = user.Phone,
                ResidentType = "owner",
                VerificationStatus = App.Domain.Residents.ResidentVerificationStatuses.Verified,
            });
        }

        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken),
            "That email or phone is already in use by another login.");

        var assignments = new List<AdminUserRoleScopeDto>
        {
            new(role.Id, role.Name, targetCommunityId)
        };

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

        if (!Enum.TryParse<UserStatus>(request.Status?.Trim(), ignoreCase: true, out var status))
        {
            throw new ValidationException("status", "Unknown status.");
        }

        var communityIds = await ResolveScopeCommunityIdsAsync(roleName, request.OrganizationId, request.CommunityId, cancellationToken);
        var targetCommunityId = communityIds.Count > 0 ? communityIds[0] : (Guid?)null;

        var previousStatus = user.Status;
        user.Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        user.Status = status;
        user.RoleId = role.Id;
        user.CommunityId = targetCommunityId;
        if (request.OrganizationId.HasValue)
        {
            user.OrganizationId = request.OrganizationId;
        }

        if (!string.IsNullOrEmpty(request.Password))
        {
            user.PasswordHash = passwordHasher.Hash(request.Password);
        }

        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken),
            "That role scope is already assigned.");

        if (previousStatus == UserStatus.Suspended && status == UserStatus.Active)
        {
            await SendActivationNotificationAsync(user, communityIds, cancellationToken);
        }

        var assignments = new List<AdminUserRoleScopeDto>
        {
            new(role.Id, role.Name, targetCommunityId)
        };

        return new AdminUserDto(user.Id, user.Name, user.Email, user.Phone, user.Status.ToString(), [role.Name], assignments);
    }

    private async Task SendActivationNotificationAsync(User user, List<Guid> communityIds, CancellationToken cancellationToken)
    {
        try
        {
            var scopeCommunityId = communityIds.Count > 0 ? communityIds[0] : (Guid?)null;
            if (scopeCommunityId is not { } communityId)
            {
                return;
            }

            var societyName = await db.Communities.AsNoTracking().IgnoreQueryFilters()
                .Where(c => c.Id == communityId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);
            var portalUrl = (await config.GetStringAsync(PortalUrlKey, DefaultPortalUrl, communityId, cancellationToken)).TrimEnd('/');

            var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = string.IsNullOrWhiteSpace(user.Name) ? "there" : user.Name!,
                ["societyName"] = string.IsNullOrWhiteSpace(societyName) ? "your society" : societyName!,
                ["portalUrl"] = portalUrl,
                ["loginId"] = user.Email ?? user.Phone ?? "the email/phone you registered with",
            };

            await notifications.EnqueueAsync(
                new NotificationRequest(NotificationTemplateKeys.SocietyActivated, user.Id, communityId, placeholders),
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to queue activation notification for admin user {UserId}", user.Id);
        }
    }

    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("User not found.");

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

        if (string.Equals(roleName, "Superadmin", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        if (string.Equals(roleName, "OrganizationAdmin", StringComparison.OrdinalIgnoreCase))
        {
            if (organizationId is not { } orgId)
            {
                throw new ValidationException("organizationId", "An organization is required for an organization admin.");
            }
            if (communityId.HasValue && communityId.Value != Guid.Empty)
            {
                return [communityId.Value];
            }
            var firstId = await db.Communities.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.OrganizationId == orgId && !c.IsDeleted)
                .Select(c => c.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (firstId == Guid.Empty)
            {
                throw new ValidationException("organizationId", "The organization has no communities to scope to.");
            }
            return [firstId];
        }

        if (communityId is not { } cid)
        {
            throw new ValidationException("communityId", "A community is required for a community admin.");
        }
        return [cid];
    }
}
