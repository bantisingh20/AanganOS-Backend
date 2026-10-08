using System.ComponentModel.DataAnnotations;
using CommunityOS.Api.Security;
using CommunityOS.Api.Validation;
using CommunityOS.Application.Exceptions;
using CommunityOS.Application.Parking;
using CommunityOS.Contracts.Envelope;
using CommunityOS.Domain.Identity;

namespace CommunityOS.Api.Endpoints;

/// <summary>
/// HTTP surface over the Parking module (<see cref="IParkingService"/>) — the layout (zones + slots),
/// assignment with the one-active-per-slot guarantee, violations, the occupancy dashboard (Req 40.x),
/// guest parking for approved visitors (Req 41.1) and EV-charging reservations with the one-active-
/// per-EV-slot guarantee (Req 41.2, 41.3). Mirrors the established minimal-API style of
/// <see cref="ParcelEndpoints"/>: the <see cref="ApiResult{T}"/> envelope, a
/// <see cref="RequirePermissionAttribute"/> on the correct (Action, ResourceType), the
/// <see cref="DataAnnotationsValidationFilter"/> on write bodies and the "write" rate-limit policy on
/// every mutation. Reads are tenant-scoped by the EF global query filter; the service re-checks each
/// permission as defense-in-depth. Task 24.3.
/// </summary>
public static class ParkingEndpoints
{
    public static IEndpointRouteBuilder MapParkingEndpoints(this IEndpointRouteBuilder app)
    {
        MapZoneEndpoints(app);
        MapSlotEndpoints(app);
        MapAssignmentEndpoints(app);
        MapViolationEndpoints(app);
        MapGuestAndEvEndpoints(app);
        return app;
    }

    // --- Zones (/api/v1/parking-zones) — Req 40.1 ------------------------------------------------
    private static void MapZoneEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/parking-zones");

        group.MapPost("/", async (CreateZoneBody body, IParkingService parking, CancellationToken ct) =>
            {
                var dto = await parking.CreateZoneAsync(new CreateParkingZoneRequest(body.CommunityId, body.Code!, body.Name!, body.ZoneType!, body.Level), ct);
                return Results.Ok(ApiResult<ParkingZoneDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "ParkingZone"));

        group.MapGet("/", async (Guid communityId, IParkingService parking, CancellationToken ct) =>
            {
                var items = await parking.ListZonesAsync(communityId, ct);
                return Results.Ok(ApiResult<IReadOnlyList<ParkingZoneDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "ParkingZone"));
    }

    // --- Slots (/api/v1/parking-slots) + occupancy — Req 40.1, 40.5 ------------------------------
    private static void MapSlotEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/parking-slots");

        group.MapPost("/", async (CreateSlotBody body, IParkingService parking, CancellationToken ct) =>
            {
                var dto = await parking.CreateSlotAsync(new CreateParkingSlotLayoutRequest(
                    body.CommunityId, body.Code!, body.ZoneId, body.Level, body.SlotNumber, body.SlotType!), ct);
                return Results.Ok(ApiResult<ParkingSlotDetailDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "ParkingSlot"));

        group.MapGet("/", async (Guid communityId, IParkingService parking, CancellationToken ct) =>
            {
                var items = await parking.ListSlotsAsync(communityId, ct);
                return Results.Ok(ApiResult<IReadOnlyList<ParkingSlotDetailDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "ParkingSlot"));

        group.MapGet("/occupancy", async (Guid communityId, IParkingService parking, CancellationToken ct) =>
            {
                var dto = await parking.GetOccupancyAsync(communityId, ct);
                return Results.Ok(ApiResult<ParkingOccupancyDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "ParkingSlot"));

        group.MapGet("/{id:guid}", async (Guid id, IParkingService parking, CancellationToken ct) =>
            {
                var dto = await parking.GetSlotAsync(id, ct) ?? throw new NotFoundException("Parking slot not found.");
                return Results.Ok(ApiResult<ParkingSlotDetailDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "ParkingSlot"));

        group.MapPut("/{id:guid}", async (Guid id, UpdateSlotBody body, IParkingService parking, CancellationToken ct) =>
            {
                var dto = await parking.UpdateSlotAsync(id, new UpdateParkingSlotLayoutRequest(
                    body.ZoneId, body.ClearZone, body.Level, body.SlotNumber, body.SlotType, body.IsActive), ct);
                return Results.Ok(ApiResult<ParkingSlotDetailDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "ParkingSlot"));

        group.MapGet("/{id:guid}/assignments", async (Guid id, IParkingService parking, CancellationToken ct) =>
            {
                var items = await parking.ListAssignmentsForSlotAsync(id, ct);
                return Results.Ok(ApiResult<IReadOnlyList<ParkingAssignmentDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "ParkingAssignment"));

        group.MapGet("/{id:guid}/violations", async (Guid id, IParkingService parking, CancellationToken ct) =>
            {
                var items = await parking.ListViolationsForSlotAsync(id, ct);
                return Results.Ok(ApiResult<IReadOnlyList<ParkingViolationDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "ParkingViolation"));

        group.MapGet("/{id:guid}/ev-reservations", async (Guid id, IParkingService parking, CancellationToken ct) =>
            {
                var items = await parking.ListEvReservationsForSlotAsync(id, ct);
                return Results.Ok(ApiResult<IReadOnlyList<EvReservationDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "EvReservation"));
    }

    // --- Assignments (/api/v1/parking-assignments) — Req 40.2, 40.3 ------------------------------
    private static void MapAssignmentEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/parking-assignments");

        group.MapPost("/", async (AssignSlotBody body, IParkingService parking, CancellationToken ct) =>
            {
                var dto = await parking.AssignSlotAsync(new AssignParkingSlotRequest(body.ParkingSlotId, body.UnitId, body.VisitorId, body.AssignmentType!), ct);
                return Results.Ok(ApiResult<ParkingAssignmentDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Assign, "ParkingAssignment"));

        group.MapPost("/release", async (ReleaseSlotBody body, IParkingService parking, CancellationToken ct) =>
            {
                await parking.ReleaseSlotAsync(body.ParkingSlotId, ct);
                return Results.Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(body.ParkingSlotId), string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Assign, "ParkingAssignment"));
    }

    // --- Violations (/api/v1/parking-violations) — Req 40.4 --------------------------------------
    private static void MapViolationEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/parking-violations");

        group.MapPost("/", async (RecordViolationBody body, IParkingService parking, CancellationToken ct) =>
            {
                var dto = await parking.RecordViolationAsync(new RecordParkingViolationRequest(
                    body.ParkingSlotId, body.VehicleId, body.ViolationType!, body.Description!, body.EvidenceFileId), ct);
                return Results.Ok(ApiResult<ParkingViolationDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "ParkingViolation"));

        group.MapPost("/{id:guid}/status", async (Guid id, UpdateViolationStatusBody body, IParkingService parking, CancellationToken ct) =>
            {
                var dto = await parking.UpdateViolationStatusAsync(id, new UpdateParkingViolationStatusRequest(body.Status!), ct);
                return Results.Ok(ApiResult<ParkingViolationDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "ParkingViolation"));
    }

    // --- Guest parking + EV reservations (Req 41.1, 41.2, 41.3) ----------------------------------
    private static void MapGuestAndEvEndpoints(IEndpointRouteBuilder app)
    {
        // Guest parking is a visitor-type assignment, so it reuses the ParkingAssignment Assign grant.
        app.MapPost("/api/v1/guest-parking", async (GuestParkingBody body, IParkingService parking, CancellationToken ct) =>
            {
                var dto = await parking.RequestGuestParkingAsync(new RequestGuestParkingRequest(
                    body.ParkingSlotId, body.VisitorId, body.RequestedByResidentId, body.UnitId), ct);
                return Results.Ok(ApiResult<ParkingAssignmentDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Assign, "ParkingAssignment"));

        var ev = app.MapGroup("/api/v1/ev-reservations");

        ev.MapPost("/", async (ReserveEvBody body, IParkingService parking, CancellationToken ct) =>
            {
                var dto = await parking.ReserveEvSlotAsync(new ReserveEvSlotRequest(
                    body.ParkingSlotId, body.ResidentId, body.UnitId, body.ReservedFromUtc, body.ReservedUntilUtc), ct);
                return Results.Ok(ApiResult<EvReservationDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "EvReservation"));

        ev.MapPost("/{id:guid}/close", async (Guid id, CloseEvBody body, IParkingService parking, CancellationToken ct) =>
            {
                var dto = await parking.CloseEvReservationAsync(id, new CloseEvReservationRequest(body.Status!, body.EnergyKwh), ct);
                return Results.Ok(ApiResult<EvReservationDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "EvReservation"));
    }

    // --- Request bodies (validated via DataAnnotationsValidationFilter) --------------------------

    public sealed class CreateZoneBody
    {
        [Required] public Guid CommunityId { get; init; }
        [Required(AllowEmptyStrings = false)] [MaxLength(50)] public string? Code { get; init; }
        [Required(AllowEmptyStrings = false)] [MaxLength(200)] public string? Name { get; init; }
        [Required(AllowEmptyStrings = false)] [MaxLength(100)] public string? ZoneType { get; init; }
        [MaxLength(50)] public string? Level { get; init; }
    }

    public sealed class CreateSlotBody
    {
        [Required] public Guid CommunityId { get; init; }
        [Required(AllowEmptyStrings = false)] [MaxLength(50)] public string? Code { get; init; }
        public Guid? ZoneId { get; init; }
        [MaxLength(50)] public string? Level { get; init; }
        [MaxLength(50)] public string? SlotNumber { get; init; }
        [Required(AllowEmptyStrings = false)] [MaxLength(100)] public string? SlotType { get; init; }
    }

    public sealed class UpdateSlotBody
    {
        public Guid? ZoneId { get; init; }
        public bool ClearZone { get; init; }
        [MaxLength(50)] public string? Level { get; init; }
        [MaxLength(50)] public string? SlotNumber { get; init; }
        [MaxLength(100)] public string? SlotType { get; init; }
        public bool? IsActive { get; init; }
    }

    public sealed class AssignSlotBody
    {
        [Required] public Guid ParkingSlotId { get; init; }
        public Guid? UnitId { get; init; }
        public Guid? VisitorId { get; init; }
        [Required(AllowEmptyStrings = false)] [MaxLength(100)] public string? AssignmentType { get; init; }
    }

    public sealed class ReleaseSlotBody
    {
        [Required] public Guid ParkingSlotId { get; init; }
    }

    public sealed class RecordViolationBody
    {
        [Required] public Guid ParkingSlotId { get; init; }
        public Guid? VehicleId { get; init; }
        [Required(AllowEmptyStrings = false)] [MaxLength(100)] public string? ViolationType { get; init; }
        [Required(AllowEmptyStrings = false)] [MaxLength(1000)] public string? Description { get; init; }
        public Guid? EvidenceFileId { get; init; }
    }

    public sealed class UpdateViolationStatusBody
    {
        [Required(AllowEmptyStrings = false)] [MaxLength(100)] public string? Status { get; init; }
    }

    public sealed class GuestParkingBody
    {
        [Required] public Guid ParkingSlotId { get; init; }
        [Required] public Guid VisitorId { get; init; }
        [Required] public Guid RequestedByResidentId { get; init; }
        [Required] public Guid UnitId { get; init; }
    }

    public sealed class ReserveEvBody
    {
        [Required] public Guid ParkingSlotId { get; init; }
        [Required] public Guid ResidentId { get; init; }
        [Required] public Guid UnitId { get; init; }
        [Required] public DateTime ReservedFromUtc { get; init; }
        [Required] public DateTime ReservedUntilUtc { get; init; }
    }

    public sealed class CloseEvBody
    {
        [Required(AllowEmptyStrings = false)] [MaxLength(100)] public string? Status { get; init; }
        [Range(0, 9999999)] public decimal? EnergyKwh { get; init; }
    }
}
