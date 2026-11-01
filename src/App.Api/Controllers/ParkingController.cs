using System.ComponentModel.DataAnnotations;
using App.Api.Endpoints;
using App.Api.Security;
using App.Application.Exceptions;
using App.Application.Parking;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
public sealed class ParkingController(IParkingService parking) : ControllerBase
{
    // --- Zones ---
    [HttpPost("api/v1/parking-zones")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "ParkingZone")]
    public async Task<IActionResult> CreateZone([FromBody] CreateZoneBody body, CancellationToken ct)
    {
        var dto = await parking.CreateZoneAsync(new CreateParkingZoneRequest(body.CommunityId, body.Code!, body.Name!, body.ZoneType!, body.Level), ct);
        return Ok(ApiResult<ParkingZoneDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/parking-zones")]
    [RequirePermission(PermissionAction.View, "ParkingZone")]
    public async Task<IActionResult> ListZones([FromQuery] Guid communityId, CancellationToken ct)
    {
        var items = await parking.ListZonesAsync(communityId, ct);
        return Ok(ApiResult<IReadOnlyList<ParkingZoneDto>>.Ok(items, string.Empty));
    }

    // --- Slots ---
    [HttpPost("api/v1/parking-slots")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "ParkingSlot")]
    public async Task<IActionResult> CreateSlot([FromBody] CreateSlotBody body, CancellationToken ct)
    {
        var dto = await parking.CreateSlotAsync(new CreateParkingSlotLayoutRequest(
            body.CommunityId, body.Code!, body.ZoneId, body.Level, body.SlotNumber, body.SlotType!), ct);
        return Ok(ApiResult<ParkingSlotDetailDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/parking-slots")]
    [RequirePermission(PermissionAction.View, "ParkingSlot")]
    public async Task<IActionResult> ListSlots([FromQuery] Guid communityId, CancellationToken ct)
    {
        var items = await parking.ListSlotsAsync(communityId, ct);
        return Ok(ApiResult<IReadOnlyList<ParkingSlotDetailDto>>.Ok(items, string.Empty));
    }

    [HttpGet("api/v1/parking-slots/occupancy")]
    [RequirePermission(PermissionAction.View, "ParkingSlot")]
    public async Task<IActionResult> Occupancy([FromQuery] Guid communityId, CancellationToken ct)
    {
        var dto = await parking.GetOccupancyAsync(communityId, ct);
        return Ok(ApiResult<ParkingOccupancyDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/parking-slots/{id:guid}")]
    [RequirePermission(PermissionAction.View, "ParkingSlot")]
    public async Task<IActionResult> GetSlot(Guid id, CancellationToken ct)
    {
        var dto = await parking.GetSlotAsync(id, ct) ?? throw new NotFoundException("Parking slot not found.");
        return Ok(ApiResult<ParkingSlotDetailDto>.Ok(dto, string.Empty));
    }

    [HttpPut("api/v1/parking-slots/{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "ParkingSlot")]
    public async Task<IActionResult> UpdateSlot(Guid id, [FromBody] UpdateSlotBody body, CancellationToken ct)
    {
        var dto = await parking.UpdateSlotAsync(id, new UpdateParkingSlotLayoutRequest(
            body.ZoneId, body.ClearZone, body.Level, body.SlotNumber, body.SlotType, body.IsActive), ct);
        return Ok(ApiResult<ParkingSlotDetailDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/parking-slots/{id:guid}/assignments")]
    [RequirePermission(PermissionAction.View, "ParkingAssignment")]
    public async Task<IActionResult> SlotAssignments(Guid id, CancellationToken ct)
    {
        var items = await parking.ListAssignmentsForSlotAsync(id, ct);
        return Ok(ApiResult<IReadOnlyList<ParkingAssignmentDto>>.Ok(items, string.Empty));
    }

    [HttpGet("api/v1/parking-slots/{id:guid}/violations")]
    [RequirePermission(PermissionAction.View, "ParkingViolation")]
    public async Task<IActionResult> SlotViolations(Guid id, CancellationToken ct)
    {
        var items = await parking.ListViolationsForSlotAsync(id, ct);
        return Ok(ApiResult<IReadOnlyList<ParkingViolationDto>>.Ok(items, string.Empty));
    }

    [HttpGet("api/v1/parking-slots/{id:guid}/ev-reservations")]
    [RequirePermission(PermissionAction.View, "EvReservation")]
    public async Task<IActionResult> SlotEvReservations(Guid id, CancellationToken ct)
    {
        var items = await parking.ListEvReservationsForSlotAsync(id, ct);
        return Ok(ApiResult<IReadOnlyList<EvReservationDto>>.Ok(items, string.Empty));
    }

    // --- Assignments ---
    [HttpPost("api/v1/parking-assignments")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Assign, "ParkingAssignment")]
    public async Task<IActionResult> AssignSlot([FromBody] AssignSlotBody body, CancellationToken ct)
    {
        var dto = await parking.AssignSlotAsync(new AssignParkingSlotRequest(body.ParkingSlotId, body.UnitId, body.VisitorId, body.AssignmentType!), ct);
        return Ok(ApiResult<ParkingAssignmentDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/parking-assignments/release")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Assign, "ParkingAssignment")]
    public async Task<IActionResult> ReleaseSlot([FromBody] ReleaseSlotBody body, CancellationToken ct)
    {
        await parking.ReleaseSlotAsync(body.ParkingSlotId, ct);
        return Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(body.ParkingSlotId), string.Empty));
    }

    // --- Violations ---
    [HttpPost("api/v1/parking-violations")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "ParkingViolation")]
    public async Task<IActionResult> RecordViolation([FromBody] RecordViolationBody body, CancellationToken ct)
    {
        var dto = await parking.RecordViolationAsync(new RecordParkingViolationRequest(
            body.ParkingSlotId, body.VehicleId, body.ViolationType!, body.Description!, body.EvidenceFileId), ct);
        return Ok(ApiResult<ParkingViolationDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/parking-violations/{id:guid}/status")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "ParkingViolation")]
    public async Task<IActionResult> UpdateViolationStatus(Guid id, [FromBody] UpdateViolationStatusBody body, CancellationToken ct)
    {
        var dto = await parking.UpdateViolationStatusAsync(id, new UpdateParkingViolationStatusRequest(body.Status!), ct);
        return Ok(ApiResult<ParkingViolationDto>.Ok(dto, string.Empty));
    }

    // --- Guest parking + EV reservations ---
    [HttpPost("api/v1/guest-parking")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Assign, "ParkingAssignment")]
    public async Task<IActionResult> GuestParking([FromBody] GuestParkingBody body, CancellationToken ct)
    {
        var dto = await parking.RequestGuestParkingAsync(new RequestGuestParkingRequest(
            body.ParkingSlotId, body.VisitorId, body.RequestedByResidentId, body.UnitId), ct);
        return Ok(ApiResult<ParkingAssignmentDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/ev-reservations")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "EvReservation")]
    public async Task<IActionResult> ReserveEv([FromBody] ReserveEvBody body, CancellationToken ct)
    {
        var dto = await parking.ReserveEvSlotAsync(new ReserveEvSlotRequest(
            body.ParkingSlotId, body.ResidentId, body.UnitId, body.ReservedFromUtc, body.ReservedUntilUtc), ct);
        return Ok(ApiResult<EvReservationDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/ev-reservations/{id:guid}/close")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "EvReservation")]
    public async Task<IActionResult> CloseEv(Guid id, [FromBody] CloseEvBody body, CancellationToken ct)
    {
        var dto = await parking.CloseEvReservationAsync(id, new CloseEvReservationRequest(body.Status!, body.EnergyKwh), ct);
        return Ok(ApiResult<EvReservationDto>.Ok(dto, string.Empty));
    }

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
