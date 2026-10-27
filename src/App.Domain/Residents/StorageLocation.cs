using App.Domain.Common;

namespace App.Domain.Residents;

/// <summary>
/// A storage location master row within a community, optionally assigned to a Unit (Req 19.4). The
/// storage counterpart of <see cref="ParkingSlot"/> — a labelled location plus an optional owning
/// <see cref="AssignedUnitId"/> an operator sets via the Assign operation.
///
/// <para><b>Minimal Phase 2 scope.</b> Like <see cref="ParkingSlot"/>, this records the basic
/// association only: a reassignable nullable owner with a same-community check. ponytail: no active
/// one-location-one-unit concurrency guard — the full storage/parking module is Phase 6 (Task 24).</para>
///
/// <para>Not <see cref="IUnitScoped"/>: a storage location is community infrastructure an admin
/// manages, not resident self-view data. Derives from <see cref="TenantOwnedEntity"/>: tenant +
/// soft-delete global filter, rowversion, audit columns.</para>
/// </summary>
public class StorageLocation : TenantOwnedEntity
{
    /// <summary>Human-readable location code/label (e.g. "ST-14", "Loft A-3"). Required; unique within the community.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// The unit this location is currently assigned to, or null when unassigned (Req 19.4). Set/
    /// cleared by the Assign operation; the assigned unit must belong to the same community.
    /// </summary>
    public Guid? AssignedUnitId { get; set; }
}
