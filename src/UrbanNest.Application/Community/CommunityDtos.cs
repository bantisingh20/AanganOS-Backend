namespace UrbanNest.Application.Community;

/// <summary>Read shape for a <c>Community</c> returned to callers.</summary>
/// <param name="Id">The community id — this IS the CommunityId used on all child data (Req 12.1).</param>
/// <param name="OrganizationId">Owning organization.</param>
/// <param name="Name">Community name.</param>
/// <param name="CommunityType">Configurable community-type code (Req 12.2).</param>
/// <param name="IsArchived">Whether the community is soft-deleted (Req 12.5).</param>
public sealed record CommunityDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string CommunityType,
    bool IsArchived);

/// <summary>
/// Create a community (Req 12.1) — a platform super-admin action. The generated id becomes the
/// CommunityId stamped on all child data. <paramref name="CommunityType"/> is validated against the
/// configurable <c>Community_Type</c> master-data list (Req 12.2).
/// </summary>
/// <param name="OrganizationId">Owning organization. Required.</param>
/// <param name="Name">Community name. Required.</param>
/// <param name="CommunityType">Community-type code from the Community_Type list. Required.</param>
public sealed record CreateCommunityRequest(Guid OrganizationId, string Name, string CommunityType);

/// <summary>Edit a community. Null fields are left unchanged.</summary>
/// <param name="Name">New name, or null to leave unchanged.</param>
/// <param name="CommunityType">New community-type code (validated), or null to leave unchanged.</param>
public sealed record UpdateCommunityRequest(string? Name, string? CommunityType);
