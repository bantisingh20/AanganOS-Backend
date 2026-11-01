using System.ComponentModel.DataAnnotations;
using App.Api.Endpoints;
using App.Api.Security;
using App.Application.Community;
using App.Application.Exceptions;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
public sealed class HierarchyNodesController(IHierarchyService hierarchy) : ControllerBase
{
    [HttpPost("api/v1/hierarchy-nodes")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "HierarchyNode")]
    public async Task<IActionResult> Create([FromBody] CreateHierarchyNodeBody body, CancellationToken ct)
    {
        var dto = await hierarchy.CreateAsync(
            new CreateHierarchyNodeRequest(body.CommunityId, body.ParentId, body.Level!, body.Name!), ct);
        return Ok(ApiResult<HierarchyNodeDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/communities/{communityId:guid}/hierarchy")]
    [RequirePermission(PermissionAction.View, "HierarchyNode")]
    public async Task<IActionResult> GetCommunityHierarchy(Guid communityId, CancellationToken ct)
    {
        var nodes = await hierarchy.GetCommunityHierarchyAsync(communityId, ct);
        return Ok(ApiResult<IReadOnlyList<HierarchyNodeDto>>.Ok(nodes, string.Empty));
    }

    [HttpGet("api/v1/hierarchy-nodes/{id:guid}")]
    [RequirePermission(PermissionAction.View, "HierarchyNode")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var dto = await hierarchy.GetAsync(id, includeArchived: false, ct)
            ?? throw new NotFoundException("Hierarchy node not found.");
        return Ok(ApiResult<HierarchyNodeDto>.Ok(dto, string.Empty));
    }

    [HttpPut("api/v1/hierarchy-nodes/{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "HierarchyNode")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateHierarchyNodeBody body, CancellationToken ct)
    {
        var dto = await hierarchy.UpdateAsync(id,
            new UpdateHierarchyNodeRequest(body.ParentId, body.UpdateParent, body.Level, body.Name), ct);
        return Ok(ApiResult<HierarchyNodeDto>.Ok(dto, string.Empty));
    }

    [HttpDelete("api/v1/hierarchy-nodes/{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Delete, "HierarchyNode")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await hierarchy.DeleteAsync(id, ct);
        return Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
    }

    public sealed class CreateHierarchyNodeBody
    {
        [Required(ErrorMessage = "communityId is required")]
        public Guid CommunityId { get; init; }

        public Guid? ParentId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "level is required")]
        [MaxLength(100)]
        public string? Level { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "name is required")]
        [MaxLength(256)]
        public string? Name { get; init; }
    }

    public sealed class UpdateHierarchyNodeBody
    {
        public Guid? ParentId { get; init; }

        public bool UpdateParent { get; init; }

        [MaxLength(100)]
        public string? Level { get; init; }

        [MaxLength(256)]
        public string? Name { get; init; }
    }
}
