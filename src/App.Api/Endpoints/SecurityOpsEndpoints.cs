using System.ComponentModel.DataAnnotations;
using App.Api.Security;
using App.Api.Validation;
using App.Application.Gate;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;

namespace App.Api.Endpoints;

/// <summary>
/// HTTP surface over the Phase 3 gate security-operations service
/// (<see cref="ISecurityOpsService"/>) — watchlist (Req 26.1, 26.3, 26.5), guard shifts (Req 26.2,
/// 26.4), incidents (Req 27.1, 27.2), SOS alerts (Req 27.3, 27.4) and the access-control integration
/// (Req 27.5). Same minimal-API conventions as <see cref="GateEndpoints"/>: the
/// <see cref="ApiResult{T}"/> envelope, a <see cref="RequirePermissionAttribute"/> on the correct
/// (Action, ResourceType), <see cref="DataAnnotationsValidationFilter"/> on write bodies, the "write"
/// rate-limit policy on mutations and the shared paginated list infra. Lists are tenant-scoped by the
/// EF global query filter.
///
/// <para><b>Permission pairs reference the seeded Gate catalog (steering: security first).</b>
/// Watchlist add/clear = <c>(Create, WatchlistEntry)</c>, read = <c>(View, WatchlistEntry)</c> (Req
/// 26.3); guard-shift schedule = <c>(Create, GuardShift)</c>, read = <c>(View, GuardShift)</c>;
/// incident report = <c>(Create, Incident)</c>, acknowledge/resolve = <c>(Edit, Incident)</c>, read =
/// <c>(View, Incident)</c>; SOS raise = <c>(Create, SosAlert)</c>, acknowledge/resolve =
/// <c>(Edit, SosAlert)</c>, read = <c>(View, SosAlert)</c>; access-integration trigger =
/// <c>(Create, Incident)</c> (the guard-facing gate-operations grant, matching the service), its log
/// read = <c>(View, Incident)</c>. The services re-check the same permission as defense-in-depth;
/// CONFLICT / VALIDATION_ERROR / NOT_FOUND / FORBIDDEN surface through the central exception
/// envelope.</para>
///
/// <para><b>Acting-user ids on transitions.</b> Incident/SOS acknowledge/resolve and access-trigger
/// carry the acting user id in the body (matching the service signatures) so the service records the
/// decider; the permission gate + tenant scope still bound who may act.</para>
/// </summary>
public static class SecurityOpsEndpoints
{
    public static IEndpointRouteBuilder MapSecurityOpsEndpoints(this IEndpointRouteBuilder app)
    {
        MapWatchlistEndpoints(app);
        MapGuardShiftEndpoints(app);
        MapIncidentEndpoints(app);
        MapSosEndpoints(app);
        MapAccessIntegrationEndpoints(app);
        return app;
    }

    // --- Watchlist (/api/v1/watchlist) — Req 26.1, 26.3, 26.5 ------------------------------------
    private static void MapWatchlistEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/watchlist");

        group.MapPost("/", async (
                AddWatchlistEntryBody body,
                ISecurityOpsService ops,
                CancellationToken ct) =>
            {
                var dto = await ops.AddWatchlistEntryAsync(new AddWatchlistEntryRequest(
                    body.CommunityId,
                    body.SubjectType!,
                    body.SubjectIdentifier!,
                    body.Reason!,
                    body.Severity!), ct);
                return Results.Ok(ApiResult<WatchlistEntryDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "WatchlistEntry"));

        group.MapPost("/{id:guid}/clear", async (
                Guid id,
                ISecurityOpsService ops,
                CancellationToken ct) =>
            {
                var dto = await ops.ClearWatchlistEntryAsync(id, ct);
                return Results.Ok(ApiResult<WatchlistEntryDto>.Ok(dto, string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "WatchlistEntry"));

        group.MapGet("/", async (
                string? search,
                string? sort,
                string? status,
                string? subjectType,
                string? severity,
                int? page,
                int? pageSize,
                ISecurityOpsService ops,
                CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                AddEquals(filters, "status", status);
                AddEquals(filters, "subjectType", subjectType);
                AddEquals(filters, "severity", severity);

                var data = await ops.ListWatchlistEntriesAsync(BuildParameters(page, pageSize, search, sort, filters), ct);
                return Results.Ok(ApiResult<PagedData<WatchlistEntryDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "WatchlistEntry"));
    }

    // --- Guard shifts (/api/v1/guard-shifts) — Req 26.2, 26.4 ------------------------------------
    private static void MapGuardShiftEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/guard-shifts");

        group.MapPost("/", async (
                ScheduleGuardShiftBody body,
                ISecurityOpsService ops,
                CancellationToken ct) =>
            {
                var dto = await ops.ScheduleGuardShiftAsync(new ScheduleGuardShiftRequest(
                    body.CommunityId,
                    body.GuardUserId,
                    body.GateId,
                    body.GateLabel,
                    body.ShiftStartUtc,
                    body.ShiftEndUtc), ct);
                return Results.Ok(ApiResult<GuardShiftDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "GuardShift"));

        group.MapGet("/", async (
                string? sort,
                string? status,
                Guid? guardUserId,
                int? page,
                int? pageSize,
                ISecurityOpsService ops,
                CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                AddEquals(filters, "status", status);
                AddEquals(filters, "guardUserId", guardUserId);

                var data = await ops.ListGuardShiftsAsync(BuildParameters(page, pageSize, null, sort, filters), ct);
                return Results.Ok(ApiResult<PagedData<GuardShiftDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "GuardShift"));
    }

    // --- Incidents (/api/v1/incidents) — Req 27.1, 27.2 ------------------------------------------
    private static void MapIncidentEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/incidents");

        group.MapPost("/", async (
                ReportIncidentBody body,
                ISecurityOpsService ops,
                CancellationToken ct) =>
            {
                var dto = await ops.ReportIncidentAsync(new ReportIncidentRequest(
                    body.CommunityId,
                    body.ReportedByUserId,
                    body.GateId,
                    body.Location,
                    body.Category!,
                    body.Severity!,
                    body.Description!), ct);
                return Results.Ok(ApiResult<IncidentDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "Incident"));

        group.MapPost("/{id:guid}/acknowledge", async (
                Guid id,
                ActingUserBody body,
                ISecurityOpsService ops,
                CancellationToken ct) =>
            {
                var dto = await ops.AcknowledgeIncidentAsync(id, body.UserId, ct);
                return Results.Ok(ApiResult<IncidentDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Incident"));

        group.MapPost("/{id:guid}/resolve", async (
                Guid id,
                ActingUserBody body,
                ISecurityOpsService ops,
                CancellationToken ct) =>
            {
                var dto = await ops.ResolveIncidentAsync(id, body.UserId, ct);
                return Results.Ok(ApiResult<IncidentDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Incident"));

        group.MapGet("/", async (
                string? sort,
                string? status,
                string? severity,
                string? category,
                string? escalationState,
                int? page,
                int? pageSize,
                ISecurityOpsService ops,
                CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                AddEquals(filters, "status", status);
                AddEquals(filters, "severity", severity);
                AddEquals(filters, "category", category);
                AddEquals(filters, "escalationState", escalationState);

                var data = await ops.ListIncidentsAsync(BuildParameters(page, pageSize, null, sort, filters), ct);
                return Results.Ok(ApiResult<PagedData<IncidentDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Incident"));
    }

    // --- SOS (/api/v1/sos-alerts) — Req 27.3, 27.4 ----------------------------------------------
    private static void MapSosEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/sos-alerts");

        group.MapPost("/", async (
                RaiseSosBody body,
                ISecurityOpsService ops,
                CancellationToken ct) =>
            {
                var dto = await ops.RaiseSosAsync(new RaiseSosRequest(
                    body.CommunityId,
                    body.RaisedByUserId,
                    body.UnitId,
                    body.Location), ct);
                return Results.Ok(ApiResult<SosAlertDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "SosAlert"));

        group.MapPost("/{id:guid}/acknowledge", async (
                Guid id,
                ActingUserBody body,
                ISecurityOpsService ops,
                CancellationToken ct) =>
            {
                var dto = await ops.AcknowledgeSosAsync(id, body.UserId, ct);
                return Results.Ok(ApiResult<SosAlertDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "SosAlert"));

        group.MapPost("/{id:guid}/resolve", async (
                Guid id,
                ActingUserBody body,
                ISecurityOpsService ops,
                CancellationToken ct) =>
            {
                var dto = await ops.ResolveSosAsync(id, body.UserId, ct);
                return Results.Ok(ApiResult<SosAlertDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "SosAlert"));

        group.MapGet("/", async (
                string? sort,
                string? status,
                int? page,
                int? pageSize,
                ISecurityOpsService ops,
                CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                AddEquals(filters, "status", status);

                var data = await ops.ListSosAlertsAsync(BuildParameters(page, pageSize, null, sort, filters), ct);
                return Results.Ok(ApiResult<PagedData<SosAlertDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "SosAlert"));
    }

    // --- Access integration (/api/v1/access-integration) — Req 27.5 -----------------------------
    private static void MapAccessIntegrationEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/access-integration");

        // Trigger gated on the guard-facing gate-operations grant (Create on Incident), matching the
        // service's own permission check.
        group.MapPost("/", async (
                TriggerAccessIntegrationBody body,
                ISecurityOpsService ops,
                CancellationToken ct) =>
            {
                var dto = await ops.TriggerAccessIntegrationAsync(new TriggerAccessIntegrationRequest(
                    body.CommunityId,
                    body.Action!,
                    body.GateId,
                    body.GateLabel,
                    body.TriggeredByUserId,
                    body.Detail), ct);
                return Results.Ok(ApiResult<AccessIntegrationEventDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "Incident"));

        // GET — the recorded access-integration event log (Req 27.5), View on Incident (the same
        // gate-operations resource the trigger is gated on).
        group.MapGet("/", async (
                string? sort,
                string? action,
                string? status,
                int? page,
                int? pageSize,
                ISecurityOpsService ops,
                CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                AddEquals(filters, "action", action);
                AddEquals(filters, "status", status);

                var data = await ops.ListAccessIntegrationEventsAsync(BuildParameters(page, pageSize, null, sort, filters), ct);
                return Results.Ok(ApiResult<PagedData<AccessIntegrationEventDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Incident"));
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

    // --- Request bodies --------------------------------------------------------------------------

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

    /// <summary>Acting-user body shared by the incident/SOS acknowledge/resolve transitions.</summary>
    public sealed class ActingUserBody
    {
        [Required(ErrorMessage = "userId is required")]
        public Guid UserId { get; init; }
    }
}
