using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Parking;

/// <summary>
/// A parking zone in the community's parking layout (Req 40.1): a named grouping of
/// <c>ParkingSlot</c>s — a basement block, a tower podium, a visitor bay, an EV bank — that an
/// operator defines so the layout and the occupancy dashboard are organised by physical area. A
/// community-scoped master row identified by a human-readable <see cref="Code"/> unique within the
/// community.
///
/// <para><b>Configurable zone type (steering: no hardcoding, Req 40.1).</b> <see cref="ZoneType"/>
/// resolves against the editable <c>Parking_Zone_Type</c> master-data list (basement / podium /
/// surface / visitor / ev, ...) — never a frozen enum — so an operator can add or retire a type
/// without a code change.</para>
///
/// <para>Not <see cref="IUnitScoped"/>: a zone is community infrastructure, not resident self-view
/// data (same stance as <c>ParkingSlot</c>/<c>Asset</c>/<c>CustodyLocation</c>). Derives from
/// <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion concurrency token,
/// audit columns.</para>
/// </summary>
public class ParkingZone : TenantOwnedEntity
{
    /// <summary>Human-readable zone code/label (e.g. "B1-NORTH", "VISITOR-BAY"). Required; unique within the community (Req 40.1).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable zone name (e.g. "Basement 1 North Block"). Required.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Configurable zone-type code resolved against the <c>Parking_Zone_Type</c> master-data list
    /// (Req 40.1). Stored as the stable code, never a frozen enum. Defaults to
    /// <see cref="ParkingZoneTypes.Surface"/>.
    /// </summary>
    public string ZoneType { get; set; } = ParkingZoneTypes.Surface;

    /// <summary>Optional free-text level/floor the zone sits on (e.g. "B1", "Ground"). Optional (Req 40.1).</summary>
    public string? Level { get; set; }
}

/// <summary>
/// Well-known default <c>Parking_Zone_Type</c> codes (Req 40.1). Seeded master-data codes kept as
/// constants so the service sets/compares the zone type without a magic string. <b>Data</b>, not a
/// behavioural enum — the list is editable per community; these constants only name the seeded
/// defaults.
/// </summary>
public static class ParkingZoneTypes
{
    /// <summary>Default: a surface (ground-level) parking area.</summary>
    public const string Surface = "surface";

    /// <summary>A basement parking area.</summary>
    public const string Basement = "basement";

    /// <summary>A podium/stilt parking area.</summary>
    public const string Podium = "podium";

    /// <summary>A visitor/guest parking area.</summary>
    public const string Visitor = "visitor";

    /// <summary>An EV-charging parking area.</summary>
    public const string Ev = "ev";
}
