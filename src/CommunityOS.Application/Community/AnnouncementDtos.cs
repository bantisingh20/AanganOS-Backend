namespace CommunityOS.Application.Community;

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
public sealed record AnnouncementDto(
    Guid Id,
    Guid CommunityId,
    string Title,
    string Body,
    string Category,
    DateTime PublishedAtUtc);
