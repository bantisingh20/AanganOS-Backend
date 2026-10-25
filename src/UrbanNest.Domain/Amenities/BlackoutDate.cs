using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Amenities;

/// <summary>
/// A date/period during which an <see cref="Amenity"/> is unavailable for booking (Req 38.4):
/// maintenance windows, community events, holidays. A tenant-owned child of an amenity carrying the
/// SAME <see cref="TenantOwnedEntity.CommunityId"/> as its parent, NOT <see cref="IUnitScoped"/> —
/// blackout configuration is community infrastructure.
///
/// <para><b>Half-open UTC range (Req 38.4).</b> The blackout covers <see cref="StartUtc"/>
/// (inclusive) up to <see cref="EndUtc"/> (exclusive). The service validates <c>EndUtc &gt;
/// StartUtc</c>. The 23.2 booking lifecycle rejects a booking whose slot instant falls within ANY
/// active, non-soft-deleted blackout range for the amenity. Overlapping blackout ranges are
/// deliberately ALLOWED (two maintenance windows can overlap) — the lifecycle treats the union as
/// unavailable, so no uniqueness constraint is placed on the range. 23.1 only STORES the ranges.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter (a removed
/// blackout stays traceable but is hidden from ordinary reads — lifting a blackout is a soft delete),
/// rowversion concurrency token (Req 22.4), audit columns.</para>
/// </summary>
public class BlackoutDate : TenantOwnedEntity
{
    /// <summary>The amenity this blackout applies to (Req 38.4). Required.</summary>
    public Guid AmenityId { get; set; }

    /// <summary>Start of the unavailable period, inclusive (Req 38.4). UTC. Validated strictly before <see cref="EndUtc"/>.</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>End of the unavailable period, exclusive (Req 38.4). UTC. Validated strictly after <see cref="StartUtc"/>.</summary>
    public DateTime EndUtc { get; set; }

    /// <summary>Human-readable reason for the blackout (e.g. "Pool resurfacing", "Diwali event"). Required (Req 38.4).</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Whether a given UTC instant falls within this blackout's half-open range (Req 38.4):
    /// <c>StartUtc &lt;= instant &lt; EndUtc</c>. Pure helper the 23.2 lifecycle uses to decide
    /// unavailability; no I/O.
    /// </summary>
    public bool Covers(DateTime instantUtc) => StartUtc <= instantUtc && instantUtc < EndUtc;
}
