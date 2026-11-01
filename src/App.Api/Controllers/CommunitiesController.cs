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
[Route("api/v1/communities")]
public sealed class CommunitiesController(ICommunityService communities) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "Community")]
    public async Task<IActionResult> Create([FromBody] CreateCommunityBody body, CancellationToken ct)
    {
        var dto = await communities.CreateAsync(
            new CreateCommunityRequest(body.OrganizationId, body.Name!, body.CommunityType!), ct);
        return Ok(ApiResult<CommunityDto>.Ok(dto, string.Empty));
    }

    [HttpGet]
    [RequirePermission(PermissionAction.View, "Community")]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] string? communityType,
        [FromQuery] Guid? organizationId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        if (!string.IsNullOrWhiteSpace(communityType))
        {
            filters.Add(new FilterTerm("communityType", FilterOperator.Equals, communityType));
        }
        if (organizationId is not null)
        {
            filters.Add(new FilterTerm("organizationId", FilterOperator.Equals, organizationId.Value.ToString()));
        }

        var parameters = new ListQueryParameters
        {
            Page = page,
            PageSize = pageSize,
            Search = string.IsNullOrWhiteSpace(search) ? null : search,
            Filters = filters,
            Sort = EndpointSort.Parse(sort),
        };

        var data = await communities.ListAsync(parameters, ct);
        return Ok(ApiResult<PagedData<CommunityDto>>.Ok(data, string.Empty));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionAction.View, "Community")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var dto = await communities.GetAsync(id, includeArchived: false, ct)
            ?? throw new NotFoundException("Community not found.");
        return Ok(ApiResult<CommunityDto>.Ok(dto, string.Empty));
    }

    [HttpPut("{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Community")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCommunityBody body, CancellationToken ct)
    {
        var dto = await communities.UpdateAsync(id, new UpdateCommunityRequest(body.Name, body.CommunityType), ct);
        return Ok(ApiResult<CommunityDto>.Ok(dto, string.Empty));
    }

    [HttpDelete("{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Delete, "Community")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await communities.DeleteAsync(id, ct);
        return Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
    }

    public sealed class CreateCommunityBody
    {
        [Required(ErrorMessage = "organizationId is required")]
        public Guid OrganizationId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "name is required")]
        [MaxLength(256)]
        public string? Name { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "communityType is required")]
        [MaxLength(100)]
        public string? CommunityType { get; init; }
    }

    public sealed class UpdateCommunityBody
    {
        [MaxLength(256)]
        public string? Name { get; init; }

        [MaxLength(100)]
        public string? CommunityType { get; init; }
    }
}
