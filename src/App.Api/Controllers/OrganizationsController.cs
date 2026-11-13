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
[Route("api/v1/organizations")]
public sealed class OrganizationsController(IOrganizationService orgs) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "Organization")]
    public async Task<IActionResult> Create([FromBody] CreateOrganizationBody body, CancellationToken ct)
    {
        var dto = await orgs.CreateAsync(new CreateOrganizationRequest(body.Name!), ct);
        return Ok(ApiResult<OrganizationDto>.Ok(dto, string.Empty));
    }

    [HttpGet]
    [RequirePermission(PermissionAction.View, "Organization")]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var parameters = new ListQueryParameters
        {
            Page = page,
            PageSize = pageSize,
            Search = string.IsNullOrWhiteSpace(search) ? null : search,
            Sort = EndpointSort.Parse(sort),
        };

        var data = await orgs.ListAsync(parameters, ct);
        return Ok(ApiResult<PagedData<OrganizationDto>>.Ok(data, string.Empty));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionAction.View, "Organization")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var dto = await orgs.GetAsync(id, includeArchived: false, ct)
            ?? throw new NotFoundException("Organization not found.");
        return Ok(ApiResult<OrganizationDto>.Ok(dto, string.Empty));
    }

    [HttpPut("{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Organization")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateOrganizationBody body, CancellationToken ct)
    {
        var dto = await orgs.UpdateAsync(id, new UpdateOrganizationRequest(body.Name), ct);
        return Ok(ApiResult<OrganizationDto>.Ok(dto, string.Empty));
    }

    [HttpPost("{id:guid}/activate")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Organization")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        var dto = await orgs.ActivateAsync(id, ct);
        return Ok(ApiResult<OrganizationDto>.Ok(dto, string.Empty));
    }

    [HttpDelete("{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Delete, "Organization")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await orgs.DeleteAsync(id, ct);
        return Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
    }

    public sealed class CreateOrganizationBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "name is required")]
        [MaxLength(256)]
        public string? Name { get; init; }
    }

    public sealed class UpdateOrganizationBody
    {
        [MaxLength(256)]
        public string? Name { get; init; }
    }
}
