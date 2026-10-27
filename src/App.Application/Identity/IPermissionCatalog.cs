using App.Domain.Identity;

namespace App.Application.Identity;

/// <summary>
/// The permission catalog (Req 3.2): the registrable set of known (ResourceType, Action)
/// permissions. The foundation catalog is seeded as editable data (EF <c>HasData</c>); this
/// service lets an admin list it and register additional entries without a code change (steering:
/// no hardcoding — resource types are data, not compiled gates). Registration is idempotent and
/// concurrency-safe: a duplicate (ResourceType, Action) relies on the DB unique index and is
/// returned as the existing row rather than failing a race.
/// </summary>
public interface IPermissionCatalog
{
    /// <summary>List the registered permission catalog (Req 3.2), optionally filtered to one resource type.</summary>
    Task<IReadOnlyList<PermissionDto>> ListAsync(string? resourceType = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Register a (ResourceType, Action) permission if absent, returning the catalog entry (Req 3.2).
    /// Idempotent: an existing entry is returned unchanged. Audited on first registration.
    /// </summary>
    Task<PermissionDto> RegisterAsync(string resourceType, PermissionAction action, CancellationToken cancellationToken = default);
}
