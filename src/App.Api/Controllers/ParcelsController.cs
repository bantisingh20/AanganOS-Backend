using System.ComponentModel.DataAnnotations;
using App.Api.Endpoints;
using App.Api.Security;
using App.Application.Exceptions;
using App.Application.Parcel;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
public sealed class ParcelsController(IParcelService parcels) : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    [HttpPost("api/v1/parcels")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "Parcel")]
    public async Task<IActionResult> Register([FromBody] RegisterParcelBody body, CancellationToken ct)
    {
        var dto = await parcels.RegisterParcelAsync(new RegisterParcelRequest(
            body.RecipientUnitId, body.TrackingNumber!, body.Provider!, body.Category!, body.Condition!,
            body.Receiver, body.ReceivedLocation, body.PhotoFileId, body.ConditionEvidenceFileId), ct);
        return Ok(ApiResult<ParcelDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/parcels")]
    [RequirePermission(PermissionAction.View, "Parcel")]
    public async Task<IActionResult> List(
        [FromQuery] string? search, [FromQuery] string? sort, [FromQuery] string? status, [FromQuery] string? category,
        [FromQuery] string? condition, [FromQuery] Guid? recipientUnitId, [FromQuery] Guid? custodyLocationId,
        [FromQuery] bool? isPriority, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
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
        return Ok(ApiResult<PagedData<ParcelDto>>.Ok(data, string.Empty));
    }

    [HttpGet("api/v1/parcels/{id:guid}")]
    [RequirePermission(PermissionAction.View, "Parcel")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var dto = await parcels.GetParcelAsync(id, ct) ?? throw new NotFoundException("Parcel not found.");
        return Ok(ApiResult<ParcelDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/parcels/{id:guid}/status")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Parcel")]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeParcelStatusBody body, CancellationToken ct)
    {
        var dto = await parcels.ChangeParcelStatusAsync(id, new ChangeParcelStatusRequest(body.ToStatus!, body.Reason), ct);
        return Ok(ApiResult<ParcelDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/parcels/{id:guid}/custody-location")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Parcel")]
    public async Task<IActionResult> AssignCustody(Guid id, [FromBody] AssignCustodyLocationBody body, CancellationToken ct)
    {
        var dto = await parcels.AssignCustodyLocationAsync(id, new AssignCustodyLocationRequest(body.CustodyLocationId), ct);
        return Ok(ApiResult<ParcelDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/parcels/{id:guid}/authorizations")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "ParcelAuthorization")]
    public async Task<IActionResult> AuthorizeCollector(Guid id, [FromBody] AuthorizeCollectorBody body, CancellationToken ct)
    {
        var result = await parcels.AuthorizeCollectorAsync(new AuthorizeCollectorRequest(
            id, body.CollectorName!, body.CollectorRelationship, body.VerificationMethod, body.ExpectedIdReference), ct);
        return Ok(ApiResult<AuthorizeCollectorResult>.Ok(result, string.Empty));
    }

    [HttpPost("api/v1/parcels/{id:guid}/handover")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Parcel")]
    public async Task<IActionResult> Handover(Guid id, [FromBody] PerformHandoverBody body, CancellationToken ct)
    {
        var idempotencyKey = Request.Headers[IdempotencyKeyHeader].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new App.Application.Exceptions.ValidationException(
                IdempotencyKeyHeader, "An Idempotency-Key header is required for a parcel handover.");
        }

        var dto = await parcels.PerformHandoverAsync(new PerformHandoverRequest(
            body.ParcelAuthorizationId, body.Otp, body.QrToken, body.PresentedIdReference, body.ProofFileId, idempotencyKey), ct);
        return Ok(ApiResult<ParcelHandoverDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/parcels/{id:guid}/lost-damaged")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Parcel")]
    public async Task<IActionResult> RecordLostDamaged(Guid id, [FromBody] RecordLostDamagedBody body, CancellationToken ct)
    {
        var dto = await parcels.RecordLostDamagedAsync(id, new RecordLostDamagedRequest(
            body.Circumstances!, body.EvidenceFileId, body.Category, body.Severity), ct);
        return Ok(ApiResult<ParcelDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/parcels/{id:guid}/returned")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Parcel")]
    public async Task<IActionResult> RecordReturned(Guid id, [FromBody] RecordReturnedBody body, CancellationToken ct)
    {
        var dto = await parcels.RecordReturnedAsync(id, new RecordReturnedRequest(body.Reason), ct);
        return Ok(ApiResult<ParcelDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/parcels/{id:guid}/custody-timeline")]
    [RequirePermission(PermissionAction.View, "CustodyTimelineEntry")]
    public async Task<IActionResult> CustodyTimeline(Guid id, CancellationToken ct)
    {
        var entries = await parcels.GetCustodyTimelineAsync(id, ct);
        return Ok(ApiResult<IReadOnlyList<CustodyTimelineEntryDto>>.Ok(entries, string.Empty));
    }

    [HttpPost("api/v1/custody-locations")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "CustodyLocation")]
    public async Task<IActionResult> CreateCustodyLocation([FromBody] CreateCustodyLocationBody body, CancellationToken ct)
    {
        var dto = await parcels.CreateCustodyLocationAsync(new CreateCustodyLocationRequest(body.CommunityId, body.Code!), ct);
        return Ok(ApiResult<CustodyLocationDto>.Ok(dto, string.Empty));
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
