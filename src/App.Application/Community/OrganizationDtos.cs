using App.Application.Subscription;

namespace App.Application.Community;

/// <summary>Read shape for an <c>Organization</c> returned to callers.</summary>
/// <param name="Id">The organization id.</param>
/// <param name="Name">Organization name.</param>
/// <param name="PropertyType">Property / community type code.</param>
/// <param name="IsArchived">Whether the organization is soft-deleted (Req 12.5 parity).</param>
public sealed record OrganizationDto(Guid Id, string Name, string? PropertyType, bool IsArchived, bool HasActiveAdmin = false, long CommunityCount = 0, SubscriptionDto? ExpiryData = null);

/// <summary>Create an organization (Req 12.1).</summary>
/// <param name="Name">Organization name. Required.</param>
/// <param name="PropertyType">Property type code. Optional.</param>
public sealed record CreateOrganizationRequest(string Name, string? PropertyType = null);

/// <summary>Edit an organization. Null fields are left unchanged.</summary>
/// <param name="Name">New name, or null to leave unchanged.</param>
/// <param name="PropertyType">New property type, or null to leave unchanged.</param>
public sealed record UpdateOrganizationRequest(string? Name, string? PropertyType = null);
