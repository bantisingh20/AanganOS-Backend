using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Community;

/// <summary>
/// A community notice-board announcement (MVP notices slice): a message a community publishes for
/// its residents to read — a notice, event heads-up, maintenance advisory, etc. Carries its owning
/// <see cref="TenantOwnedEntity.CommunityId"/>.
///
/// <para><b>Community-wide, NOT resident self-view data.</b> Like <c>Amenity</c>/<c>Asset</c>, an
/// announcement belongs to the whole community, so it is a <see cref="TenantOwnedEntity"/> that is
/// deliberately NOT <see cref="IUnitScoped"/> — every resident in the community reads the same
/// board, bounded only by the tenant global filter, not pinned to one unit.</para>
///
/// <para><b>Configurable category (steering: no hardcoding).</b> <see cref="Category"/> resolves
/// against the editable <c>Announcement_Category</c> master-data list (general / event / maintenance
/// / emergency / ...); stored as a stable code, never a frozen enum. The constants in
/// <see cref="AnnouncementCategories"/> only name the seeded defaults.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token, audit columns.</para>
/// </summary>
public class Announcement : TenantOwnedEntity
{
    /// <summary>Short headline for the announcement. Required.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The announcement body / full message text. Required.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Configurable category code resolved against the <c>Announcement_Category</c> master-data list.
    /// Stored as the stable code, never a frozen enum.
    /// </summary>
    public string Category { get; set; } = AnnouncementCategories.General;

    /// <summary>When the announcement was published (UTC). The board is ordered newest-first on this.</summary>
    public DateTime PublishedAtUtc { get; set; }
}

/// <summary>
/// Well-known default <c>Announcement_Category</c> codes. Seeded master-data codes kept as constants
/// so the service resolves/compares the category without a magic string. <b>Data</b>, not a
/// behavioural enum — the list is editable per community; these constants only name the seeded
/// defaults.
/// </summary>
public static class AnnouncementCategories
{
    /// <summary>Default category: a general community notice.</summary>
    public const string General = "general";

    /// <summary>An upcoming community event.</summary>
    public const string Event = "event";

    /// <summary>A maintenance / facilities advisory.</summary>
    public const string Maintenance = "maintenance";

    /// <summary>An urgent / emergency notice.</summary>
    public const string Emergency = "emergency";
}
