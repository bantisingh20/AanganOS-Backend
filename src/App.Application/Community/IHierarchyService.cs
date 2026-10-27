namespace App.Application.Community;

/// <summary>
/// Property-hierarchy management (Req 13.1–13.5). Create/edit/soft-delete/get hierarchy nodes and
/// retrieve a community's whole hierarchy. Levels are optional (Req 13.2): a node's parent may be
/// any higher node in the same community or null. A parent in a different community is rejected with
/// a validation error (Req 13.3). Reads are tenant-scoped (Req 13.4); mutations are audited
/// (Req 6.1) and delete is a soft delete that stays traceable (Req 13.5).
/// </summary>
public interface IHierarchyService
{
    /// <summary>
    /// Create a hierarchy node (Req 13.1). An optional parent (Req 13.2) must be in the same
    /// community (Req 13.3) — else a validation error. The level is validated against the
    /// configurable <c>Hierarchy_Level</c> list.
    /// </summary>
    Task<HierarchyNodeDto> CreateAsync(CreateHierarchyNodeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Edit a hierarchy node. A re-parent is re-validated for same-community (Req 13.3); unknown id → not found.</summary>
    Task<HierarchyNodeDto> UpdateAsync(Guid nodeId, UpdateHierarchyNodeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Soft-delete (archive) a hierarchy node (Req 13.5). The row is retained and still retrievable with <c>includeArchived</c>.</summary>
    Task DeleteAsync(Guid nodeId, CancellationToken cancellationToken = default);

    /// <summary>Fetch a single node, tenant-scoped. When <paramref name="includeArchived"/> is true a soft-deleted node is returned (Req 13.5).</summary>
    Task<HierarchyNodeDto?> GetAsync(Guid nodeId, bool includeArchived = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve a community's full hierarchy, tenant-scoped (Req 13.4) in a single query (no N+1).
    /// Returns the live (non-archived) nodes ordered by parent then name.
    /// </summary>
    Task<IReadOnlyList<HierarchyNodeDto>> GetCommunityHierarchyAsync(Guid communityId, CancellationToken cancellationToken = default);
}
