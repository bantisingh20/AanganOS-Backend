using App.Application.Audit;
using App.Application.Community;
using App.Application.Exceptions;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Community;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Community;

/// <summary>
/// EF-backed <see cref="IOrganizationService"/> (Req 12.1). Create/edit/soft-delete/get/list the
/// platform-scoped organization. Mutations are audited (Req 6.1); delete archives the row (the
/// audit interceptor converts Remove into a soft delete) and it stays retrievable via
/// <c>IgnoreQueryFilters</c> for audit. The list goes through the shared pagination infra and only
/// the allow-listed fields may be sorted/searched (no arbitrary client fields).
/// </summary>
public sealed class OrganizationService(
    AppDbContext db,
    IAuditService audit,
    IPageSizePolicy pageSizePolicy,
    IAsyncQueryExecutor executor) : IOrganizationService
{
    // Allow-list: what a client may sort/search on (steering: no arbitrary client LINQ fields).
    private static readonly ListQueryConfiguration<Organization> QueryConfig =
        new ListQueryConfiguration<Organization>()
            .WithDefaultSort(o => o.Name)
            .Sortable("name", o => o.Name)
            .Searchable(o => o.Name);

    public async Task<OrganizationDto> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireName(request.Name);

        var org = new Organization { Name = request.Name.Trim() };
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

        // The global soft-delete filter excludes archived organizations from ordinary lists.
        var paged = await db.Organizations.AsNoTracking().ToPagedResultAsync(
            parameters, QueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);

        var orgIds = paged.Items.Select(o => o.Id).ToList();
        if (orgIds.Count == 0)
        {
            return paged;
        }

        // An org is "active" once at least one Active admin login is scoped to one of its
        // communities (the approval gate flips a registered org's admin from Suspended to Active).
        var activeOrgIds = await (
            from c in db.Communities.AsNoTracking().IgnoreQueryFilters()
            where orgIds.Contains(c.OrganizationId)
            join ur in db.UserRoles.AsNoTracking().IgnoreQueryFilters() on c.Id equals ur.CommunityId
            where !ur.IsDeleted
            join u in db.Users.AsNoTracking().IgnoreQueryFilters() on ur.UserId equals u.Id
            where !u.IsDeleted && u.Status == App.Domain.Identity.UserStatus.Active
            select c.OrganizationId).Distinct().ToListAsync(cancellationToken);

        var activeSet = activeOrgIds.ToHashSet();

        return new PagedData<OrganizationDto>
        {
            Items = paged.Items.Select(o => o with { HasActiveAdmin = activeSet.Contains(o.Id) }).ToList(),
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

        if (org.IsDeleted)
        {
            org.IsDeleted = false;
            org.DeletedAtUtc = null;
        }

        var communityIds = await db.Communities.IgnoreQueryFilters()
            .Where(c => c.OrganizationId == organizationId && !c.IsDeleted)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (communityIds.Count > 0)
        {
            var adminUserIds = await (
                from ur in db.UserRoles.IgnoreQueryFilters()
                where !ur.IsDeleted && ur.CommunityId != null && communityIds.Contains(ur.CommunityId.Value)
                join r in db.Roles on ur.RoleId equals r.Id
                where r.Surface == App.Domain.Identity.RoleSurface.Admin || r.Surface == App.Domain.Identity.RoleSurface.Both
                select ur.UserId).Distinct().ToListAsync(cancellationToken);

            if (adminUserIds.Count > 0)
            {
                var users = await db.Users.IgnoreQueryFilters()
                    .Where(u => adminUserIds.Contains(u.Id) && !u.IsDeleted && u.Status == App.Domain.Identity.UserStatus.Suspended)
                    .ToListAsync(cancellationToken);

                foreach (var user in users)
                {
                    user.Status = App.Domain.Identity.UserStatus.Active;
                }
            }
        }

        // Activating the org also STARTS its subscription: a self-registered org is seeded with an
        // expired subscription (StartsAtUtc == ExpiresAtUtc == registration time) as the approval
        // gate. Without this, "expired" is still derived (ExpiresAtUtc <= now) after activation. Move
        // the window to now → now + PlanMonths and mark it active so it reads active post-activation.
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

        return ToDto(org) with { HasActiveAdmin = true };
    }

    private static OrganizationDto ToDto(Organization o) => new(o.Id, o.Name, o.IsDeleted);

    private static void RequireName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("name", "An organization name is required.");
        }
    }
}
