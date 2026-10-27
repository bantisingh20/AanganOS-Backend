namespace App.Application.Community;

/// <summary>Read shape for an <c>Organization</c> returned to callers.</summary>
/// <param name="Id">The organization id.</param>
/// <param name="Name">Organization name.</param>
/// <param name="IsArchived">Whether the organization is soft-deleted (Req 12.5 parity).</param>
public sealed record OrganizationDto(Guid Id, string Name, bool IsArchived);

/// <summary>Create an organization (Req 12.1).</summary>
/// <param name="Name">Organization name. Required.</param>
public sealed record CreateOrganizationRequest(string Name);

/// <summary>Edit an organization. Null fields are left unchanged.</summary>
/// <param name="Name">New name, or null to leave unchanged.</param>
public sealed record UpdateOrganizationRequest(string? Name);
