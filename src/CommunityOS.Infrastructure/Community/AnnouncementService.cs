using CommunityOS.Application.Abstractions.Security;
using CommunityOS.Application.Community;
using CommunityOS.Application.Exceptions;
using CommunityOS.Application.Querying;
using CommunityOS.Contracts.Envelope;
using CommunityOS.Domain.Identity;
using CommunityOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using DomainAnnouncement = CommunityOS.Domain.Community.Announcement;

namespace CommunityOS.Infrastructure.Community;

/// <summary>
/// EF-backed <see cref="IAnnouncementService"/> (MVP notices slice): the community notice-board read
/// surface. Reads are tenant-scoped by the global query filter (a resident only sees the board of a
/// community they belong to; soft-deleted notices are hidden), <c>AsNoTracking</c>, and the list
/// rides the shared allow-listed <c>ListQueryConfiguration</c> + <c>ToPagedResultAsync</c> — the same
/// shape as <c>AmenityService.ListAmenitiesAsync</c>. Read-only in the MVP (no publish path).
/// </summary>
public sealed class AnnouncementService(
    AppDbContext db,
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
        ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // Defense-in-depth: listing the board requires View on Announcement. The endpoint gate
        // declares the same; the invariant holds regardless of caller.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "Announcement", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view announcements.");
        }

        // Tenant-scoped by the global query filter (soft-deleted rows hidden); the allow-listed query
        // config fixes the sort/filter/search surface; AsNoTracking projection, page size clamped by
        // the shared policy — mirrors ListAmenitiesAsync.
        return await db.Announcements.AsNoTracking().ToPagedResultAsync(
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

    private static AnnouncementDto ToDto(DomainAnnouncement a) =>
        new(a.Id, a.CommunityId, a.Title, a.Body, a.Category, a.PublishedAtUtc);
}
