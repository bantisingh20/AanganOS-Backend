using System.ComponentModel.DataAnnotations;
using App.Api.Endpoints;
using App.Api.Security;
using App.Application.Gate;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
public sealed class SecurityOpsController(ISecurityOpsService ops) : ControllerBase
{
    [HttpPost("api/v1/watchlist")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "WatchlistEntry")]
    public async Task<IActionResult> AddWatchlistEntry([FromBody] AddWatchlistEntryBody body, CancellationToken ct)
    {
        var dto = await ops.AddWatchlistEntryAsync(new AddWatchlistEntryRequest(
            body.CommunityId,
            body.SubjectType!,
            body.SubjectIdentifier!,
            body.Reason!,
            body.Severity!), ct);
        return Ok(ApiResult<WatchlistEntryDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/watchlist/{id:guid}/clear")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "WatchlistEntry")]
    public async Task<IActionResult> ClearWatchlistEntry(Guid id, CancellationToken ct)
    {
        var dto = await ops.ClearWatchlistEntryAsync(id, ct);
        return Ok(ApiResult<WatchlistEntryDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/watchlist")]
    [RequirePermission(PermissionAction.View, "WatchlistEntry")]
    public async Task<IActionResult> ListWatchlistEntries(
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] string? status,
        [FromQuery] string? subjectType,
        [FromQuery] string? severity,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        AddEquals(filters, "status", status);
        AddEquals(filters, "subjectType", subjectType);
        AddEquals(filters, "severity", severity);

        var data = await ops.ListWatchlistEntriesAsync(BuildParameters(page, pageSize, search, sort, filters), ct);
        return Ok(ApiResult<PagedData<WatchlistEntryDto>>.Ok(data, string.Empty));
    }

    [HttpPost("api/v1/guard-shifts")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "GuardShift")]
    public async Task<IActionResult> ScheduleGuardShift([FromBody] ScheduleGuardShiftBody body, CancellationToken ct)
    {
        var dto = await ops.ScheduleGuardShiftAsync(new ScheduleGuardShiftRequest(
            body.CommunityId,
            body.GuardUserId,
            body.GateId,
            body.GateLabel,
            body.ShiftStartUtc,
            body.ShiftEndUtc), ct);
        return Ok(ApiResult<GuardShiftDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/guard-shifts")]
    [RequirePermission(PermissionAction.View, "GuardShift")]
    public async Task<IActionResult> ListGuardShifts(
        [FromQuery] string? sort,
        [FromQuery] string? status,
        [FromQuery] Guid? guardUserId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        AddEquals(filters, "status", status);
        AddEquals(filters, "guardUserId", guardUserId);

        var data = await ops.ListGuardShiftsAsync(BuildParameters(page, pageSize, null, sort, filters), ct);
        return Ok(ApiResult<PagedData<GuardShiftDto>>.Ok(data, string.Empty));
    }

    [HttpPost("api/v1/incidents")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "Incident")]
    public async Task<IActionResult> ReportIncident([FromBody] ReportIncidentBody body, CancellationToken ct)
    {
        var dto = await ops.ReportIncidentAsync(new ReportIncidentRequest(
            body.CommunityId,
            body.ReportedByUserId,
            body.GateId,
            body.Location,
            body.Category!,
            body.Severity!,
            body.Description!), ct);
        return Ok(ApiResult<IncidentDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/incidents/{id:guid}/acknowledge")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Incident")]
    public async Task<IActionResult> AcknowledgeIncident(Guid id, [FromBody] ActingUserBody body, CancellationToken ct)
    {
        var dto = await ops.AcknowledgeIncidentAsync(id, body.UserId, ct);
        return Ok(ApiResult<IncidentDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/incidents/{id:guid}/resolve")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Incident")]
    public async Task<IActionResult> ResolveIncident(Guid id, [FromBody] ActingUserBody body, CancellationToken ct)
    {
        var dto = await ops.ResolveIncidentAsync(id, body.UserId, ct);
        return Ok(ApiResult<IncidentDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/incidents")]
    [RequirePermission(PermissionAction.View, "Incident")]
    public async Task<IActionResult> ListIncidents(
        [FromQuery] string? sort,
        [FromQuery] string? status,
        [FromQuery] string? severity,
        [FromQuery] string? category,
        [FromQuery] string? escalationState,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        AddEquals(filters, "status", status);
        AddEquals(filters, "severity", severity);
        AddEquals(filters, "category", category);
        AddEquals(filters, "escalationState", escalationState);

        var data = await ops.ListIncidentsAsync(BuildParameters(page, pageSize, null, sort, filters), ct);
        return Ok(ApiResult<PagedData<IncidentDto>>.Ok(data, string.Empty));
    }

    [HttpPost("api/v1/sos-alerts")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "SosAlert")]
    public async Task<IActionResult> RaiseSos([FromBody] RaiseSosBody body, CancellationToken ct)
    {
        var dto = await ops.RaiseSosAsync(new RaiseSosRequest(
            body.CommunityId,
            body.RaisedByUserId,
            body.UnitId,
            body.Location), ct);
        return Ok(ApiResult<SosAlertDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/sos-alerts/{id:guid}/acknowledge")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "SosAlert")]
    public async Task<IActionResult> AcknowledgeSos(Guid id, [FromBody] ActingUserBody body, CancellationToken ct)
    {
        var dto = await ops.AcknowledgeSosAsync(id, body.UserId, ct);
        return Ok(ApiResult<SosAlertDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/sos-alerts/{id:guid}/resolve")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "SosAlert")]
    public async Task<IActionResult> ResolveSos(Guid id, [FromBody] ActingUserBody body, CancellationToken ct)
    {
        var dto = await ops.ResolveSosAsync(id, body.UserId, ct);
        return Ok(ApiResult<SosAlertDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/sos-alerts")]
    [RequirePermission(PermissionAction.View, "SosAlert")]
    public async Task<IActionResult> ListSosAlerts(
        [FromQuery] string? sort,
        [FromQuery] string? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        AddEquals(filters, "status", status);

        var data = await ops.ListSosAlertsAsync(BuildParameters(page, pageSize, null, sort, filters), ct);
        return Ok(ApiResult<PagedData<SosAlertDto>>.Ok(data, string.Empty));
    }

    [HttpPost("api/v1/access-integration")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "Incident")]
    public async Task<IActionResult> TriggerAccessIntegration([FromBody] TriggerAccessIntegrationBody body, CancellationToken ct)
    {
        var dto = await ops.TriggerAccessIntegrationAsync(new TriggerAccessIntegrationRequest(
            body.CommunityId,
            body.Action!,
            body.GateId,
            body.GateLabel,
            body.TriggeredByUserId,
            body.Detail), ct);
        return Ok(ApiResult<AccessIntegrationEventDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/access-integration")]
    [RequirePermission(PermissionAction.View, "Incident")]
    public async Task<IActionResult> ListAccessIntegrationEvents(
        [FromQuery] string? sort,
        [FromQuery] string? action,
        [FromQuery] string? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        AddEquals(filters, "action", action);
        AddEquals(filters, "status", status);

        var data = await ops.ListAccessIntegrationEventsAsync(BuildParameters(page, pageSize, null, sort, filters), ct);
        return Ok(ApiResult<PagedData<AccessIntegrationEventDto>>.Ok(data, string.Empty));
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

    private static ListQueryParameters BuildParameters(
        int? page, int? pageSize, string? search, string? sort, List<FilterTerm> filters) =>
        new()
        {
            Page = page,
            PageSize = pageSize,
            Search = string.IsNullOrWhiteSpace(search) ? null : search,
            Filters = filters,
            Sort = EndpointSort.Parse(sort),
        };

    public sealed class AddWatchlistEntryBody
    {
        [Required(ErrorMessage = "communityId is required")]
        public Guid CommunityId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "subjectType is required")]
        [MaxLength(50)]
        public string? SubjectType { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "subjectIdentifier is required")]
        [MaxLength(256)]
        public string? SubjectIdentifier { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "reason is required")]
        [MaxLength(1000)]
        public string? Reason { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "severity is required")]
        [MaxLength(50)]
        public string? Severity { get; init; }
    }

    public sealed class ScheduleGuardShiftBody
    {
        [Required(ErrorMessage = "communityId is required")]
        public Guid CommunityId { get; init; }

        [Required(ErrorMessage = "guardUserId is required")]
        public Guid GuardUserId { get; init; }

        public Guid? GateId { get; init; }

        [MaxLength(256)]
        public string? GateLabel { get; init; }

        [Required(ErrorMessage = "shiftStartUtc is required")]
        public DateTime ShiftStartUtc { get; init; }

        [Required(ErrorMessage = "shiftEndUtc is required")]
        public DateTime ShiftEndUtc { get; init; }
    }

    public sealed class ReportIncidentBody
    {
        [Required(ErrorMessage = "communityId is required")]
        public Guid CommunityId { get; init; }

        [Required(ErrorMessage = "reportedByUserId is required")]
        public Guid ReportedByUserId { get; init; }

        public Guid? GateId { get; init; }

        [MaxLength(256)]
        public string? Location { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "category is required")]
        [MaxLength(100)]
        public string? Category { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "severity is required")]
        [MaxLength(50)]
        public string? Severity { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "description is required")]
        [MaxLength(2000)]
        public string? Description { get; init; }
    }

    public sealed class RaiseSosBody
    {
        [Required(ErrorMessage = "communityId is required")]
        public Guid CommunityId { get; init; }

        [Required(ErrorMessage = "raisedByUserId is required")]
        public Guid RaisedByUserId { get; init; }

        public Guid? UnitId { get; init; }

        [MaxLength(256)]
        public string? Location { get; init; }
    }

    public sealed class TriggerAccessIntegrationBody
    {
        [Required(ErrorMessage = "communityId is required")]
        public Guid CommunityId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "action is required")]
        [MaxLength(100)]
        public string? Action { get; init; }

        public Guid? GateId { get; init; }

        [MaxLength(256)]
        public string? GateLabel { get; init; }

        public Guid? TriggeredByUserId { get; init; }

        [MaxLength(1000)]
        public string? Detail { get; init; }
    }

    public sealed class ActingUserBody
    {
        [Required(ErrorMessage = "userId is required")]
        public Guid UserId { get; init; }
    }
}
