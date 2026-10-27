namespace App.Application.Community;

/// <summary>Read shape for a <c>HierarchyNode</c> returned to callers.</summary>
/// <param name="Id">The node id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="ParentId">Parent node, or null for a root node (Req 13.2).</param>
/// <param name="Level">Configurable level code (Req 13.1, 13.2).</param>
/// <param name="Name">Node name.</param>
/// <param name="IsArchived">Whether the node is soft-deleted (Req 13.5).</param>
public sealed record HierarchyNodeDto(
    Guid Id,
    Guid CommunityId,
    Guid? ParentId,
    string Level,
    string Name,
    bool IsArchived);

/// <summary>
/// Create a hierarchy node (Req 13.1). <paramref name="ParentId"/> is optional (Req 13.2) and, when
/// supplied, must reference a node in the same community (Req 13.3). <paramref name="Level"/> is
/// validated against the configurable <c>Hierarchy_Level</c> master-data list.
/// </summary>
/// <param name="CommunityId">Owning community. Required.</param>
/// <param name="ParentId">Optional parent node (same community); null for a root node.</param>
/// <param name="Level">Level code from the Hierarchy_Level list. Required.</param>
/// <param name="Name">Node name. Required.</param>
public sealed record CreateHierarchyNodeRequest(
    Guid CommunityId,
    Guid? ParentId,
    string Level,
    string Name);

/// <summary>Edit a hierarchy node. Null fields are left unchanged. A changed parent is re-validated.</summary>
/// <param name="ParentId">New parent (same community); pass to re-parent. See <see cref="UpdateParent"/>.</param>
/// <param name="UpdateParent">When true, <see cref="ParentId"/> is applied (allowing a null = make root); when false the parent is untouched.</param>
/// <param name="Level">New level code (validated), or null to leave unchanged.</param>
/// <param name="Name">New name, or null to leave unchanged.</param>
public sealed record UpdateHierarchyNodeRequest(
    Guid? ParentId,
    bool UpdateParent,
    string? Level,
    string? Name);
