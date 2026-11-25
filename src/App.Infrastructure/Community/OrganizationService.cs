using App.Application.Audit;
using App.Application.Community;
using App.Application.Config;
using App.Application.Exceptions;
using App.Application.Notifications;
using App.Application.Querying;
using App.Application.Subscription;
using App.Contracts.Envelope;
using App.Domain.Community;
using App.Domain.Identity;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Community;

public sealed class OrganizationService(
    AppDbContext db,
    IAuditService audit,
    IPageSizePolicy pageSizePolicy,
    IAsyncQueryExecutor executor,
    INotificationService notifications,
    IConfigService config,
    ISubscriptionService subscription,
    ILogger<OrganizationService> logger) : IOrganizationService
{
    private const string PortalUrlKey = "admin.portalUrl";
    private const string DefaultPortalUrl = "http://localhost:5173";

    // Allow-list: what a client may sort/search on (steering: no arbitrary client LINQ fields).
    private static readonly ListQueryConfiguration<Organization> QueryConfig =
        new ListQueryConfiguration<Organization>()
            .WithDefaultSort(o => o.Name)
            .Sortable("name", o => o.Name)
            .Sortable("propertyType", o => o.PropertyType)
            .Filterable("propertyType", o => o.PropertyType)
            .Searchable(o => o.Name);

    public async Task<OrganizationDto> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireName(request.Name);

        var org = new Organization
        {
            Name = request.Name.Trim(),
            PropertyType = string.IsNullOrWhiteSpace(request.PropertyType) ? null : request.PropertyType.Trim(),
        };
        db.Organizations.Add(org);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Create", "Organization", org.Id.ToString(), CommunityId: null, Detail: null),
            cancellationToken);

        return ToDto(org);
    }

    public async Task<OrganizationDto> UpdateAsync(Guid organizationId, UpdateOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken)
            ?? throw new NotFoundException("Organization not found.");

        if (request.Name is not null)
        {
            RequireName(request.Name);
            org.Name = request.Name.Trim();
        }

        if (request.PropertyType is not null)
        {
            org.PropertyType = string.IsNullOrWhiteSpace(request.PropertyType) ? null : request.PropertyType.Trim();
        }

        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Update", "Organization", org.Id.ToString(), CommunityId: null, Detail: null),
            cancellationToken);

        return ToDto(org);
    }

    public async Task DeleteAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken)
            ?? throw new NotFoundException("Organization not found.");

        // Remove is converted to a soft delete by the audit interceptor.
        db.Organizations.Remove(org);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Delete", "Organization", org.Id.ToString(), CommunityId: null, Detail: null),
            cancellationToken);
    }

    public async Task<OrganizationDto?> GetAsync(Guid organizationId, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        IQueryable<Organization> q = db.Organizations.AsNoTracking();
        if (includeArchived)
        {
            q = q.IgnoreQueryFilters(); // retrieve soft-deleted for audit
        }

        var org = await q.FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);
        return org is null ? null : ToDto(org);
    }

    public async Task<PagedData<OrganizationDto>> ListAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var paged = await db.Organizations.AsNoTracking().ToPagedResultAsync(
            parameters, QueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);

        var orgIds = paged.Items.Select(o => o.Id).ToList();

        if (orgIds.Count == 0)
            return paged;

        // 1. Community counts per organization
        var communityCounts = await db.Communities.AsNoTracking().IgnoreQueryFilters()
            .Where(c => orgIds.Contains(c.OrganizationId) && !c.IsDeleted)
            .GroupBy(c => c.OrganizationId)
            .Select(g => new { OrganizationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OrganizationId, x => x.Count, cancellationToken);

        // 2. Active admin check per organization
        var activeAdminOrgIds = (await (
            from c in db.Communities.AsNoTracking().IgnoreQueryFilters()
            where orgIds.Contains(c.OrganizationId) && !c.IsDeleted
            join u in db.Users.AsNoTracking().IgnoreQueryFilters() on c.Id equals u.CommunityId
            where !u.IsDeleted &&
                  u.Status == App.Domain.Identity.UserStatus.Active &&
                  u.RoleId != null
            select c.OrganizationId
        ).Distinct().ToListAsync(cancellationToken)).ToHashSet();

        // 3. Subscription/Expiry data per organization (run sequentially to avoid MARS connection conflicts)
        var expiryMap = new Dictionary<Guid, SubscriptionDto>();
        foreach (var id in orgIds)
        {
            expiryMap[id] = await subscription.BuildDtoAsync(id, cancellationToken);
        }

        return new PagedData<OrganizationDto>
        {
            Items = paged.Items.Select(o => o with
            {
                HasActiveAdmin = activeAdminOrgIds.Contains(o.Id),
                CommunityCount = communityCounts.GetValueOrDefault(o.Id, 0),
                ExpiryData = expiryMap.GetValueOrDefault(o.Id),
            }).ToList(),
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalCount = paged.TotalCount,
        };
    }

    public async Task<OrganizationDto> ActivateAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var org = await db.Organizations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken)
            ?? throw new NotFoundException("Organization not found.");

        string SocietyName = org.Name;
        if (org.IsDeleted)
        {
            org.IsDeleted = false;
            org.DeletedAtUtc = null;
        }

        var communities = await db.Communities.IgnoreQueryFilters()
            .Where(c => c.OrganizationId == organizationId && !c.IsDeleted)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(cancellationToken);

        var communityIds = communities.Select(c => c.Id).ToList();
        var activatedUsers = new List<User>();

        if (communityIds.Count > 0)
        {
            var adminUserIds = await (
                from u in db.Users.IgnoreQueryFilters()
                where !u.IsDeleted && u.CommunityId != null && communityIds.Contains(u.CommunityId.Value) && u.RoleId != null
                join r in db.Roles on u.RoleId!.Value equals r.Id
                where r.Name == "CommunityAdmin" || r.Name == "OrganizationAdmin"
                //where r.Surface == App.Domain.Identity.RoleSurface.Admin || r.Surface == App.Domain.Identity.RoleSurface.Both
                select u.Id).Distinct().ToListAsync(cancellationToken);

            if (adminUserIds.Count > 0)
            {
                var users = await db.Users.IgnoreQueryFilters()
                    .Where(u => adminUserIds.Contains(u.Id) && !u.IsDeleted && u.Status == App.Domain.Identity.UserStatus.Suspended)
                    .ToListAsync(cancellationToken);

                foreach (var user in users)
                {
                    user.Status = App.Domain.Identity.UserStatus.Active;
                    activatedUsers.Add(user);
                }
            }
        }

        // Activating the org also STARTS its subscription: a self-registered org is seeded with an
        // expired subscription (StartsAtUtc == ExpiresAtUtc == registration time) as the approval
        // gate. Without this, "expired" is still derived (ExpiresAtUtc <= now) after activation. Move
        // the window to now -> now + PlanMonths and mark it active so it reads active post-activation.
        var subscription = await db.OrgSubscriptions.IgnoreQueryFilters()
            .Where(s => s.OrganizationId == organizationId && !s.IsDeleted)
            .OrderByDescending(s => s.StartsAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (subscription is not null)
        {
            var nowUtc = DateTime.UtcNow;
            var months = App.Domain.Subscription.OrgSubscriptionPlans.IsValid(subscription.PlanMonths)
                ? subscription.PlanMonths
                : 12;
            subscription.StartsAtUtc = nowUtc;
            subscription.ExpiresAtUtc = nowUtc.AddMonths(months);
            subscription.Status = App.Domain.Subscription.OrgSubscriptionStatuses.Active;
        }

        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Activate", "Organization", org.Id.ToString(), CommunityId: null, Detail: null),
            cancellationToken);

        // Notify society admin(s) who registered or administer this organization
        await SendActivationNotificationsAsync(org, communities.Select(c => (c.Id, c.Name)).ToList(), activatedUsers, SocietyName, cancellationToken);

        return ToDto(org) with { HasActiveAdmin = true };
    }

    private async Task SendActivationNotificationsAsync(Organization org, List<(Guid Id, string Name)> communities, List<User> activatedUsers, string SocietyName, CancellationToken cancellationToken)
    {
        try
        {
            var targetUsers = new List<(User User, Guid CommunityId, string SocietyName)>();

            if (activatedUsers.Count > 0)
            {
                foreach (var user in activatedUsers)
                {
                    var cid = user.CommunityId ?? Guid.Empty;

                    var societyName = communities.FirstOrDefault(c => c.Id == cid).Name ?? org.Name;
                    var effectiveCid = cid != Guid.Empty ? cid : (communities.FirstOrDefault().Id != Guid.Empty ? communities.FirstOrDefault().Id : Guid.Empty);
                    targetUsers.Add((user, effectiveCid, societyName));
                }
            }
            else if (communities.Count > 0)
            {
                var communityIds = communities.Select(c => c.Id).ToList();
                var admins = await (
                    from u in db.Users.IgnoreQueryFilters()
                    where !u.IsDeleted && u.CommunityId != null && communityIds.Contains(u.CommunityId.Value) && u.RoleId != null
                    join r in db.Roles on u.RoleId!.Value equals r.Id
                    where r.Surface == App.Domain.Identity.RoleSurface.Admin || r.Surface == App.Domain.Identity.RoleSurface.Both
                    select new { User = u, CommunityId = u.CommunityId.Value }
                ).Distinct().ToListAsync(cancellationToken);

                foreach (var item in admins)
                {
                    var societyName = communities.FirstOrDefault(c => c.Id == item.CommunityId).Name ?? org.Name;
                    targetUsers.Add((item.User, item.CommunityId, societyName));
                }
            }

            foreach (var target in targetUsers)
            {
                if (target.CommunityId == Guid.Empty) continue;

                var portalUrl = (await config.GetStringAsync(PortalUrlKey, DefaultPortalUrl, target.CommunityId, cancellationToken)).TrimEnd('/');
                var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["name"] = string.IsNullOrWhiteSpace(target.User.Name) ? "there" : target.User.Name!,
                    ["societyName"] = SocietyName,
                    ["portalUrl"] = portalUrl,
                    ["loginId"] = target.User.Email ?? target.User.Phone ?? "the email/phone you registered with",
                };

                await notifications.EnqueueAsync(
                    new NotificationRequest(NotificationTemplateKeys.SocietyActivated, target.User.Id, target.CommunityId, placeholders),
                    cancellationToken);
            }

            if (targetUsers.Count > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to queue activation notifications for organization {OrganizationId}", org.Id);
        }
    }

    private static OrganizationDto ToDto(Organization o) => new(o.Id, o.Name, o.PropertyType, o.IsDeleted);

    private static void RequireName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("name", "An organization name is required.");
        }
    }
}

