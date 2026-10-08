using CommunityOS.Application.Querying;
using CommunityOS.Contracts.Envelope;

namespace CommunityOS.Application.Identity;

/// <summary>
/// Role management (Req 3.5, 3.6, 3.7). Define a role with a set of (Action, ResourceType)
/// permissions, edit it, soft-delete it, and list roles. A duplicate role name within the same
/// scope is rejected with a conflict — relying on the DB unique index, not a read-then-write
/// pre-check (Req 3.6, concurrency-safe). Mutations are audited (Req 6.1).
/// </summary>
public interface IRoleService
{
    /// <summary>Define a role and its permission set (Req 3.5). Duplicate name within scope → conflict (Req 3.6); unknown permission → validation error.</summary>
    Task<RoleDto> DefineAsync(DefineRoleRequest request, CancellationToken cancellationToken = default);

    /// <summary>Redefine an existing role's name and/or permission set (Req 3.5). Duplicate name within scope → conflict; unknown id → not found.</summary>
    Task<RoleDto> UpdateAsync(Guid roleId, DefineRoleRequest request, CancellationToken cancellationToken = default);

    /// <summary>Soft-delete (archive) a role (Req 3.7). Retained and retrievable with <c>includeArchived</c>.</summary>
    Task DeleteAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>Fetch a single role with its granted permissions. <paramref name="includeArchived"/> returns a soft-deleted role (Req 3.7).</summary>
    Task<RoleDto?> GetAsync(Guid roleId, bool includeArchived = false, CancellationToken cancellationToken = default);

    /// <summary>Paginated/filter/sort/search list of (non-archived) roles (Req 9.3).</summary>
    Task<PagedData<RoleDto>> ListAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default);
}
