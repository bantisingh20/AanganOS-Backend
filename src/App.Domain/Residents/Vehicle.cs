using App.Domain.Common;

namespace App.Domain.Residents;

/// <summary>
/// A vehicle registered against a resident Unit (Req 19.1). Carries its owning
/// <see cref="TenantOwnedEntity.CommunityId"/> and the <see cref="UnitId"/> it belongs to.
///
/// <para><b>Resident self-view (Req 5.4, 19.5, 66.3).</b> Implements <see cref="IUnitScoped"/> via
/// its non-null <see cref="UnitId"/>, so the DbContext applies the resident self-scope filter: a
/// resident-self-scoped principal only sees vehicles whose <c>UnitId</c> matches their own unit.
/// This is how "my unit's vehicles" is bounded to the resident's own data — no bespoke predicate in
/// the service.</para>
///
/// <para><b>Configurable type (Req 19.1, steering: no hardcoding).</b> <see cref="VehicleType"/> is
/// a stable master-data code resolved at runtime against the editable <c>Vehicle_Type</c> list
/// (two_wheeler / four_wheeler / bicycle / commercial / other) — NOT a frozen enum — so an operator
/// can add or retire a type without a code change, exactly like <c>Unit_Type</c>/<c>Resident_Type</c>.</para>
///
/// <para><b>Concurrency-safe uniqueness (Req 19.2).</b> <c>(CommunityId, RegistrationNumber)</c> is a
/// DB-level unique index: two concurrent registrations of the same plate race to the index and
/// exactly one wins; the loser's violation is translated to CONFLICT (no application read-then-write
/// pre-check). Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter,
/// rowversion concurrency token, audit columns.</para>
/// </summary>
public class Vehicle : TenantOwnedEntity, IUnitScoped
{
    /// <summary>
    /// The unit this vehicle is registered to (Req 19.1). Non-null in practice (a vehicle is always
    /// tied to a unit); typed as nullable to satisfy <see cref="IUnitScoped"/>, which the resident
    /// self-scope filter keys on.
    /// </summary>
    public Guid? UnitId { get; set; }

    /// <summary>
    /// Vehicle registration/number plate (Req 19.1). Required; unique within the community
    /// (Req 19.2).
    /// </summary>
    public string RegistrationNumber { get; set; } = string.Empty;

    /// <summary>
    /// Configurable vehicle-type code resolved against the <c>Vehicle_Type</c> master-data list
    /// (Req 19.1). Stored as the stable code, never a frozen enum.
    /// </summary>
    public string VehicleType { get; set; } = string.Empty;
}
