namespace CommunityOS.Application.Config;

/// <summary>
/// A resolved master-data entry returned to callers (code + label + ordering + active flag).
/// This is the read shape domain code and endpoints consume; the persisted
/// <c>MasterDataItem</c> entity stays in the data layer.
/// </summary>
/// <param name="Code">Stable machine code stored on referencing records (e.g. <c>apartment</c>).</param>
/// <param name="Label">Human-readable label (e.g. <c>Apartment</c>).</param>
/// <param name="SortOrder">Display order within the list.</param>
/// <param name="IsActive">Whether the entry is offered as a new choice.</param>
public sealed record MasterDataEntry(string Code, string Label, int SortOrder, bool IsActive);
