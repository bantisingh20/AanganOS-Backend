namespace UrbanNest.Application.Community;

// =================================================================================================
// MVP notices slice — the community notice board. Read-only resident-facing surface: list the
// community's announcements (newest-first) and fetch one. Shapes mirror the other module read DTOs'
// style. No write DTO in the MVP — the board is populated by the dev seed / future admin task.
// =================================================================================================

/// <summary>Read shape for an <c>Announcement</c> (MVP notices slice).</summary>
/// <param name="Id">The announcement id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="Title">Short headline.</param>
/// <param name="Body">The full message text.</param>
/// <param name="Category">Configurable category code (<c>Announcement_Category</c> list).</param>
/// <param name="PublishedAtUtc">When the announcement was published (UTC).</param>
/// <param name="IsActive">Whether the notice is live on the resident board (admin can toggle).</param>
public sealed record AnnouncementDto(
    Guid Id,
    Guid CommunityId,
    string Title,
    string Body,
    string Category,
    DateTime PublishedAtUtc,
    bool IsActive);

/// <summary>
/// Publish a new announcement (admin). The category is validated against the configurable
/// <c>Announcement_Category</c> list; it is published active and stamped with the current UTC time.
/// </summary>
/// <param name="CommunityId">Owning community. Required.</param>
/// <param name="Title">Short headline. Required.</param>
/// <param name="Body">Full message text. Required.</param>
/// <param name="Category">Category code from the Announcement_Category list. Required.</param>
public sealed record CreateAnnouncementRequest(
    Guid CommunityId,
    string Title,
    string Body,
    string Category);

/// <summary>
/// Edit an announcement (admin). Null fields are left unchanged; a changed category is re-validated.
/// Visibility is toggled through the dedicated activate/deactivate action, not here.
/// </summary>
/// <param name="Title">New headline, or null to leave unchanged.</param>
/// <param name="Body">New body, or null to leave unchanged.</param>
/// <param name="Category">New category code (validated), or null to leave unchanged.</param>
public sealed record UpdateAnnouncementRequest(
    string? Title,
    string? Body,
    string? Category);
