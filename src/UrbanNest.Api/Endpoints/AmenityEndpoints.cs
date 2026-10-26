using System.ComponentModel.DataAnnotations;
using UrbanNest.Api.Security;
using UrbanNest.Api.Validation;
using UrbanNest.Application.Amenities;
using UrbanNest.Application.Exceptions;
using UrbanNest.Application.Querying;
using UrbanNest.Contracts.Envelope;
using UrbanNest.Domain.Identity;

namespace UrbanNest.Api.Endpoints;


public static class AmenityEndpoints
{
    public static IEndpointRouteBuilder MapAmenityEndpoints(this IEndpointRouteBuilder app)
    {
        MapAmenityCoreEndpoints(app);
        MapBookingEndpoints(app);
        MapResidentEventEndpoints(app);
        return app;
    }

    // --- Amenities + slots + rules + blackout dates (/api/v1/amenities) — Req 38.x ---------------
    private static void MapAmenityCoreEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/amenities");

        group.MapPost("/", async (CreateAmenityBody body, IAmenityService amenities, CancellationToken ct) =>
            {
                var dto = await amenities.CreateAmenityAsync(new CreateAmenityRequest(
                    body.CommunityId, body.Name!, body.Code!, body.AmenityType!, body.Location, body.Description, body.DefaultCapacity), ct);
                return Results.Ok(ApiResult<AmenityDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "Amenity"));

        group.MapGet("/", async (
                string? search, string? sort, string? amenityType, string? status, Guid? communityId,
                int? page, int? pageSize, IAmenityService amenities, CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                AddEquals(filters, "amenityType", amenityType);
                AddEquals(filters, "status", status);
                AddEquals(filters, "communityId", communityId);
                var data = await amenities.ListAmenitiesAsync(BuildParameters(page, pageSize, search, sort, filters), ct);
                return Results.Ok(ApiResult<PagedData<AmenityDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Amenity"));

        group.MapGet("/{id:guid}", async (Guid id, IAmenityService amenities, CancellationToken ct) =>
            {
                var dto = await amenities.GetAmenityAsync(id, ct) ?? throw new NotFoundException("Amenity not found.");
                return Results.Ok(ApiResult<AmenityDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Amenity"));

        group.MapPut("/{id:guid}", async (Guid id, UpdateAmenityBody body, IAmenityService amenities, CancellationToken ct) =>
            {
                var dto = await amenities.UpdateAmenityAsync(id, new UpdateAmenityRequest(
                    body.Name, body.AmenityType, body.Status, body.Location, body.Description, body.DefaultCapacity), ct);
                return Results.Ok(ApiResult<AmenityDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Amenity"));

        group.MapDelete("/{id:guid}", async (Guid id, IAmenityService amenities, CancellationToken ct) =>
            {
                await amenities.DeleteAmenityAsync(id, ct);
                return Results.Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Delete, "Amenity"));

        // Facility slots.
        group.MapPost("/{id:guid}/slots", async (Guid id, CreateFacilitySlotBody body, IAmenityService amenities, CancellationToken ct) =>
            {
                var dto = await amenities.CreateFacilitySlotAsync(new CreateFacilitySlotRequest(
                    id, body.Name, body.StartMinutesOfDay, body.EndMinutesOfDay, body.DaysOfWeekMask, body.Capacity), ct);
                return Results.Ok(ApiResult<FacilitySlotDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "FacilitySlot"));

        group.MapGet("/{id:guid}/slots", async (Guid id, IAmenityService amenities, CancellationToken ct) =>
            {
                var items = await amenities.GetAmenitySlotsAsync(id, ct);
                return Results.Ok(ApiResult<IReadOnlyList<FacilitySlotDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "FacilitySlot"));

        // Booking-rule set (singleton per amenity; managed via the amenity's Edit grant).
        group.MapPut("/{id:guid}/booking-rule", async (Guid id, SetBookingRuleBody body, IAmenityService amenities, CancellationToken ct) =>
            {
                var dto = await amenities.SetBookingRuleAsync(new SetBookingRuleRequest(
                    id, body.AllowedResidentTypes, body.AdvanceBookingWindowDays, body.MinDurationMinutes, body.MaxDurationMinutes,
                    body.MaxBookingsPerResident, body.MaxBookingsPeriodDays, body.RequiresApproval, body.BookingCharge,
                    body.SecurityDeposit, body.CancellationNoticeHours, body.RefundPercentOnCancellation), ct);
                return Results.Ok(ApiResult<BookingRuleDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Amenity"));

        group.MapGet("/{id:guid}/booking-rule", async (Guid id, IAmenityService amenities, CancellationToken ct) =>
            {
                var dto = await amenities.GetBookingRuleAsync(id, ct) ?? throw new NotFoundException("No booking rule configured for this amenity.");
                return Results.Ok(ApiResult<BookingRuleDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Amenity"));

        // Blackout dates.
        group.MapPost("/{id:guid}/blackout-dates", async (Guid id, CreateBlackoutDateBody body, IAmenityService amenities, CancellationToken ct) =>
            {
                var dto = await amenities.CreateBlackoutDateAsync(new CreateBlackoutDateRequest(id, body.StartUtc, body.EndUtc, body.Reason!), ct);
                return Results.Ok(ApiResult<BlackoutDateDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "BlackoutDate"));

        group.MapGet("/{id:guid}/blackout-dates", async (Guid id, IAmenityService amenities, CancellationToken ct) =>
            {
                var items = await amenities.GetAmenityBlackoutDatesAsync(id, ct);
                return Results.Ok(ApiResult<IReadOnlyList<BlackoutDateDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "BlackoutDate"));

        // Slot + blackout deletes (flat routes keyed by the child id).
        app.MapDelete("/api/v1/facility-slots/{slotId:guid}", async (Guid slotId, IAmenityService amenities, CancellationToken ct) =>
            {
                await amenities.DeleteFacilitySlotAsync(slotId, ct);
                return Results.Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(slotId), string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Delete, "FacilitySlot"));

        app.MapDelete("/api/v1/blackout-dates/{blackoutId:guid}", async (Guid blackoutId, IAmenityService amenities, CancellationToken ct) =>
            {
                await amenities.DeleteBlackoutDateAsync(blackoutId, ct);
                return Results.Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(blackoutId), string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Delete, "BlackoutDate"));
    }

    // --- Bookings (/api/v1/bookings) — Req 39.x --------------------------------------------------
    private static void MapBookingEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/bookings");

        group.MapPost("/", async (CreateBookingBody body, IAmenityService amenities, CancellationToken ct) =>
            {
                var dto = await amenities.CreateBookingAsync(new CreateBookingRequest(
                    body.FacilitySlotId, body.BookingDate, body.ResidentId, body.UnitId), ct);
                return Results.Ok(ApiResult<BookingDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "Booking"));

        group.MapGet("/{id:guid}", async (Guid id, IAmenityService amenities, CancellationToken ct) =>
            {
                var dto = await amenities.GetBookingAsync(id, ct) ?? throw new NotFoundException("Booking not found.");
                return Results.Ok(ApiResult<BookingDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Booking"));

        group.MapPost("/{id:guid}/approve", async (Guid id, IAmenityService amenities, CancellationToken ct) =>
            {
                var dto = await amenities.ApproveBookingAsync(id, ct);
                return Results.Ok(ApiResult<BookingDto>.Ok(dto, string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Approve, "Booking"));

        group.MapPost("/{id:guid}/cancel", async (Guid id, IAmenityService amenities, CancellationToken ct) =>
            {
                var dto = await amenities.CancelBookingAsync(id, ct);
                return Results.Ok(ApiResult<BookingDto>.Ok(dto, string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Booking"));
    }

    // --- Resident events (/api/v1/resident-events) — Req 41.4, 41.5 ------------------------------
    private static void MapResidentEventEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/resident-events");

        group.MapPost("/", async (PublishResidentEventBody body, IAmenityService amenities, CancellationToken ct) =>
            {
                var dto = await amenities.PublishResidentEventAsync(new PublishResidentEventRequest(
                    body.CommunityId, body.OrganizerResidentId, body.UnitId, body.Title!, body.Description, body.Location,
                    body.StartsAtUtc, body.EndsAtUtc, body.Capacity), ct);
                return Results.Ok(ApiResult<ResidentEventDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "ResidentEvent"));

        group.MapGet("/", async (Guid communityId, IAmenityService amenities, CancellationToken ct) =>
            {
                var items = await amenities.ListResidentEventsAsync(communityId, ct);
                return Results.Ok(ApiResult<IReadOnlyList<ResidentEventDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "ResidentEvent"));

        group.MapGet("/{id:guid}", async (Guid id, IAmenityService amenities, CancellationToken ct) =>
            {
                var dto = await amenities.GetResidentEventAsync(id, ct) ?? throw new NotFoundException("Resident event not found.");
                return Results.Ok(ApiResult<ResidentEventDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "ResidentEvent"));

        group.MapPost("/{id:guid}/register", async (Guid id, RegisterForResidentEventBody body, IAmenityService amenities, CancellationToken ct) =>
            {
                var dto = await amenities.RegisterForResidentEventAsync(new RegisterForResidentEventRequest(id, body.ResidentId, body.UnitId), ct);
                return Results.Ok(ApiResult<ResidentEventRegistrationDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "ResidentEvent"));

        app.MapDelete("/api/v1/resident-event-registrations/{registrationId:guid}", async (Guid registrationId, IAmenityService amenities, CancellationToken ct) =>
            {
                await amenities.CancelResidentEventRegistrationAsync(registrationId, ct);
                return Results.Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(registrationId), string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "ResidentEvent"));
    }

    // --- Shared helpers --------------------------------------------------------------------------

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

    // --- Request bodies (validated via DataAnnotationsValidationFilter) --------------------------

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
