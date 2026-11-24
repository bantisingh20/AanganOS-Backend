namespace App.Application.Community;

// =================================================================================================
// MVP notices slice — the community notice board. Read-only resident-facing surface: list the
// community's announcements (newest-first) and fetch one. Shapes mirror the other module read DTOs'
// style. No write DTO in the MVP — the board is populated by the dev seed / future admin task.
// =================================================================================================

/// <summary>Read shape for an <c>Announcement</c> (MVP notices slice).</summary>
public sealed record AnnouncementDto(
    Guid Id,
    Guid CommunityId,
    Guid? OrganizationId,
    string Title,
    string Body,
    string Category,
    DateTime PublishedAtUtc,
    bool IsActive,
    string? CommunityName = null,
    string? OrganizationName = null,
    string? Scope = null);

/// <summary>
/// Publish a new announcement. Gets organization and community from session/current user when omitted.
/// </summary>
public sealed record CreateAnnouncementRequest(
    string Title,
    string Body,
    string Category,
    Guid? CommunityId = null,
    Guid? OrganizationId = null);

/// <summary>
/// Edit an announcement (admin). Null fields are left unchanged; a changed category is re-validated.
/// Visibility is toggled through the dedicated activate/deactivate action, not here.
/// </summary>
public sealed record UpdateAnnouncementRequest(
    string? Title,
    string? Body,
    string? Category);
