using System.ComponentModel.DataAnnotations;
using App.Api.Endpoints;
using App.Api.Security;
using App.Application.Community;
using App.Application.Exceptions;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
[Route("api/v1/announcements")]
public sealed class AnnouncementsController(IAnnouncementService announcements) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionAction.View, "Announcement")]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] string? category,
        [FromQuery] Guid? communityId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] bool? includeInactive,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        if (!string.IsNullOrWhiteSpace(category))
        {
            filters.Add(new FilterTerm("category", FilterOperator.Equals, category));
        }
        if (communityId is { } cid)
        {
            filters.Add(new FilterTerm("communityId", FilterOperator.Equals, cid.ToString()));
        }
        if (fromDate is not null)
        {
            filters.Add(new FilterTerm("publishedAtUtc", FilterOperator.GreaterThanOrEqual, fromDate.Value.ToUniversalTime().ToString("o")));
        }

        var parameters = new ListQueryParameters
        {
            Page = page,
            PageSize = pageSize,
            Search = string.IsNullOrWhiteSpace(search) ? null : search,
            Filters = filters,
            Sort = EndpointSort.Parse(sort),
        };

        var data = await announcements.ListAnnouncementsAsync(parameters, includeInactive ?? false, ct);
        return Ok(ApiResult<PagedData<AnnouncementDto>>.Ok(data, string.Empty));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionAction.View, "Announcement")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var dto = await announcements.GetAnnouncementAsync(id, ct)
            ?? throw new NotFoundException("Announcement not found.");
        return Ok(ApiResult<AnnouncementDto>.Ok(dto, string.Empty));
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "Announcement")]
    public async Task<IActionResult> Create([FromBody] CreateAnnouncementBody body, CancellationToken ct)
    {
        var dto = await announcements.CreateAnnouncementAsync(
            new CreateAnnouncementRequest(body.CommunityId, body.Title!, body.Body!, body.Category!), ct);
        return Ok(ApiResult<AnnouncementDto>.Ok(dto, string.Empty));
    }

    [HttpPut("{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Announcement")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAnnouncementBody body, CancellationToken ct)
    {
        var dto = await announcements.UpdateAnnouncementAsync(
            id, new UpdateAnnouncementRequest(body.Title, body.Body, body.Category), ct);
        return Ok(ApiResult<AnnouncementDto>.Ok(dto, string.Empty));
    }

    [HttpPost("{id:guid}/activate")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Announcement")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        var dto = await announcements.SetAnnouncementActiveAsync(id, true, ct);
        return Ok(ApiResult<AnnouncementDto>.Ok(dto, string.Empty));
    }

    [HttpPost("{id:guid}/deactivate")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Announcement")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var dto = await announcements.SetAnnouncementActiveAsync(id, false, ct);
        return Ok(ApiResult<AnnouncementDto>.Ok(dto, string.Empty));
    }

    [HttpDelete("{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Delete, "Announcement")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await announcements.DeleteAnnouncementAsync(id, ct);
        return Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
    }

    public sealed class CreateAnnouncementBody
    {
        [Required(ErrorMessage = "communityId is required")]
        public Guid CommunityId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "title is required")]
        [MaxLength(200)]
        public string? Title { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "body is required")]
        [MaxLength(4000)]
        public string? Body { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "category is required")]
        [MaxLength(50)]
        public string? Category { get; init; }
    }

    public sealed class UpdateAnnouncementBody
    {
        [MaxLength(200)]
        public string? Title { get; init; }

        [MaxLength(4000)]
        public string? Body { get; init; }

        [MaxLength(50)]
        public string? Category { get; init; }
    }
}
