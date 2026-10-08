using System.ComponentModel.DataAnnotations;
using CommunityOS.Api.Security;
using CommunityOS.Api.Validation;
using CommunityOS.Application.Exceptions;
using CommunityOS.Application.Parcel;
using CommunityOS.Application.Querying;
using CommunityOS.Contracts.Envelope;
using CommunityOS.Domain.Identity;

namespace CommunityOS.Api.Endpoints;

/// <summary>
/// HTTP surface over the Phase 4 Parcel_Service (<see cref="IParcelService"/>) — parcel registration
/// + the configurable status state machine (Req 28.x), custody locations + custody assignment (Req
/// 29.x), authorized collector + verified at-most-once handover (Req 30.x) and the append-only
/// custody timeline + lost/damaged/returned flows (Req 31.x). Mirrors the established minimal-API
/// style of <see cref="GateEndpoints"/>: the <see cref="ApiResult{T}"/> envelope (Req 9.1), a
/// <see cref="RequirePermissionAttribute"/> on the correct (Action, ResourceType) (Req 4.1),
/// <see cref="DataAnnotationsValidationFilter"/> on write bodies (Req 9.2), the "write" rate-limit
/// policy on every mutation (Req 65.5) and the shared paginated list infra
/// (<see cref="ListQueryParameters"/> + the service's allow-listed <c>ListQueryConfiguration</c>,
/// Req 9.3). Lists are scoped to the caller's authorized communities (and, for a resident principal,
/// their own unit) by the EF global tenant/self-scope query filter — no bespoke predicate here.
///
/// <para><b>Permission pairs mirror the service's defense-in-depth checks (steering: security
/// first, no hardcoding).</b> Register = <c>(Create, Parcel)</c>; change-status / assign-custody /
/// handover / record lost-damaged / record returned = <c>(Edit, Parcel)</c>; custody-location create
/// = <c>(Create, CustodyLocation)</c>; authorize-collector = <c>(Create, ParcelAuthorization)</c>;
/// parcel reads = <c>(View, Parcel)</c>; custody-timeline read = <c>(View, CustodyTimelineEntry)</c>.
/// The service re-checks the same permission as defense-in-depth; CONFLICT / VALIDATION_ERROR /
/// NOT_FOUND / FORBIDDEN surface through the central exception envelope — the endpoints only shape
/// the request.</para>
///
/// <para><b>Idempotent handover (Req 30.5).</b> <c>POST /parcels/{id}/handover</c> reads the
/// client-supplied <c>Idempotency-Key</c> HTTP header and passes it into
/// <see cref="PerformHandoverRequest.IdempotencyKey"/> so a retry carrying the same key performs the
/// handover at most once and replays the first result. A missing header is rejected with the
/// standard VALIDATION_ERROR before the service runs.</para>
/// </summary>
public static class ParcelEndpoints
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapParcelEndpoints(this IEndpointRouteBuilder app)
    {
        MapParcelCoreEndpoints(app);
        MapCustodyLocationEndpoints(app);
        return app;
    }

    // --- Parcels (/api/v1/parcels) — Req 28.x, 29.x, 30.x, 31.x ----------------------------------
    private static void MapParcelCoreEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/parcels");

        // POST — register a received parcel at the gate/reception (Req 28.1).
        group.MapPost("/", async (
                RegisterParcelBody body,
                IParcelService parcels,
                CancellationToken ct) =>
            {
                var dto = await parcels.RegisterParcelAsync(new RegisterParcelRequest(
                    body.RecipientUnitId,
                    body.TrackingNumber!,
                    body.Provider!,
                    body.Category!,
                    body.Condition!,
                    body.Receiver,
                    body.ReceivedLocation,
                    body.PhotoFileId,
                    body.ConditionEvidenceFileId), ct);
                return Results.Ok(ApiResult<ParcelDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "Parcel"));

        // GET — paginated/filter/sort/search list (Req 28.5).
        group.MapGet("/", async (
                string? search,
                string? sort,
                string? status,
                string? category,
                string? condition,
                Guid? recipientUnitId,
                Guid? custodyLocationId,
                bool? isPriority,
                int? page,
                int? pageSize,
                IParcelService parcels,
                CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                AddEquals(filters, "status", status);
                AddEquals(filters, "category", category);
                AddEquals(filters, "condition", condition);
                AddEquals(filters, "recipientUnitId", recipientUnitId);
                AddEquals(filters, "custodyLocationId", custodyLocationId);
                if (isPriority is { } p)
                {
                    filters.Add(new FilterTerm("isPriority", FilterOperator.Equals, p ? "true" : "false"));
                }

                var data = await parcels.ListParcelsAsync(BuildParameters(page, pageSize, search, sort, filters), ct);
                return Results.Ok(ApiResult<PagedData<ParcelDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Parcel"));

        // GET /{id} — fetch one, tenant/self-scoped. Unknown/foreign id → NOT_FOUND.
        group.MapGet("/{id:guid}", async (
                Guid id,
                IParcelService parcels,
                CancellationToken ct) =>
            {
                var dto = await parcels.GetParcelAsync(id, ct)
                    ?? throw new NotFoundException("Parcel not found.");
                return Results.Ok(ApiResult<ParcelDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Parcel"));

        // POST /{id}/status — change status through the configurable state machine (Req 28.2, 28.3).
        group.MapPost("/{id:guid}/status", async (
                Guid id,
                ChangeParcelStatusBody body,
                IParcelService parcels,
                CancellationToken ct) =>
            {
                var dto = await parcels.ChangeParcelStatusAsync(id, new ChangeParcelStatusRequest(body.ToStatus!, body.Reason), ct);
                return Results.Ok(ApiResult<ParcelDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Parcel"));

        // POST /{id}/custody-location — move the parcel into custody + assign a location (Req 29.1, 29.4).
        group.MapPost("/{id:guid}/custody-location", async (
                Guid id,
                AssignCustodyLocationBody body,
                IParcelService parcels,
                CancellationToken ct) =>
            {
                var dto = await parcels.AssignCustodyLocationAsync(id, new AssignCustodyLocationRequest(body.CustodyLocationId), ct);
                return Results.Ok(ApiResult<ParcelDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Parcel"));

        // POST /{id}/authorizations — a resident authorizes a collector for their own unit's parcel
        // (Req 30.1). Returns the authorization + the one-time OTP + QR token (returned once).
        group.MapPost("/{id:guid}/authorizations", async (
                Guid id,
                AuthorizeCollectorBody body,
                IParcelService parcels,
                CancellationToken ct) =>
            {
                var result = await parcels.AuthorizeCollectorAsync(new AuthorizeCollectorRequest(
                    id,
                    body.CollectorName!,
                    body.CollectorRelationship,
                    body.VerificationMethod,
                    body.ExpectedIdReference), ct);
                return Results.Ok(ApiResult<AuthorizeCollectorResult>.Ok(result, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "ParcelAuthorization"));

        // POST /{id}/handover — guard performs a verified, at-most-once handover (Req 30.2–30.5). The
        // Idempotency-Key HTTP header carries the at-most-once key into the service (Req 30.5); a
        // missing header is a VALIDATION_ERROR before the service runs.
        group.MapPost("/{id:guid}/handover", async (
                Guid id,
                PerformHandoverBody body,
                HttpRequest httpRequest,
                IParcelService parcels,
                CancellationToken ct) =>
            {
                var idempotencyKey = httpRequest.Headers[IdempotencyKeyHeader].ToString();
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    throw new CommunityOS.Application.Exceptions.ValidationException(
                        IdempotencyKeyHeader, "An Idempotency-Key header is required for a parcel handover.");
                }

                var dto = await parcels.PerformHandoverAsync(new PerformHandoverRequest(
                    body.ParcelAuthorizationId,
                    body.Otp,
                    body.QrToken,
                    body.PresentedIdReference,
                    body.ProofFileId,
                    idempotencyKey), ct);
                return Results.Ok(ApiResult<ParcelHandoverDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Parcel"));

        // POST /{id}/lost-damaged — record the parcel lost/damaged + create a linked incident (Req 31.3).
        group.MapPost("/{id:guid}/lost-damaged", async (
                Guid id,
                RecordLostDamagedBody body,
                IParcelService parcels,
                CancellationToken ct) =>
            {
                var dto = await parcels.RecordLostDamagedAsync(id, new RecordLostDamagedRequest(
                    body.Circumstances!,
                    body.EvidenceFileId,
                    body.Category,
                    body.Severity), ct);
                return Results.Ok(ApiResult<ParcelDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Parcel"));

        // POST /{id}/returned — record the parcel returned to the delivery provider (Req 31.4).
        group.MapPost("/{id:guid}/returned", async (
                Guid id,
                RecordReturnedBody body,
                IParcelService parcels,
                CancellationToken ct) =>
            {
                var dto = await parcels.RecordReturnedAsync(id, new RecordReturnedRequest(body.Reason), ct);
                return Results.Ok(ApiResult<ParcelDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Parcel"));

        // GET /{id}/custody-timeline — the chronological append-only custody timeline (Req 31.5).
        group.MapGet("/{id:guid}/custody-timeline", async (
                Guid id,
                IParcelService parcels,
                CancellationToken ct) =>
            {
                var entries = await parcels.GetCustodyTimelineAsync(id, ct);
                return Results.Ok(ApiResult<IReadOnlyList<CustodyTimelineEntryDto>>.Ok(entries, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "CustodyTimelineEntry"));
    }

    // --- Custody locations (/api/v1/custody-locations) — Req 29.1 --------------------------------
    private static void MapCustodyLocationEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/custody-locations");

        // POST — create a custody location (secure bin/shelf). Duplicate code → CONFLICT (Req 29.1).
        group.MapPost("/", async (
                CreateCustodyLocationBody body,
                IParcelService parcels,
                CancellationToken ct) =>
            {
                var dto = await parcels.CreateCustodyLocationAsync(new CreateCustodyLocationRequest(body.CommunityId, body.Code!), ct);
                return Results.Ok(ApiResult<CustodyLocationDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "CustodyLocation"));
    }

    // --- Shared helpers --------------------------------------------------------------------------

    /// <summary>Add an equals filter term only when the value is present (keeps the allow-list honest).</summary>
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

    // --- Request bodies (validated via DataAnnotationsValidationFilter) --------------------------

    public sealed class RegisterParcelBody
    {
        [Required(ErrorMessage = "recipientUnitId is required")]
        public Guid RecipientUnitId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "trackingNumber is required")]
        [MaxLength(256)]
        public string? TrackingNumber { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "provider is required")]
        [MaxLength(256)]
        public string? Provider { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "category is required")]
        [MaxLength(100)]
        public string? Category { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "condition is required")]
        [MaxLength(100)]
        public string? Condition { get; init; }

        [MaxLength(256)]
        public string? Receiver { get; init; }

        [MaxLength(256)]
        public string? ReceivedLocation { get; init; }

        public Guid? PhotoFileId { get; init; }

        public Guid? ConditionEvidenceFileId { get; init; }
    }

    public sealed class ChangeParcelStatusBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "toStatus is required")]
        [MaxLength(100)]
        public string? ToStatus { get; init; }

        [MaxLength(1000)]
        public string? Reason { get; init; }
    }

    public sealed class AssignCustodyLocationBody
    {
        [Required(ErrorMessage = "custodyLocationId is required")]
        public Guid CustodyLocationId { get; init; }
    }

    public sealed class AuthorizeCollectorBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "collectorName is required")]
        [MaxLength(256)]
        public string? CollectorName { get; init; }

        [MaxLength(256)]
        public string? CollectorRelationship { get; init; }

        [MaxLength(100)]
        public string? VerificationMethod { get; init; }

        [MaxLength(256)]
        public string? ExpectedIdReference { get; init; }
    }

    public sealed class PerformHandoverBody
    {
        [Required(ErrorMessage = "parcelAuthorizationId is required")]
        public Guid ParcelAuthorizationId { get; init; }

        [MaxLength(32)]
        public string? Otp { get; init; }

        [MaxLength(512)]
        public string? QrToken { get; init; }

        [MaxLength(256)]
        public string? PresentedIdReference { get; init; }

        public Guid? ProofFileId { get; init; }
    }

    public sealed class RecordLostDamagedBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "circumstances is required")]
        [MaxLength(2000)]
        public string? Circumstances { get; init; }

        public Guid? EvidenceFileId { get; init; }

        [MaxLength(100)]
        public string? Category { get; init; }

        [MaxLength(100)]
        public string? Severity { get; init; }
    }

    public sealed class RecordReturnedBody
    {
        [MaxLength(1000)]
        public string? Reason { get; init; }
    }

    public sealed class CreateCustodyLocationBody
    {
        [Required(ErrorMessage = "communityId is required")]
        public Guid CommunityId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "code is required")]
        [MaxLength(100)]
        public string? Code { get; init; }
    }
}
