using App.Application.Abstractions.Security;
using App.Application.Abstractions;
using App.Application.Audit;
using App.Application.Config;
using App.Application.Exceptions;
using App.Application.Messaging;
using App.Application.Notifications;
using App.Application.Subscription;
using App.Domain.Subscription;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Subscription;

public sealed class SubscriptionService(
    AppDbContext db,
    IAuditService audit,
    IConfigService config,
    CurrentUser currentUser,
    IClock clock,
    IMessageSender messageSender,
    INotificationService notifications,
    ILogger<SubscriptionService> logger) : ISubscriptionService
{
    private const string ResourceType = "Subscription";
    private const string ApproverEmailsKey = "subscription.approverEmails";
    private const string WarningDaysKey = "subscription.warningDays";
    private const string PortalUrlKey = "admin.portalUrl";
    private const string DefaultPortalUrl = "http://localhost:5173";
    private const int DefaultWarningDays = 20;

    public async Task<SubscriptionDto> GetMineAsync(CancellationToken cancellationToken = default)
    {
        var organizationId = await ResolveCallerOrganizationIdAsync(cancellationToken)
            ?? throw new NotFoundException("No organization is associated with your account.");
        return await BuildDtoAsync(organizationId, cancellationToken);
    }

    public Task<SubscriptionDto> GetByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        BuildDtoAsync(organizationId, cancellationToken);

    public async Task<SubscriptionDto> SetExpiryAsync(
        Guid organizationId,
        int planMonths,
        DateTime startsAtUtc,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        await ApplyExpiryAsync(organizationId, planMonths, startsAtUtc, expiresAtUtc, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("SetExpiry", ResourceType, organizationId.ToString(), null,
                Detail: $"planMonths={planMonths};expiresAtUtc={expiresAtUtc:o}"),
            cancellationToken);

        await SendSubscriptionUpdatedNotificationAsync(organizationId, planMonths, expiresAtUtc, cancellationToken);

        return await BuildDtoAsync(organizationId, cancellationToken);
    }

    public async Task<SubscriptionDto> RequestRenewalAsync(int planMonths, CancellationToken cancellationToken = default)
    {
        if (!OrgSubscriptionPlans.IsValid(planMonths))
        {
            throw new ValidationException("planMonths", "The selected plan is not valid.");
        }

        var organizationId = await ResolveCallerOrganizationIdAsync(cancellationToken)
            ?? throw new NotFoundException("No organization is associated with your account.");

        var pendingExists = await db.SubscriptionRenewalRequests.IgnoreQueryFilters()
            .AnyAsync(r => r.OrganizationId == organizationId
                && r.Status == SubscriptionRenewalStatuses.Pending
                && !r.IsDeleted, cancellationToken);
        if (pendingExists)
        {
            throw new ConflictException("A renewal request is already pending for your organization.");
        }

        var requestedBy = (currentUser.UserId ?? Guid.Empty);
        var request = new SubscriptionRenewalRequest
        {
            OrganizationId = organizationId,
            RequestedPlanMonths = planMonths,
            Status = SubscriptionRenewalStatuses.Pending,
            RequestedBy = requestedBy,
            RequestedAtUtc = clock.UtcNow,
        };
        db.SubscriptionRenewalRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("RequestRenewal", ResourceType, request.Id.ToString(), null,
                Detail: $"organizationId={organizationId};planMonths={planMonths}"),
            cancellationToken);

        await NotifyApproversAsync(organizationId, planMonths, cancellationToken);

        return await BuildDtoAsync(organizationId, cancellationToken);
    }

    public async Task<IReadOnlyList<RenewalRequestDto>> ListPendingRenewalsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await (
            from r in db.SubscriptionRenewalRequests.AsNoTracking().IgnoreQueryFilters()
            where r.Status == SubscriptionRenewalStatuses.Pending && !r.IsDeleted
            join o in db.Organizations.AsNoTracking().IgnoreQueryFilters() on r.OrganizationId equals o.Id
            orderby r.RequestedAtUtc
            select new RenewalRequestDto(
                r.Id,
                r.OrganizationId,
                o.Name,
                r.RequestedPlanMonths,
                r.Status,
                r.RequestedAtUtc))
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<SubscriptionDto> ApproveRenewalAsync(
        Guid requestId,
        int planMonths,
        DateTime startsAtUtc,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        var request = await db.SubscriptionRenewalRequests.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == requestId && !r.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("Renewal request not found.");

        if (request.Status != SubscriptionRenewalStatuses.Pending)
        {
            throw new ConflictException("This renewal request has already been resolved.");
        }

        var resolvedBy = currentUser.UserId;
        request.Status = SubscriptionRenewalStatuses.Approved;
        request.ResolvedBy = resolvedBy;
        request.ResolvedAtUtc = clock.UtcNow;
        request.ResultExpiresAtUtc = expiresAtUtc;

        await ApplyExpiryAsync(request.OrganizationId, planMonths, startsAtUtc, expiresAtUtc, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("ApproveRenewal", ResourceType, requestId.ToString(), null,
                Detail: $"organizationId={request.OrganizationId};planMonths={planMonths};expiresAtUtc={expiresAtUtc:o}"),
            cancellationToken);

        await SendSubscriptionUpdatedNotificationAsync(request.OrganizationId, planMonths, expiresAtUtc, cancellationToken);

        return await BuildDtoAsync(request.OrganizationId, cancellationToken);
    }

    private async Task ApplyExpiryAsync(
        Guid organizationId,
        int planMonths,
        DateTime startsAtUtc,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken)
    {
        if (!OrgSubscriptionPlans.IsValid(planMonths))
        {
            throw new ValidationException("planMonths", "The selected plan is not valid.");
        }

        var status = expiresAtUtc > clock.UtcNow
            ? OrgSubscriptionStatuses.Active
            : OrgSubscriptionStatuses.Expired;

        var existing = await db.OrgSubscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId && !s.IsDeleted, cancellationToken);

        if (existing is null)
        {
            db.OrgSubscriptions.Add(new OrgSubscription
            {
                OrganizationId = organizationId,
                PlanMonths = planMonths,
                StartsAtUtc = startsAtUtc,
                ExpiresAtUtc = expiresAtUtc,
                Status = status,
            });
        }
        else
        {
            existing.PlanMonths = planMonths;
            existing.StartsAtUtc = startsAtUtc;
            existing.ExpiresAtUtc = expiresAtUtc;
            existing.Status = status;
        }
    }

    private async Task SendSubscriptionUpdatedNotificationAsync(
        Guid organizationId,
        int planMonths,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var org = await db.Organizations.AsNoTracking().IgnoreQueryFilters()
                .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);
            if (org is null) return;

            var communities = await db.Communities.AsNoTracking().IgnoreQueryFilters()
                .Where(c => c.OrganizationId == organizationId && !c.IsDeleted)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync(cancellationToken);

            var communityIds = communities.Select(c => c.Id).ToList();
            if (communityIds.Count == 0) return;

            var adminUsers = await (
                from u in db.Users.AsNoTracking().IgnoreQueryFilters()
                where !u.IsDeleted && u.CommunityId != null && communityIds.Contains(u.CommunityId.Value) && u.RoleId != null
                join r in db.Roles on u.RoleId!.Value equals r.Id
                where r.Surface == App.Domain.Identity.RoleSurface.Admin || r.Surface == App.Domain.Identity.RoleSurface.Both
                select new { u.Id, u.Name, u.Email, u.Phone, CommunityId = u.CommunityId.Value }
            ).Distinct().ToListAsync(cancellationToken);

            if (adminUsers.Count == 0) return;

            foreach (var admin in adminUsers)
            {
                var communityName = communities.FirstOrDefault(c => c.Id == admin.CommunityId)?.Name ?? org.Name;
                var portalUrl = (await config.GetStringAsync(PortalUrlKey, DefaultPortalUrl, admin.CommunityId, cancellationToken)).TrimEnd('/');

                var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["name"] = string.IsNullOrWhiteSpace(admin.Name) ? "there" : admin.Name,
                    ["societyName"] = communityName,
                    ["planMonths"] = planMonths.ToString(),
                    ["expiresAt"] = expiresAtUtc.ToString("dd MMM yyyy"),
                    ["portalUrl"] = portalUrl,
                };

                await notifications.EnqueueAsync(
                    new NotificationRequest(NotificationTemplateKeys.SubscriptionUpdated, admin.Id, admin.CommunityId, placeholders),
                    cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to queue subscription updated notification for organization {OrganizationId}", organizationId);
        }
    }

    private async Task NotifyApproversAsync(Guid organizationId, int planMonths, CancellationToken cancellationToken)
    {
        try
        {
            var recipients = await config.GetStringAsync(ApproverEmailsKey, string.Empty, cancellationToken: cancellationToken);
            var addresses = recipients
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (addresses.Count == 0)
            {
                return;
            }

            var orgName = await db.Organizations.AsNoTracking().IgnoreQueryFilters()
                .Where(o => o.Id == organizationId)
                .Select(o => o.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? "An organization";

            var subject = $"[AanganOS] {orgName} requested subscription renewal";
            var body = $"{orgName} has requested a subscription renewal for a {planMonths}-month plan. Please review it in the admin portal.";

            foreach (var address in addresses)
            {
                await messageSender.QueueEmailAsync(address, subject, body, cancellationToken);
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to queue subscription renewal notification for organization {OrganizationId}", organizationId);
        }
    }

    public async Task<SubscriptionDto> BuildDtoAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var sub = await db.OrgSubscriptions.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId && !s.IsDeleted, cancellationToken);

        var pending = await db.SubscriptionRenewalRequests.AsNoTracking().IgnoreQueryFilters()
            .Where(r => r.OrganizationId == organizationId
                && r.Status == SubscriptionRenewalStatuses.Pending
                && !r.IsDeleted)
            .OrderByDescending(r => r.RequestedAtUtc)
            .Select(r => (int?)r.RequestedPlanMonths)
            .FirstOrDefaultAsync(cancellationToken);

        var now = clock.UtcNow;

        if (sub is null)
        {
            return new SubscriptionDto(
                organizationId,
                0,
                null,
                null,
                OrgSubscriptionStatuses.Expired,
                0,
                true,
                false,
                false,
                pending is not null,
                pending);
        }

        var isExpired = sub.ExpiresAtUtc <= now;
        var daysRemaining = isExpired
            ? 0
            : (int)Math.Ceiling((sub.ExpiresAtUtc - now).TotalDays);
        var warningDays = await config.GetIntAsync(WarningDaysKey, DefaultWarningDays, cancellationToken: cancellationToken);
        var isExpiringSoon = !isExpired && daysRemaining <= warningDays;

        return new SubscriptionDto(
            organizationId,
            sub.PlanMonths,
            sub.StartsAtUtc,
            sub.ExpiresAtUtc,
            isExpired ? OrgSubscriptionStatuses.Expired : OrgSubscriptionStatuses.Active,
            daysRemaining,
            isExpired,
            isExpiringSoon,
            true,
            pending is not null,
            pending);
    }

    private async Task<Guid?> ResolveCallerOrganizationIdAsync(CancellationToken cancellationToken)
    {
        if (currentUser.OrganizationId is { } org)
        {
            return org;
        }

        if (currentUser.CommunityId is not { } first)
        {
            return null;
        }

        return await db.Communities.AsNoTracking().IgnoreQueryFilters()
            .Where(c => c.Id == first)
            .Select(c => (Guid?)c.OrganizationId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}

