using UrbanNest.Application.Querying;
using UrbanNest.Contracts.Envelope;

namespace UrbanNest.Application.Community;

/// <summary>
/// Organization management (Req 12.1). Create/edit/soft-delete/get the platform-scoped top-level
/// tenant owner. Each mutation is recorded in the audit trail via Audit_Service (Req 6.1); delete
/// is a soft delete that stays traceable.
/// </summary>
public interface IOrganizationService
{
    /// <summary>Create an organization (Req 12.1).</summary>
    Task<OrganizationDto> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Edit an organization. Unknown id → not found.</summary>
    Task<OrganizationDto> UpdateAsync(Guid organizationId, UpdateOrganizationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Soft-delete (archive) an organization. The row is retained and still retrievable with <c>includeArchived</c>.</summary>
    Task DeleteAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>Fetch a single organization. When <paramref name="includeArchived"/> is true a soft-deleted row is returned.</summary>
    Task<OrganizationDto?> GetAsync(Guid organizationId, bool includeArchived = false, CancellationToken cancellationToken = default);

    /// <summary>Paginated/filter/sort/search list of (non-archived) organizations (Req 9.3).</summary>
    Task<PagedData<OrganizationDto>> ListAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default);
}
