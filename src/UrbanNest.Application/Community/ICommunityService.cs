using UrbanNest.Application.Querying;
using UrbanNest.Contracts.Envelope;

namespace UrbanNest.Application.Community;

/// <summary>
/// Community management (Req 12.1, 12.2, 12.5). Create/edit/soft-delete/get a community — the
/// tenant root. Create assigns the CommunityId (the new row's own Id) and is a platform
/// super-admin action; the community-type is validated against the configurable
/// <c>Community_Type</c> master-data list (Req 12.2). Every mutation is recorded in the audit trail
/// (Req 6.1); delete is a soft delete that stays traceable (Req 12.5). Reads are tenant-scoped by
/// the global query filter (a community admin sees only their own community row).
/// </summary>
public interface ICommunityService
{
    /// <summary>
    /// Create a community (Req 12.1). The generated id becomes the CommunityId on all child data.
    /// Rejects an unknown organization or an invalid community-type with a validation error.
    /// </summary>
    Task<CommunityDto> CreateAsync(CreateCommunityRequest request, CancellationToken cancellationToken = default);

    /// <summary>Edit a community (Req 12.2). Invalid community-type → validation error; unknown id → not found.</summary>
    Task<CommunityDto> UpdateAsync(Guid communityId, UpdateCommunityRequest request, CancellationToken cancellationToken = default);

    /// <summary>Soft-delete (archive) a community (Req 12.5). The row is retained and still retrievable with <c>includeArchived</c>.</summary>
    Task DeleteAsync(Guid communityId, CancellationToken cancellationToken = default);

    /// <summary>Fetch a single community, tenant-scoped. When <paramref name="includeArchived"/> is true a soft-deleted row is returned (Req 12.5).</summary>
    Task<CommunityDto?> GetAsync(Guid communityId, bool includeArchived = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated/filter/sort/search list of (non-archived) communities (Req 12.4, 9.3). The result is
    /// scoped to the caller's authorized communities by the global tenant query filter — a community
    /// admin sees only their own community row, a platform super-admin sees across.
    /// </summary>
    Task<PagedData<CommunityDto>> ListAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default);
}
