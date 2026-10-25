using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Community;

/// <summary>
/// One node in a community's configurable property hierarchy (Req 13.1). A node is a Phase,
/// Building/Tower, Block, Zone or Floor (Unit is a separate entity, added in Task 11.2) that sits
/// under an optional <see cref="ParentId"/> and carries its owning <see cref="TenantOwnedEntity.CommunityId"/>.
///
/// <para><b>Optional levels (Req 13.2).</b> There is deliberately no fixed Phase→Building→Block→…
/// chain. <see cref="Level"/> is a master-data code resolved against the editable
/// <c>Hierarchy_Level</c> list (not a frozen enum), and <see cref="ParentId"/> may point at ANY
/// higher node in the same community or be null for a root node — so a community can include or omit
/// any level and model its real layout rather than one hard-coded society shape.</para>
///
/// <para><b>Same-community parent (Req 13.3).</b> A node's parent must belong to the same
/// community; the service rejects a cross-community parent with a validation error. The self-
/// referencing <see cref="Parent"/>/<see cref="Children"/> relationship is kept within one tenant.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>, so the global tenant + soft-delete filter
/// scopes every read to the caller's authorized communities (Req 13.4) and delete is a soft delete
/// that stays traceable (Req 13.5).</para>
/// </summary>
public class HierarchyNode : TenantOwnedEntity
{
    /// <summary>
    /// Optional parent node (Req 13.2). Null for a top-level node. When set it must reference a node
    /// in the SAME community (Req 13.3) — enforced in the service before persist.
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>Navigation to the parent node (same community). Null for a root node.</summary>
    public HierarchyNode? Parent { get; set; }

    /// <summary>Child nodes directly under this one (same community).</summary>
    public ICollection<HierarchyNode> Children { get; set; } = new List<HierarchyNode>();

    /// <summary>
    /// Configurable level code resolved against the <c>Hierarchy_Level</c> master-data list
    /// (phase / building_tower / block / zone / floor). Stored as the stable code, never a frozen
    /// enum, so levels stay optional and editable (Req 13.2).
    /// </summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>Human-readable node name (e.g. "Tower A", "Phase 1", "3rd Floor"). Required.</summary>
    public string Name { get; set; } = string.Empty;
}
