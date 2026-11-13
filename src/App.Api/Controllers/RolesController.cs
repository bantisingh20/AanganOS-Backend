using System.ComponentModel.DataAnnotations;
using App.Api.Endpoints;
using App.Api.Security;
using App.Application.Exceptions;
using App.Application.Identity;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
[Route("api/v1/roles")]
public sealed class RolesController(IRoleService roles) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "Role")]
    public async Task<IActionResult> Define([FromBody] DefineRoleBody body, CancellationToken ct)
    {
        var dto = await roles.DefineAsync(body.ToRequest(), ct);
        return Ok(ApiResult<RoleDto>.Ok(dto, string.Empty));
    }

    [HttpGet]
    [RequirePermission(PermissionAction.View, "Role")]
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

        var data = await roles.ListAsync(parameters, ct);
        return Ok(ApiResult<PagedData<RoleDto>>.Ok(data, string.Empty));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionAction.View, "Role")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var dto = await roles.GetAsync(id, includeArchived: false, ct)
            ?? throw new NotFoundException("Role not found.");
        return Ok(ApiResult<RoleDto>.Ok(dto, string.Empty));
    }

    [HttpPut("{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Role")]
    public async Task<IActionResult> Update(Guid id, [FromBody] DefineRoleBody body, CancellationToken ct)
    {
        var dto = await roles.UpdateAsync(id, body.ToRequest(), ct);
        return Ok(ApiResult<RoleDto>.Ok(dto, string.Empty));
    }

    [HttpDelete("{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Delete, "Role")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await roles.DeleteAsync(id, ct);
        return Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
    }

    public sealed class DefineRoleBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "name is required")]
        [MaxLength(200)]
        public string Name { get; init; } = string.Empty;

        public RoleScope Scope { get; init; }

        public Guid? CommunityId { get; init; }

        [Required(ErrorMessage = "permissions is required")]
        [MinLength(1, ErrorMessage = "a role must grant at least one permission")]
        public List<PermissionGrantBody> Permissions { get; init; } = [];

        public DefineRoleRequest ToRequest() => new(
            Name,
            Scope,
            CommunityId,
            Permissions.Select(p => new PermissionGrant(p.Action, p.ResourceType)).ToList());
    }

    public sealed class PermissionGrantBody
    {
        public PermissionAction Action { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "resourceType is required")]
        [MaxLength(100)]
        public string ResourceType { get; init; } = string.Empty;
    }
}
