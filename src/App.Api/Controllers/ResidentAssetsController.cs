using System.ComponentModel.DataAnnotations;
using App.Api.Security;
using App.Application.Residents;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
public sealed class ResidentAssetsController(IResidentAssetsService assets) : ControllerBase
{
    [HttpPost("api/v1/vehicles")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "Vehicle")]
    public async Task<IActionResult> RegisterVehicle([FromBody] RegisterVehicleBody body, CancellationToken ct)
    {
        var dto = await assets.RegisterVehicleAsync(
            new RegisterVehicleRequest(body.UnitId, body.RegistrationNumber!, body.VehicleType!), ct);
        return Ok(ApiResult<VehicleDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/pets")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "Pet")]
    public async Task<IActionResult> RegisterPet([FromBody] RegisterPetBody body, CancellationToken ct)
    {
        var dto = await assets.RegisterPetAsync(new RegisterPetRequest(body.UnitId, body.PetType!, body.Name), ct);
        return Ok(ApiResult<PetDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/resident-parking-slots")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "ParkingSlot")]
    public async Task<IActionResult> CreateParkingSlot([FromBody] CreateParkingSlotBody body, CancellationToken ct)
    {
        var dto = await assets.CreateParkingSlotAsync(new CreateParkingSlotRequest(body.CommunityId, body.Code!), ct);
        return Ok(ApiResult<ParkingSlotDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/resident-parking-slots/{id:guid}/assignment")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Assign, "ParkingSlot")]
    public async Task<IActionResult> AssignParkingSlot(Guid id, [FromBody] AssignToUnitBody body, CancellationToken ct)
    {
        var dto = await assets.AssignParkingSlotAsync(id, new AssignToUnitRequest(body.UnitId), ct);
        return Ok(ApiResult<ParkingSlotDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/storage-locations")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "StorageLocation")]
    public async Task<IActionResult> CreateStorageLocation([FromBody] CreateStorageLocationBody body, CancellationToken ct)
    {
        var dto = await assets.CreateStorageLocationAsync(new CreateStorageLocationRequest(body.CommunityId, body.Code!), ct);
        return Ok(ApiResult<StorageLocationDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/storage-locations/{id:guid}/assignment")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Assign, "StorageLocation")]
    public async Task<IActionResult> AssignStorageLocation(Guid id, [FromBody] AssignToUnitBody body, CancellationToken ct)
    {
        var dto = await assets.AssignStorageLocationAsync(id, new AssignToUnitRequest(body.UnitId), ct);
        return Ok(ApiResult<StorageLocationDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/units/{unitId:guid}/vehicles")]
    [RequirePermission(PermissionAction.View, "Vehicle")]
    public async Task<IActionResult> ListVehicles(Guid unitId, CancellationToken ct)
    {
        var data = await assets.ListVehiclesForUnitAsync(unitId, ct);
        return Ok(ApiResult<IReadOnlyList<VehicleDto>>.Ok(data, string.Empty));
    }

    [HttpGet("api/v1/units/{unitId:guid}/pets")]
    [RequirePermission(PermissionAction.View, "Pet")]
    public async Task<IActionResult> ListPets(Guid unitId, CancellationToken ct)
    {
        var data = await assets.ListPetsForUnitAsync(unitId, ct);
        return Ok(ApiResult<IReadOnlyList<PetDto>>.Ok(data, string.Empty));
    }

    [HttpGet("api/v1/units/{unitId:guid}/parking-slots")]
    [RequirePermission(PermissionAction.View, "ParkingSlot")]
    public async Task<IActionResult> ListParkingSlots(Guid unitId, CancellationToken ct)
    {
        var data = await assets.ListParkingSlotsForUnitAsync(unitId, ct);
        return Ok(ApiResult<IReadOnlyList<ParkingSlotDto>>.Ok(data, string.Empty));
    }

    [HttpGet("api/v1/units/{unitId:guid}/storage-locations")]
    [RequirePermission(PermissionAction.View, "StorageLocation")]
    public async Task<IActionResult> ListStorageLocations(Guid unitId, CancellationToken ct)
    {
        var data = await assets.ListStorageLocationsForUnitAsync(unitId, ct);
        return Ok(ApiResult<IReadOnlyList<StorageLocationDto>>.Ok(data, string.Empty));
    }

    public sealed class RegisterVehicleBody
    {
        [Required(ErrorMessage = "unitId is required")]
        public Guid UnitId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "registrationNumber is required")]
        [MaxLength(100)]
        public string? RegistrationNumber { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "vehicleType is required")]
        [MaxLength(100)]
        public string? VehicleType { get; init; }
    }

    public sealed class RegisterPetBody
    {
        [Required(ErrorMessage = "unitId is required")]
        public Guid UnitId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "petType is required")]
        [MaxLength(100)]
        public string? PetType { get; init; }

        [MaxLength(100)]
        public string? Name { get; init; }
    }

    public sealed class CreateParkingSlotBody
    {
        [Required(ErrorMessage = "communityId is required")]
        public Guid CommunityId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "code is required")]
        [MaxLength(100)]
        public string? Code { get; init; }
    }

    public sealed class CreateStorageLocationBody
    {
        [Required(ErrorMessage = "communityId is required")]
        public Guid CommunityId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "code is required")]
        [MaxLength(100)]
        public string? Code { get; init; }
    }

    public sealed class AssignToUnitBody
    {
        public Guid? UnitId { get; init; }
    }
}
