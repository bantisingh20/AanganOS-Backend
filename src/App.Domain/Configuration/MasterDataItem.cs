using App.Domain.Common;

namespace App.Domain.Config;

/// <summary>
/// One editable entry in a master-data reference list (Req 8.4, 66.4). <see cref="ListKey"/> names
/// the list (e.g. <c>Unit_Type</c>, <c>Unit_Status</c>, later <c>Ticket_Category</c>,
/// <c>Visitor_Category</c>, <c>Parcel_Status</c>...). The set of lists is open-ended: any later
/// module registers its own list simply by seeding rows under a new <see cref="ListKey"/> — no new
/// code or enum is needed, which is the whole point of "no hardcoding".
///
/// The values here are <b>data, not behavioural enums</b>: domain code validates a Unit's type
/// against the active <c>Unit_Type</c> list resolved at runtime rather than against a compiled
/// enum, so an admin can add/retire a type without a code change.
///
/// Scoping mirrors <see cref="Configuration"/>: a <see cref="ConfigScope.Platform"/> row (null
/// <see cref="CommunityId"/>) is the default list every community sees; a
/// <see cref="ConfigScope.Community"/> row lets one community add/override an entry. It derives from
/// <see cref="PlatformEntity"/> for the same tenant-filter reason documented on
/// <see cref="Configuration"/> — platform rows must stay readable for fallback, and community
/// isolation is enforced in Config_Service.
/// </summary>
public class MasterDataItem : PlatformEntity
{
    /// <summary>The list this entry belongs to (e.g. <c>Unit_Type</c>). Required.</summary>
    public string ListKey { get; set; } = string.Empty;

    /// <summary>Platform-wide default entry or a per-community entry (Req 8.1).</summary>
    public ConfigScope Scope { get; set; }

    /// <summary>Owning community for a community-scoped entry; null for a platform default.</summary>
    public Guid? CommunityId { get; set; }

    /// <summary>Stable machine code stored on referencing records (e.g. <c>apartment</c>). Required.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable label shown in UIs (e.g. <c>Apartment</c>).</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Sort order within the list.</summary>
    public int SortOrder { get; set; }

    /// <summary>When false the entry is retired: kept for history but not offered as a new choice.</summary>
    public bool IsActive { get; set; } = true;
}
