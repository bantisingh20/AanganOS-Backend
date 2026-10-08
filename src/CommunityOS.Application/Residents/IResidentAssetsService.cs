namespace CommunityOS.Application.Residents;

/// <summary>
/// Resident household assets (Task 12.5, Req 19.1–19.5): register vehicles and pets against a unit,
/// create parking slots / storage locations and assign them to a unit, and read a unit's
/// vehicles / pets / parking / storage.
///
/// <para><b>Configurable types (Req 19.1, 19.3, steering: no hardcoding).</b> Vehicle and pet types
/// are validated against the editable <c>Vehicle_Type</c> / <c>Pet_Type</c> master-data lists
/// resolved at runtime — not frozen enums.</para>
///
/// <para><b>Concurrency-safe vehicle uniqueness (Req 19.2).</b> A duplicate registration number
/// within a community is NOT pre-checked with a read-then-write; the DB unique index
/// <c>(CommunityId, RegistrationNumber)</c> is the single source of truth and the race loser's
/// violation maps to CONFLICT.</para>
///
/// <para><b>Assign gating (Req 19.4).</b> Associating a parking slot or storage location with a unit
/// requires the <c>Assign</c> permission (defense-in-depth in the service) and a same-community
/// unit. The full parking module (layout, violations, EV, one-active-assignment-per-slot) is
/// Phase 6 (Task 24); this is a minimal reassignable association.</para>
///
/// <para><b>Tenant-scoped reads (Req 19.5).</b> Vehicle/Pet are <c>IUnitScoped</c>, so a resident
/// principal only sees their own unit's records via the DbContext self-scope filter; all reads are
/// bounded to the caller's authorized communities by the global tenant filter and use
/// <c>AsNoTracking</c>. Mutations are audited.</para>
/// </summary>
public interface IResidentAssetsService
{
    /// <summary>
    /// Register a vehicle for a unit (Req 19.1). Validates the type against <c>Vehicle_Type</c> and
    /// that the unit is in the same community. A duplicate registration number within the community
    /// → CONFLICT (Req 19.2). Returns the created vehicle.
    /// </summary>
    Task<VehicleDto> RegisterVehicleAsync(RegisterVehicleRequest request, CancellationToken cancellationToken = default);

    /// <summary>Register a pet for a unit (Req 19.3). Validates the type against <c>Pet_Type</c> and the same-community unit. Returns the created pet.</summary>
    Task<PetDto> RegisterPetAsync(RegisterPetRequest request, CancellationToken cancellationToken = default);

    /// <summary>Create a parking slot master row (Req 19.4), initially unassigned. A duplicate code within the community → CONFLICT.</summary>
    Task<ParkingSlotDto> CreateParkingSlotAsync(CreateParkingSlotRequest request, CancellationToken cancellationToken = default);

    /// <summary>Create a storage location master row (Req 19.4), initially unassigned. A duplicate code within the community → CONFLICT.</summary>
    Task<StorageLocationDto> CreateStorageLocationAsync(CreateStorageLocationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assign a parking slot to a unit, or clear its assignment when the request's unit is null
    /// (Req 19.4). Requires the <c>Assign</c> permission + authorized community (defense-in-depth);
    /// the unit must be in the slot's community. Unknown slot → not found. Returns the updated slot.
    /// </summary>
    Task<ParkingSlotDto> AssignParkingSlotAsync(Guid parkingSlotId, AssignToUnitRequest request, CancellationToken cancellationToken = default);

    /// <summary>Assign a storage location to a unit, or clear it when null (Req 19.4). Same <c>Assign</c>-permission + same-community rules as <see cref="AssignParkingSlotAsync"/>.</summary>
    Task<StorageLocationDto> AssignStorageLocationAsync(Guid storageLocationId, AssignToUnitRequest request, CancellationToken cancellationToken = default);

    /// <summary>List a unit's registered vehicles (Req 19.5), tenant-scoped and resident self-scoped. Unknown/foreign-tenant unit → empty list.</summary>
    Task<IReadOnlyList<VehicleDto>> ListVehiclesForUnitAsync(Guid unitId, CancellationToken cancellationToken = default);

    /// <summary>List a unit's registered pets (Req 19.5), tenant-scoped and resident self-scoped.</summary>
    Task<IReadOnlyList<PetDto>> ListPetsForUnitAsync(Guid unitId, CancellationToken cancellationToken = default);

    /// <summary>List the parking slots assigned to a unit (Req 19.4, 19.5), tenant-scoped.</summary>
    Task<IReadOnlyList<ParkingSlotDto>> ListParkingSlotsForUnitAsync(Guid unitId, CancellationToken cancellationToken = default);

    /// <summary>List the storage locations assigned to a unit (Req 19.4, 19.5), tenant-scoped.</summary>
    Task<IReadOnlyList<StorageLocationDto>> ListStorageLocationsForUnitAsync(Guid unitId, CancellationToken cancellationToken = default);
}
