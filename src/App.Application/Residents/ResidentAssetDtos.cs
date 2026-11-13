namespace App.Application.Residents;

/// <summary>Read shape for a registered <c>Vehicle</c> (Req 19.1).</summary>
/// <param name="Id">The vehicle id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="UnitId">The unit the vehicle is registered to.</param>
/// <param name="RegistrationNumber">Registration/number plate (unique within the community, Req 19.2).</param>
/// <param name="VehicleType">Configurable vehicle-type code (Req 19.1).</param>
public sealed record VehicleDto(
    Guid Id,
    Guid CommunityId,
    Guid UnitId,
    string RegistrationNumber,
    string VehicleType);

/// <summary>Read shape for a registered <c>Pet</c> (Req 19.3).</summary>
/// <param name="Id">The pet id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="UnitId">The unit the pet is registered to.</param>
/// <param name="PetType">Configurable pet-type code (Req 19.3).</param>
/// <param name="Name">Optional pet name, or null.</param>
public sealed record PetDto(
    Guid Id,
    Guid CommunityId,
    Guid UnitId,
    string PetType,
    string? Name);

/// <summary>Read shape for a <c>ParkingSlot</c> and its optional unit assignment (Req 19.4).</summary>
/// <param name="Id">The slot id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="Code">Slot code/label.</param>
/// <param name="AssignedUnitId">The assigned unit, or null when unassigned.</param>
public sealed record ParkingSlotDto(
    Guid Id,
    Guid CommunityId,
    string Code,
    Guid? AssignedUnitId);

/// <summary>Read shape for a <c>StorageLocation</c> and its optional unit assignment (Req 19.4).</summary>
/// <param name="Id">The storage location id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="Code">Location code/label.</param>
/// <param name="AssignedUnitId">The assigned unit, or null when unassigned.</param>
public sealed record StorageLocationDto(
    Guid Id,
    Guid CommunityId,
    string Code,
    Guid? AssignedUnitId);

/// <summary>
/// Register a vehicle for a unit (Req 19.1). <paramref name="VehicleType"/> is validated against the
/// configurable <c>Vehicle_Type</c> list; a duplicate <paramref name="RegistrationNumber"/> within
/// the same community is rejected with CONFLICT via the DB unique index (Req 19.2).
/// </summary>
/// <param name="UnitId">The unit the vehicle belongs to. Required; must be in <paramref name="CommunityId"/>.</param>
/// <param name="RegistrationNumber">Registration/number plate. Required.</param>
/// <param name="VehicleType">Vehicle-type code from the Vehicle_Type list. Required.</param>
public sealed record RegisterVehicleRequest(
    Guid UnitId,
    string RegistrationNumber,
    string VehicleType);

/// <summary>
/// Register a pet for a unit (Req 19.3). <paramref name="PetType"/> is validated against the
/// configurable <c>Pet_Type</c> list.
/// </summary>
/// <param name="UnitId">The unit the pet belongs to. Required; must be in <paramref name="CommunityId"/>.</param>
/// <param name="PetType">Pet-type code from the Pet_Type list. Required.</param>
/// <param name="Name">Optional pet name.</param>
public sealed record RegisterPetRequest(
    Guid UnitId,
    string PetType,
    string? Name);

/// <summary>
/// Create a parking slot master row for a community (Req 19.4). The slot starts unassigned; use
/// <c>AssignParkingSlotAsync</c> to associate it with a unit.
/// </summary>
/// <param name="CommunityId">Owning community. Required.</param>
/// <param name="Code">Slot code/label. Required; unique within the community.</param>
public sealed record CreateParkingSlotRequest(
    Guid CommunityId,
    string Code);

/// <summary>
/// Create a storage location master row for a community (Req 19.4). Starts unassigned.
/// </summary>
/// <param name="CommunityId">Owning community. Required.</param>
/// <param name="Code">Location code/label. Required; unique within the community.</param>
public sealed record CreateStorageLocationRequest(
    Guid CommunityId,
    string Code);

/// <summary>
/// Assign (or clear) the unit a parking slot or storage location belongs to (Req 19.4). When
/// <paramref name="UnitId"/> is null the association is cleared; otherwise the unit must belong to
/// the same community as the slot/location.
/// </summary>
/// <param name="UnitId">The unit to associate, or null to clear the assignment.</param>
public sealed record AssignToUnitRequest(Guid? UnitId);
