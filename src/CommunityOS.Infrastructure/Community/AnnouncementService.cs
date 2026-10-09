using CommunityOS.Application.Abstractions;
using CommunityOS.Application.Abstractions.Security;
using CommunityOS.Application.Audit;
using CommunityOS.Application.Community;
using CommunityOS.Application.Config;
using CommunityOS.Application.Exceptions;
using CommunityOS.Application.Querying;
using CommunityOS.Contracts.Envelope;
using CommunityOS.Domain.Identity;
using CommunityOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using DomainAnnouncement = CommunityOS.Domain.Community.Announcement;

namespace CommunityOS.Infrastructure.Community;

/// <summary>
/// EF-backed <see cref="IAnnouncementService"/>: the community notice board. Residents READ the
/// board (tenant-scoped by the global query filter; only ACTIVE, non-soft-deleted notices), while an
/// admin (platform super-admin or a role granted Create/Edit/Delete on <c>Announcement</c>) manages
/// it — publish, edit, activate/deactivate (reversible board visibility) and soft-delete. Writes
/// load TRACKED so the inherited <c>RowVersion</c> rides edits (a concurrency loser → CONFLICT
/// centrally), validate the category against the configurable <c>Announcement_Category</c> list, and
/// append an <see cref="AuditEntry"/>. Mirrors the create/edit/delete shape of <c>AmenityService</c>.
/// </summary>
public sealed class AnnouncementService(
    AppDbContext db,
    IAuditService audit,
    IConfigService config,
    IClock clock,
    IAuthorizationGuard authorizationGuard,
    IPageSizePolicy pageSizePolicy,
    IAsyncQueryExecutor executor)
    : IAnnouncementService
{
    // Allow-list for the announcement list endpoint. Only these fields reach the query — no arbitrary
    // client LINQ (steering: no hardcoding, injection-safe). Default is newest-first. Mirrors
    // AmenityQueryConfig.
    private static readonly ListQueryConfiguration<DomainAnnouncement> AnnouncementQueryConfig =
        new ListQueryConfiguration<DomainAnnouncement>()
            .WithDefaultSort(a => a.PublishedAtUtc, SortDirection.Descending)
            .Sortable("publishedAtUtc", a => a.PublishedAtUtc)
            .Sortable("title", a => a.Title)
            .Sortable("category", a => a.Category)
            .Filterable("category", a => a.Category)
            .Searchable(a => a.Title)
            .Searchable(a => a.Body);

    public async Task<PagedData<AnnouncementDto>> ListAnnouncementsAsync(
        ListQueryParameters parameters, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // Defense-in-depth: listing the board requires View on Announcement. The endpoint gate
        // declares the same; the invariant holds regardless of caller.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "Announcement", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view announcements.");
        }

        // Tenant-scoped by the global query filter (soft-deleted rows hidden). The resident board
        // sees only ACTIVE notices; an admin management view includes inactive so they can be
        // re-activated/edited. The allow-listed query config fixes the sort/filter/search surface.
        IQueryable<DomainAnnouncement> q = db.Announcements.AsNoTracking();
        if (!includeInactive)
        {
            q = q.Where(a => a.IsActive);
        }
        return await q.ToPagedResultAsync(
            parameters, AnnouncementQueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
    }

    public async Task<AnnouncementDto?> GetAnnouncementAsync(Guid announcementId, CancellationToken cancellationToken = default)
    {
        // Tenant-scoped AsNoTracking read: an unknown/foreign/soft-deleted id is hidden by the global
        // filter → null.
        var announcement = await db.Announcements.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == announcementId, cancellationToken);
        return announcement is null ? null : ToDto(announcement);
    }

    public async Task<AnnouncementDto> CreateAnnouncementAsync(CreateAnnouncementRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth: publishing requires Create on Announcement (superadmin bypasses).
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "Announcement", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to publish announcements.");
        }
        authorizationGuard.EnsureCommunityAuthorized(request.CommunityId);

        var title = RequireText(request.Title, "title", "A title is required.");
        var body = RequireText(request.Body, "body", "A message body is required.");
        var category = await RequireValidCategoryAsync(request.Category, request.CommunityId, cancellationToken);

        var announcement = new DomainAnnouncement
        {
            CommunityId = request.CommunityId,
            Title = title,
            Body = body,
            Category = category,
            PublishedAtUtc = clock.UtcNow,
            IsActive = true,
        };

        db.Announcements.Add(announcement);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Create", "Announcement", announcement.Id.ToString(), announcement.CommunityId, Detail: null),
            cancellationToken);

        return ToDto(announcement);
    }

    public async Task<AnnouncementDto> UpdateAnnouncementAsync(Guid announcementId, UpdateAnnouncementRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "Announcement", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to edit announcements.");
        }

        var announcement = await db.Announcements.FirstOrDefaultAsync(a => a.Id == announcementId, cancellationToken)
            ?? throw new NotFoundException("Announcement not found.");
        authorizationGuard.EnsureCommunityAuthorized(announcement.CommunityId);

        if (request.Title is not null)
        {
            announcement.Title = RequireText(request.Title, "title", "A title is required.");
        }
        if (request.Body is not null)
        {
            announcement.Body = RequireText(request.Body, "body", "A message body is required.");
        }
        if (request.Category is not null)
        {
            announcement.Category = await RequireValidCategoryAsync(request.Category, announcement.CommunityId, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Update", "Announcement", announcement.Id.ToString(), announcement.CommunityId, Detail: null),
            cancellationToken);

        return ToDto(announcement);
    }

    public async Task<AnnouncementDto> SetAnnouncementActiveAsync(Guid announcementId, bool isActive, CancellationToken cancellationToken = default)
    {
        // Visibility toggle is an edit of the notice (reuses the Edit grant; no separate action).
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "Announcement", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to change announcements.");
        }

        var announcement = await db.Announcements.FirstOrDefaultAsync(a => a.Id == announcementId, cancellationToken)
            ?? throw new NotFoundException("Announcement not found.");
        authorizationGuard.EnsureCommunityAuthorized(announcement.CommunityId);

        announcement.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry(isActive ? "Activate" : "Deactivate", "Announcement", announcement.Id.ToString(), announcement.CommunityId, Detail: null),
            cancellationToken);

        return ToDto(announcement);
    }

    public async Task DeleteAnnouncementAsync(Guid announcementId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Delete, "Announcement", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to delete announcements.");
        }

        var announcement = await db.Announcements.FirstOrDefaultAsync(a => a.Id == announcementId, cancellationToken)
            ?? throw new NotFoundException("Announcement not found.");
        authorizationGuard.EnsureCommunityAuthorized(announcement.CommunityId);

        // Remove is converted to a soft delete by the audit interceptor; the archived row stays
        // traceable but drops out of every ordinary read (the global filter).
        db.Announcements.Remove(announcement);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Delete", "Announcement", announcement.Id.ToString(), announcement.CommunityId, Detail: null),
            cancellationToken);
    }

    /// <summary>Validate a category code against the configurable Announcement_Category list (per community).</summary>
    private async Task<string> RequireValidCategoryAsync(string? category, Guid communityId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ValidationException("category", "A category is required.");
        }
        var allowed = await config.GetMasterDataAsync(MasterDataKeys.AnnouncementCategory, communityId, cancellationToken: cancellationToken);
        var trimmed = category.Trim();
        if (!allowed.Any(e => string.Equals(e.Code, trimmed, StringComparison.Ordinal)))
        {
            throw new ValidationException("category", $"'{category}' is not a valid announcement category.");
        }
        return trimmed;
    }

    private static string RequireText(string? value, string field, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(field, message);
        }
        return value.Trim();
    }

    private static AnnouncementDto ToDto(DomainAnnouncement a) =>
        new(a.Id, a.CommunityId, a.Title, a.Body, a.Category, a.PublishedAtUtc, a.IsActive);
}
