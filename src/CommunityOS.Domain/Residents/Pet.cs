using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Residents;

/// <summary>
/// A pet registered against a resident Unit (Req 19.3). Carries its owning
/// <see cref="TenantOwnedEntity.CommunityId"/> and the <see cref="UnitId"/> it belongs to.
///
/// <para><b>Resident self-view (Req 5.4, 19.5, 66.3).</b> Implements <see cref="IUnitScoped"/> via
/// its non-null <see cref="UnitId"/>, so the DbContext resident self-scope filter restricts a
/// resident principal to pets on their own unit automatically.</para>
///
/// <para><b>Configurable type (Req 19.3, steering: no hardcoding).</b> <see cref="PetType"/> is a
/// stable master-data code resolved at runtime against the editable <c>Pet_Type</c> list (dog / cat
/// / bird / other) — NOT a frozen enum.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token, audit columns. No uniqueness constraint — a unit may register several pets of
/// the same type (Req 19.3 stores type + owning unit only).</para>
/// </summary>
public class Pet : TenantOwnedEntity, IUnitScoped
{
    /// <summary>
    /// The unit this pet is registered to (Req 19.3). Non-null in practice; nullable to satisfy
    /// <see cref="IUnitScoped"/>, which the resident self-scope filter keys on.
    /// </summary>
    public Guid? UnitId { get; set; }

    /// <summary>
    /// Configurable pet-type code resolved against the <c>Pet_Type</c> master-data list (Req 19.3).
    /// Stored as the stable code, never a frozen enum.
    /// </summary>
    public string PetType { get; set; } = string.Empty;

    /// <summary>Optional pet name. Nullable — Req 19.3 requires only type + owning unit.</summary>
    public string? Name { get; set; }
}
