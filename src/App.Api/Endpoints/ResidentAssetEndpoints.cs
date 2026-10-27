using System.ComponentModel.DataAnnotations;
using App.Api.Security;
using App.Api.Validation;
using App.Application.Residents;
using App.Contracts.Envelope;
using App.Domain.Identity;

namespace App.Api.Endpoints;

/// <summary>
/// HTTP surface over <see cref="IResidentAssetsService"/> (Task 12.5) — resident household assets:
/// vehicles, pets, parking slots and storage locations (Req 19.1–19.5). Every endpoint uses the
/// <see cref="ApiResult{T}"/> envelope (Req 9.1), carries a <see cref="RequirePermissionAttribute"/>
/// on the correct (Action, ResourceType) (Req 4.1), validates write bodies (Req 9.2), and applies
/// the "write" rate-limit policy to mutations (Req 65.5). The per-unit lists (Req 19.5) are
/// tenant-scoped — and resident self-scoped for Vehicle/Pet — by the global query filter.
///
/// <para>Creating a parking slot / storage location uses <c>Create</c>; associating one with a unit
/// uses <c>Assign</c> (Req 19.4), matching the service's defense-in-depth permission check. Duplicate
/// vehicle registration / slot code within a community → CONFLICT; bad type / cross-community unit
/// → VALIDATION_ERROR — surfaced by the service.</para>
/// </summary>
public static class ResidentAssetEndpoints
{
    public static IEndpointRouteBuilder MapResidentAssetEndpoints(this IEndpointRouteBuilder app)
    {
        // --- Vehicles (Req 19.1, 19.2) ---
        var vehicles = app.MapGroup("/api/v1/vehicles");

        vehicles.MapPost("/", async (
                RegisterVehicleBody body,
                IResidentAssetsService assets,
                CancellationToken ct) =>
            {
                var dto = await assets.RegisterVehicleAsync(
                    new RegisterVehicleRequest(body.UnitId, body.RegistrationNumber!, body.VehicleType!), ct);
                return Results.Ok(ApiResult<VehicleDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "Vehicle"));

        // --- Pets (Req 19.3) ---
        var pets = app.MapGroup("/api/v1/pets");

        pets.MapPost("/", async (
                RegisterPetBody body,
                IResidentAssetsService assets,
                CancellationToken ct) =>
            {
                var dto = await assets.RegisterPetAsync(
                    new RegisterPetRequest(body.UnitId, body.PetType!, body.Name), ct);
                return Results.Ok(ApiResult<PetDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "Pet"));

        // --- Parking slots (Req 19.4): create (master row) + assign to a unit ---
        var parking = app.MapGroup("/api/v1/parking-slots");

        parking.MapPost("/", async (
                CreateParkingSlotBody body,
                IResidentAssetsService assets,
                CancellationToken ct) =>
            {
                var dto = await assets.CreateParkingSlotAsync(
                    new CreateParkingSlotRequest(body.CommunityId, body.Code!), ct);
                return Results.Ok(ApiResult<ParkingSlotDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "ParkingSlot"));

        parking.MapPost("/{id:guid}/assignment", async (
                Guid id,
                AssignToUnitBody body,
                IResidentAssetsService assets,
                CancellationToken ct) =>
            {
                var dto = await assets.AssignParkingSlotAsync(id, new AssignToUnitRequest(body.UnitId), ct);
                return Results.Ok(ApiResult<ParkingSlotDto>.Ok(dto, string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Assign, "ParkingSlot"));

        // --- Storage locations (Req 19.4): create (master row) + assign to a unit ---
        var storage = app.MapGroup("/api/v1/storage-locations");

        storage.MapPost("/", async (
                CreateStorageLocationBody body,
                IResidentAssetsService assets,
                CancellationToken ct) =>
            {
                var dto = await assets.CreateStorageLocationAsync(
                    new CreateStorageLocationRequest(body.CommunityId, body.Code!), ct);
                return Results.Ok(ApiResult<StorageLocationDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "StorageLocation"));

        storage.MapPost("/{id:guid}/assignment", async (
                Guid id,
                AssignToUnitBody body,
                IResidentAssetsService assets,
                CancellationToken ct) =>
            {
                var dto = await assets.AssignStorageLocationAsync(id, new AssignToUnitRequest(body.UnitId), ct);
                return Results.Ok(ApiResult<StorageLocationDto>.Ok(dto, string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Assign, "StorageLocation"));

        // --- Per-unit asset reads (Req 19.5) ---
        var units = app.MapGroup("/api/v1/units/{unitId:guid}");

        units.MapGet("/vehicles", async (
                Guid unitId,
                IResidentAssetsService assets,
                CancellationToken ct) =>
            {
                var data = await assets.ListVehiclesForUnitAsync(unitId, ct);
                return Results.Ok(ApiResult<IReadOnlyList<VehicleDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Vehicle"));

        units.MapGet("/pets", async (
                Guid unitId,
                IResidentAssetsService assets,
                CancellationToken ct) =>
            {
                var data = await assets.ListPetsForUnitAsync(unitId, ct);
                return Results.Ok(ApiResult<IReadOnlyList<PetDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Pet"));

        units.MapGet("/parking-slots", async (
                Guid unitId,
                IResidentAssetsService assets,
                CancellationToken ct) =>
            {
                var data = await assets.ListParkingSlotsForUnitAsync(unitId, ct);
                return Results.Ok(ApiResult<IReadOnlyList<ParkingSlotDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "ParkingSlot"));

        units.MapGet("/storage-locations", async (
                Guid unitId,
                IResidentAssetsService assets,
                CancellationToken ct) =>
            {
                var data = await assets.ListStorageLocationsForUnitAsync(unitId, ct);
                return Results.Ok(ApiResult<IReadOnlyList<StorageLocationDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "StorageLocation"));

        return app;
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
        // Null clears the assignment (Req 19.4); otherwise the unit must be in the slot's community.
        public Guid? UnitId { get; init; }
    }
}
