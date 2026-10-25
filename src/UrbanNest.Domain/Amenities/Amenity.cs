using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Amenities;

/// <summary>
/// A bookable community amenity (Req 38.1): a facility a Facility Manager configures so residents
/// can book it under controlled conditions — a clubhouse, gym, pool, hall, court, garden, party
/// area or a custom facility. Carries its owning <see cref="TenantOwnedEntity.CommunityId"/>.
///
/// <para><b>Community infrastructure, NOT resident self-view data.</b> Like <c>Asset</c> /
/// <c>ParkingSlot</c> / <c>CustodyLocation</c>, an amenity belongs to the community, so it is a
/// <see cref="TenantOwnedEntity"/> that is deliberately NOT <see cref="IUnitScoped"/> — a
/// Facility-Manager register bounded by the community (the tenant global filter), not pinned to one
/// resident's unit. Residents booking it (Task 23.2) read the amenity through the tenant filter;
/// their own bookings are the resident-scoped data, not the amenity master.</para>
///
/// <para><b>Configurable type + lifecycle status (steering: no hardcoding, Req 38.1).</b>
/// <see cref="AmenityType"/> resolves against the editable <c>Amenity_Type</c> master-data list
/// (clubhouse / gym / pool / hall / court / garden / party_area / custom, ...) and
/// <see cref="Status"/> against <c>Amenity_Status</c> (active / inactive / under_maintenance),
/// starting <see cref="AmenityStatuses.Active"/> on creation. Both stored as stable codes an admin
/// can add to / retire without a code change, never frozen enums.</para>
///
/// <para><b>Community-unique code (Req 38.1).</b> <see cref="Code"/> is a short operator-facing
/// identifier unique within the community (<c>Unique(CommunityId, Code)</c>), enforced DB-side via
/// <c>UniqueConstraintGuard</c> — a duplicate maps to CONFLICT, never a read-then-write gap (same
/// approach as the <c>Asset</c> serial-number index).</para>
///
/// <para><b>Capacity (Req 38.1, 38.2).</b> <see cref="DefaultCapacity"/> is the amenity-level
/// default number of concurrent bookings; a <see cref="FacilitySlot"/> may override it per time
/// window with its own capacity. The slot capacity (and 23.2's BookedCount under the slot
/// RowVersion) is what the booking lifecycle enforces at-capacity against.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter (a retired
/// amenity stays traceable but is hidden from ordinary reads), rowversion concurrency token
/// (Req 22.4), audit columns.</para>
/// </summary>
public class Amenity : TenantOwnedEntity
{
    /// <summary>Human-readable amenity name/label (e.g. "Rooftop Clubhouse", "Tower-1 Gym"). Required (Req 38.1).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Short operator-facing code identifying the amenity (Req 38.1). Required and unique within the
    /// community (<c>Unique(CommunityId, Code)</c>), the DB-enforced identity used by operators and
    /// future booking references.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Configurable amenity-type code resolved against the <c>Amenity_Type</c> master-data list
    /// (Req 38.1). Stored as the stable code, never a frozen enum.
    /// </summary>
    public string AmenityType { get; set; } = string.Empty;

    /// <summary>
    /// Configurable lifecycle status resolved against the <c>Amenity_Status</c> master-data list
    /// (Req 38.1). Starts <see cref="AmenityStatuses.Active"/> on creation. Stored as the stable
    /// code, never a frozen enum. An inactive/under-maintenance amenity is not bookable (23.2 reads
    /// this).
    /// </summary>
    public string Status { get; set; } = AmenityStatuses.Active;

    /// <summary>The amenity's physical location within the community (e.g. "Clubhouse, 5th Floor"). Optional free text (Req 38.1).</summary>
    public string? Location { get; set; }

    /// <summary>Optional free-text description of the amenity and its usage (Req 38.1).</summary>
    public string? Description { get; set; }

    /// <summary>
    /// The amenity-level default number of concurrent bookings a slot permits (Req 38.1, 38.2) when a
    /// <see cref="FacilitySlot"/> does not override it. Always positive (the service rejects a
    /// non-positive capacity).
    /// </summary>
    public int DefaultCapacity { get; set; } = 1;
}

/// <summary>
/// Well-known default <c>Amenity_Type</c> codes (Req 38.1). Seeded master-data codes kept as
/// constants so the service resolves/compares the type without a magic string. <b>Data</b>, not a
/// behavioural enum — the type list is editable per community; these constants only name the seeded
/// defaults.
/// </summary>
public static class AmenityTypes
{
    public const string Clubhouse = "clubhouse";
    public const string Gym = "gym";
    public const string Pool = "pool";
    public const string Hall = "hall";
    public const string Court = "court";
    public const string Garden = "garden";
    public const string PartyArea = "party_area";

    /// <summary>A custom facility that does not fit the seeded types.</summary>
    public const string Custom = "custom";
}

/// <summary>
/// Well-known default <c>Amenity_Status</c> codes (Req 38.1). Seeded master-data codes kept as
/// constants so the service sets/compares status without a magic string. <b>Data</b>, not a
/// behavioural enum — the status list is editable master data; these constants only name the seeded
/// defaults.
/// </summary>
public static class AmenityStatuses
{
    /// <summary>Initial status on creation (Req 38.1): the amenity is bookable.</summary>
    public const string Active = "active";

    /// <summary>The amenity is inactive / not currently bookable.</summary>
    public const string Inactive = "inactive";

    /// <summary>The amenity is temporarily out of service for maintenance.</summary>
    public const string UnderMaintenance = "under_maintenance";
}
