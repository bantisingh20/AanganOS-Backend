namespace UrbanNest.Application.Community;

/// <summary>
/// Announcement_Service (MVP notices slice): the community notice board read surface. A resident with
/// <c>View</c> on <c>Announcement</c> lists their community's announcements (newest-first) and reads
/// one. Reads are tenant-scoped by the EF global query filter — a resident only ever sees the board
/// of a community they belong to, with no bespoke predicate. Deliberately read-only in the MVP: the
/// board is populated by the dev seed (and a future admin publish task) rather than a resident write.
/// Mirrors the read side of the other module services.
/// </summary>
public interface IAnnouncementService
{
    /// <summary>
    /// Paginated/filtered/sorted/searchable announcement list, newest-first (MVP notices slice).
    /// Requires <c>View</c> on <c>Announcement</c>. Tenant-scoped by the global filter (soft-deleted
    /// notices hidden); the allow-listed query config fixes the sort/filter/search surface;
    /// <c>AsNoTracking</c> projection, page size clamped by the shared policy — mirrors
    /// <c>ListAmenitiesAsync</c>.
    ///
    /// <para>By default only ACTIVE notices are returned (the resident board). An admin management
    /// view passes <paramref name="includeInactive"/> = true to see deactivated notices as well so
    /// they can be re-activated or edited.</para>
    /// </summary>
    Task<UrbanNest.Contracts.Envelope.PagedData<AnnouncementDto>> ListAnnouncementsAsync(
        UrbanNest.Application.Querying.ListQueryParameters parameters,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    /// <summary>Fetch one announcement by id, tenant-scoped; unknown/foreign/soft-deleted id → null.</summary>
    Task<AnnouncementDto?> GetAnnouncementAsync(Guid announcementId, CancellationToken cancellationToken = default);

    /// <summary>Publish a new announcement (admin). Requires <c>Create</c> on <c>Announcement</c>; category validated.</summary>
    Task<AnnouncementDto> CreateAnnouncementAsync(CreateAnnouncementRequest request, CancellationToken cancellationToken = default);

    /// <summary>Edit an announcement's title/body/category (admin). Requires <c>Edit</c>; unknown id → NOT_FOUND.</summary>
    Task<AnnouncementDto> UpdateAnnouncementAsync(Guid announcementId, UpdateAnnouncementRequest request, CancellationToken cancellationToken = default);

    /// <summary>Activate/deactivate board visibility (admin). Requires <c>Edit</c>; unknown id → NOT_FOUND.</summary>
    Task<AnnouncementDto> SetAnnouncementActiveAsync(Guid announcementId, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>Soft-delete an announcement (admin). Requires <c>Delete</c>; retained + traceable; unknown id → NOT_FOUND.</summary>
    Task DeleteAnnouncementAsync(Guid announcementId, CancellationToken cancellationToken = default);
}
