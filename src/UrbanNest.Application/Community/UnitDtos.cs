namespace UrbanNest.Application.Community;

/// <summary>Read shape for a <c>Unit</c> returned to callers.</summary>
/// <param name="Id">The unit id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="HierarchyNodeId">Optional hierarchy node (Floor/Building/…) the unit sits under (Req 14.1).</param>
/// <param name="UnitNumber">Unit number/label; unique within the community (Req 14.4).</param>
/// <param name="UnitType">Configurable unit-type code (Req 14.2).</param>
/// <param name="Area">Optional floor area.</param>
/// <param name="Floor">Optional floor designation.</param>
/// <param name="Bedrooms">Optional bedroom count.</param>
/// <param name="UnitStatus">Configurable unit-status code (Req 14.3).</param>
/// <param name="CustomAttributes">Free-form community-specific attributes (Req 14.1).</param>
/// <param name="IsArchived">Whether the unit is soft-deleted (Req 14.6).</param>
public sealed record UnitDto(
    Guid Id,
    Guid CommunityId,
    Guid? HierarchyNodeId,
    string UnitNumber,
    string UnitType,
    decimal? Area,
    string? Floor,
    int? Bedrooms,
    string UnitStatus,
    IReadOnlyDictionary<string, string> CustomAttributes,
    bool IsArchived);

/// <summary>
/// Create a unit (Req 14.1). <paramref name="UnitType"/>/<paramref name="UnitStatus"/> are validated
/// against the configurable <c>Unit_Type</c>/<c>Unit_Status</c> lists (Req 14.2, 14.3). When
/// <paramref name="HierarchyNodeId"/> is supplied it must reference a node in the same community.
/// A duplicate <c>(CommunityId, UnitNumber)</c> is rejected by the DB unique index as a conflict
/// (Req 14.4, 22.1).
/// </summary>
/// <param name="CommunityId">Owning community. Required.</param>
/// <param name="HierarchyNodeId">Optional hierarchy node (same community); null if unmodeled.</param>
/// <param name="UnitNumber">Unit number/label. Required.</param>
/// <param name="UnitType">Unit-type code from the Unit_Type list. Required.</param>
/// <param name="Area">Optional floor area.</param>
/// <param name="Floor">Optional floor designation.</param>
/// <param name="Bedrooms">Optional bedroom count.</param>
/// <param name="UnitStatus">Unit-status code from the Unit_Status list. Required.</param>
/// <param name="CustomAttributes">Optional free-form attributes (Req 14.1); null is treated as empty.</param>
public sealed record CreateUnitRequest(
    Guid CommunityId,
    Guid? HierarchyNodeId,
    string UnitNumber,
    string UnitType,
    decimal? Area,
    string? Floor,
    int? Bedrooms,
    string UnitStatus,
    IReadOnlyDictionary<string, string>? CustomAttributes);

/// <summary>Edit a unit. Null fields are left unchanged. A changed type/status/node is re-validated.</summary>
/// <param name="HierarchyNodeId">New hierarchy node (same community); pass to re-link. See <see cref="UpdateHierarchyNode"/>.</param>
/// <param name="UpdateHierarchyNode">When true, <see cref="HierarchyNodeId"/> is applied (allowing null = unlink); when false the link is untouched.</param>
/// <param name="UnitNumber">New unit number, or null to leave unchanged.</param>
/// <param name="UnitType">New unit-type code (validated), or null to leave unchanged.</param>
/// <param name="Area">New area; see <see cref="UpdateArea"/>.</param>
/// <param name="UpdateArea">When true, <see cref="Area"/> is applied (allowing null = clear); when false untouched.</param>
/// <param name="Floor">New floor; see <see cref="UpdateFloor"/>.</param>
/// <param name="UpdateFloor">When true, <see cref="Floor"/> is applied (allowing null = clear); when false untouched.</param>
/// <param name="Bedrooms">New bedroom count; see <see cref="UpdateBedrooms"/>.</param>
/// <param name="UpdateBedrooms">When true, <see cref="Bedrooms"/> is applied (allowing null = clear); when false untouched.</param>
/// <param name="UnitStatus">New unit-status code (validated), or null to leave unchanged.</param>
/// <param name="CustomAttributes">Replacement attribute map; null leaves them unchanged.</param>
public sealed record UpdateUnitRequest(
    Guid? HierarchyNodeId,
    bool UpdateHierarchyNode,
    string? UnitNumber,
    string? UnitType,
    decimal? Area,
    bool UpdateArea,
    string? Floor,
    bool UpdateFloor,
    int? Bedrooms,
    bool UpdateBedrooms,
    string? UnitStatus,
    IReadOnlyDictionary<string, string>? CustomAttributes);
