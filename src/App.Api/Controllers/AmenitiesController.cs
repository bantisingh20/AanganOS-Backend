using System.ComponentModel.DataAnnotations;
using App.Api.Endpoints;
using App.Api.Security;
using App.Application.Amenities;
using App.Application.Exceptions;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
public sealed class AmenitiesController(IAmenityService amenities) : ControllerBase
{
    // --- Amenities ---
    [HttpPost("api/v1/amenities")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "Amenity")]
    public async Task<IActionResult> CreateAmenity([FromBody] CreateAmenityBody body, CancellationToken ct)
    {
        var dto = await amenities.CreateAmenityAsync(new CreateAmenityRequest(
            body.CommunityId, body.Name!, body.Code!, body.AmenityType!, body.Location, body.Description, body.DefaultCapacity), ct);
        return Ok(ApiResult<AmenityDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/amenities")]
    [RequirePermission(PermissionAction.View, "Amenity")]
    public async Task<IActionResult> ListAmenities(
        [FromQuery] string? search, [FromQuery] string? sort, [FromQuery] string? amenityType, [FromQuery] string? status,
        [FromQuery] Guid? communityId, [FromQuery] Guid? organizationId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        AddEquals(filters, "amenityType", amenityType);
        AddEquals(filters, "status", status);
        AddEquals(filters, "communityId", communityId);
        var data = await amenities.ListAmenitiesAsync(BuildParameters(page, pageSize, search, sort, filters), organizationId, ct);
        return Ok(ApiResult<PagedData<AmenityDto>>.Ok(data, string.Empty));
    }

    [HttpGet("api/v1/amenities/{id:guid}")]
    [RequirePermission(PermissionAction.View, "Amenity")]
    public async Task<IActionResult> GetAmenity(Guid id, CancellationToken ct)
    {
        var dto = await amenities.GetAmenityAsync(id, ct) ?? throw new NotFoundException("Amenity not found.");
        return Ok(ApiResult<AmenityDto>.Ok(dto, string.Empty));
    }

    [HttpPut("api/v1/amenities/{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Amenity")]
    public async Task<IActionResult> UpdateAmenity(Guid id, [FromBody] UpdateAmenityBody body, CancellationToken ct)
    {
        var dto = await amenities.UpdateAmenityAsync(id, new UpdateAmenityRequest(
            body.Name, body.AmenityType, body.Status, body.Location, body.Description, body.DefaultCapacity), ct);
        return Ok(ApiResult<AmenityDto>.Ok(dto, string.Empty));
    }

    [HttpDelete("api/v1/amenities/{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Delete, "Amenity")]
    public async Task<IActionResult> DeleteAmenity(Guid id, CancellationToken ct)
    {
        await amenities.DeleteAmenityAsync(id, ct);
        return Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
    }

    // --- Facility slots ---
    [HttpPost("api/v1/amenities/{id:guid}/slots")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "FacilitySlot")]
    public async Task<IActionResult> CreateSlot(Guid id, [FromBody] CreateFacilitySlotBody body, CancellationToken ct)
    {
        var dto = await amenities.CreateFacilitySlotAsync(new CreateFacilitySlotRequest(
            id, body.Name, body.StartMinutesOfDay, body.EndMinutesOfDay, body.DaysOfWeekMask, body.Capacity), ct);
        return Ok(ApiResult<FacilitySlotDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/amenities/{id:guid}/slots")]
    [RequirePermission(PermissionAction.View, "FacilitySlot")]
    public async Task<IActionResult> ListSlots(Guid id, CancellationToken ct)
    {
        var items = await amenities.GetAmenitySlotsAsync(id, ct);
        return Ok(ApiResult<IReadOnlyList<FacilitySlotDto>>.Ok(items, string.Empty));
    }

    [HttpPut("api/v1/amenities/{id:guid}/booking-rule")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Amenity")]
    public async Task<IActionResult> SetBookingRule(Guid id, [FromBody] SetBookingRuleBody body, CancellationToken ct)
    {
        var dto = await amenities.SetBookingRuleAsync(new SetBookingRuleRequest(
            id, body.AllowedResidentTypes, body.AdvanceBookingWindowDays, body.MinDurationMinutes, body.MaxDurationMinutes,
            body.MaxBookingsPerResident, body.MaxBookingsPeriodDays, body.RequiresApproval, body.BookingCharge,
            body.SecurityDeposit, body.CancellationNoticeHours, body.RefundPercentOnCancellation), ct);
        return Ok(ApiResult<BookingRuleDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/amenities/{id:guid}/booking-rule")]
    [RequirePermission(PermissionAction.View, "Amenity")]
    public async Task<IActionResult> GetBookingRule(Guid id, CancellationToken ct)
    {
        var dto = await amenities.GetBookingRuleAsync(id, ct) ?? throw new NotFoundException("No booking rule configured for this amenity.");
        return Ok(ApiResult<BookingRuleDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/amenities/{id:guid}/blackout-dates")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "BlackoutDate")]
    public async Task<IActionResult> CreateBlackout(Guid id, [FromBody] CreateBlackoutDateBody body, CancellationToken ct)
    {
        var dto = await amenities.CreateBlackoutDateAsync(new CreateBlackoutDateRequest(id, body.StartUtc, body.EndUtc, body.Reason!), ct);
        return Ok(ApiResult<BlackoutDateDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/amenities/{id:guid}/blackout-dates")]
    [RequirePermission(PermissionAction.View, "BlackoutDate")]
    public async Task<IActionResult> ListBlackouts(Guid id, CancellationToken ct)
    {
        var items = await amenities.GetAmenityBlackoutDatesAsync(id, ct);
        return Ok(ApiResult<IReadOnlyList<BlackoutDateDto>>.Ok(items, string.Empty));
    }

    [HttpDelete("api/v1/facility-slots/{slotId:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Delete, "FacilitySlot")]
    public async Task<IActionResult> DeleteSlot(Guid slotId, CancellationToken ct)
    {
        await amenities.DeleteFacilitySlotAsync(slotId, ct);
        return Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(slotId), string.Empty));
    }

    [HttpDelete("api/v1/blackout-dates/{blackoutId:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Delete, "BlackoutDate")]
    public async Task<IActionResult> DeleteBlackout(Guid blackoutId, CancellationToken ct)
    {
        await amenities.DeleteBlackoutDateAsync(blackoutId, ct);
        return Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(blackoutId), string.Empty));
    }

    // --- Bookings ---
    [HttpPost("api/v1/bookings")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "Booking")]
    public async Task<IActionResult> CreateBooking([FromBody] CreateBookingBody body, CancellationToken ct)
    {
        var dto = await amenities.CreateBookingAsync(new CreateBookingRequest(body.FacilitySlotId, body.BookingDate, body.ResidentId, body.UnitId), ct);
        return Ok(ApiResult<BookingDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/bookings/{id:guid}")]
    [RequirePermission(PermissionAction.View, "Booking")]
    public async Task<IActionResult> GetBooking(Guid id, CancellationToken ct)
    {
        var dto = await amenities.GetBookingAsync(id, ct) ?? throw new NotFoundException("Booking not found.");
        return Ok(ApiResult<BookingDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/bookings/{id:guid}/approve")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Approve, "Booking")]
    public async Task<IActionResult> ApproveBooking(Guid id, CancellationToken ct)
    {
        var dto = await amenities.ApproveBookingAsync(id, ct);
        return Ok(ApiResult<BookingDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/bookings/{id:guid}/cancel")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Booking")]
    public async Task<IActionResult> CancelBooking(Guid id, CancellationToken ct)
    {
        var dto = await amenities.CancelBookingAsync(id, ct);
        return Ok(ApiResult<BookingDto>.Ok(dto, string.Empty));
    }

    // --- Resident events ---
    [HttpPost("api/v1/resident-events")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "ResidentEvent")]
    public async Task<IActionResult> PublishEvent([FromBody] PublishResidentEventBody body, CancellationToken ct)
    {
        var dto = await amenities.PublishResidentEventAsync(new PublishResidentEventRequest(
            body.CommunityId, body.OrganizerResidentId, body.UnitId, body.Title!, body.Description, body.Location,
            body.StartsAtUtc, body.EndsAtUtc, body.Capacity), ct);
        return Ok(ApiResult<ResidentEventDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/resident-events")]
    [RequirePermission(PermissionAction.View, "ResidentEvent")]
    public async Task<IActionResult> ListEvents([FromQuery] Guid communityId, CancellationToken ct)
    {
        var items = await amenities.ListResidentEventsAsync(communityId, ct);
        return Ok(ApiResult<IReadOnlyList<ResidentEventDto>>.Ok(items, string.Empty));
    }

    [HttpGet("api/v1/resident-events/{id:guid}")]
    [RequirePermission(PermissionAction.View, "ResidentEvent")]
    public async Task<IActionResult> GetEvent(Guid id, CancellationToken ct)
    {
        var dto = await amenities.GetResidentEventAsync(id, ct) ?? throw new NotFoundException("Resident event not found.");
        return Ok(ApiResult<ResidentEventDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/resident-events/{id:guid}/register")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "ResidentEvent")]
    public async Task<IActionResult> RegisterForEvent(Guid id, [FromBody] RegisterForResidentEventBody body, CancellationToken ct)
    {
        var dto = await amenities.RegisterForResidentEventAsync(new RegisterForResidentEventRequest(id, body.ResidentId, body.UnitId), ct);
        return Ok(ApiResult<ResidentEventRegistrationDto>.Ok(dto, string.Empty));
    }

    [HttpDelete("api/v1/resident-event-registrations/{registrationId:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "ResidentEvent")]
    public async Task<IActionResult> CancelRegistration(Guid registrationId, CancellationToken ct)
    {
        await amenities.CancelResidentEventRegistrationAsync(registrationId, ct);
        return Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(registrationId), string.Empty));
    }

    private static void AddEquals(List<FilterTerm> filters, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            filters.Add(new FilterTerm(field, FilterOperator.Equals, value));
        }
    }

    private static void AddEquals(List<FilterTerm> filters, string field, Guid? value)
    {
        if (value is { } v)
        {
            filters.Add(new FilterTerm(field, FilterOperator.Equals, v.ToString()));
        }
    }

    private static ListQueryParameters BuildParameters(int? page, int? pageSize, string? search, string? sort, List<FilterTerm> filters) =>
        new()
        {
            Page = page,
            PageSize = pageSize,
            Search = string.IsNullOrWhiteSpace(search) ? null : search,
            Filters = filters,
            Sort = EndpointSort.Parse(sort),
        };

    public sealed class CreateAmenityBody
    {
        [Required] public Guid CommunityId { get; init; }
        [Required(AllowEmptyStrings = false)] [MaxLength(200)] public string? Name { get; init; }
        [Required(AllowEmptyStrings = false)] [MaxLength(50)] public string? Code { get; init; }
        [Required(AllowEmptyStrings = false)] [MaxLength(100)] public string? AmenityType { get; init; }
        [MaxLength(200)] public string? Location { get; init; }
        [MaxLength(1000)] public string? Description { get; init; }
        [Range(1, int.MaxValue)] public int DefaultCapacity { get; init; }
    }

    public sealed class UpdateAmenityBody
    {
        [MaxLength(200)] public string? Name { get; init; }
        [MaxLength(100)] public string? AmenityType { get; init; }
        [MaxLength(100)] public string? Status { get; init; }
        [MaxLength(200)] public string? Location { get; init; }
        [MaxLength(1000)] public string? Description { get; init; }
        [Range(1, int.MaxValue)] public int? DefaultCapacity { get; init; }
    }

    public sealed class CreateFacilitySlotBody
    {
        [MaxLength(100)] public string? Name { get; init; }
        [Range(0, 1439)] public int StartMinutesOfDay { get; init; }
        [Range(1, 1440)] public int EndMinutesOfDay { get; init; }
        [Range(1, 127)] public int DaysOfWeekMask { get; init; }
        [Range(1, int.MaxValue)] public int Capacity { get; init; }
    }

    public sealed class SetBookingRuleBody
    {
        [MaxLength(500)] public string? AllowedResidentTypes { get; init; }
        [Range(0, int.MaxValue)] public int? AdvanceBookingWindowDays { get; init; }
        [Range(1, int.MaxValue)] public int? MinDurationMinutes { get; init; }
        [Range(1, int.MaxValue)] public int? MaxDurationMinutes { get; init; }
        [Range(1, int.MaxValue)] public int? MaxBookingsPerResident { get; init; }
        [Range(1, int.MaxValue)] public int? MaxBookingsPeriodDays { get; init; }
        public bool RequiresApproval { get; init; }
        [Range(0, 9999999)] public decimal BookingCharge { get; init; }
        [Range(0, 9999999)] public decimal SecurityDeposit { get; init; }
        [Range(0, int.MaxValue)] public int? CancellationNoticeHours { get; init; }
        [Range(0, 100)] public decimal? RefundPercentOnCancellation { get; init; }
    }

    public sealed class CreateBlackoutDateBody
    {
        [Required] public DateTime StartUtc { get; init; }
        [Required] public DateTime EndUtc { get; init; }
        [Required(AllowEmptyStrings = false)] [MaxLength(500)] public string? Reason { get; init; }
    }

    public sealed class CreateBookingBody
    {
        [Required] public Guid FacilitySlotId { get; init; }
        [Required] public DateTime BookingDate { get; init; }
        [Required] public Guid ResidentId { get; init; }
        [Required] public Guid UnitId { get; init; }
    }

    public sealed class PublishResidentEventBody
    {
        [Required] public Guid CommunityId { get; init; }
        [Required] public Guid OrganizerResidentId { get; init; }
        [Required] public Guid UnitId { get; init; }
        [Required(AllowEmptyStrings = false)] [MaxLength(200)] public string? Title { get; init; }
        [MaxLength(2000)] public string? Description { get; init; }
        [MaxLength(200)] public string? Location { get; init; }
        [Required] public DateTime StartsAtUtc { get; init; }
        [Required] public DateTime EndsAtUtc { get; init; }
        [Range(1, int.MaxValue)] public int Capacity { get; init; }
    }

    public sealed class RegisterForResidentEventBody
    {
        [Required] public Guid ResidentId { get; init; }
        [Required] public Guid UnitId { get; init; }
    }
}
