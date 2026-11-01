using System.ComponentModel.DataAnnotations;
using App.Api.Security;
using App.Application.Identity;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
[Route("api/v1/user-roles")]
public sealed class UserRolesController(IUserRoleService userRoles) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Assign, "UserRole")]
    public async Task<IActionResult> Assign([FromBody] AssignRoleBody body, CancellationToken ct)
    {
        var id = await userRoles.AssignAsync(body.UserId, body.RoleId, body.CommunityId, ct);
        return Ok(ApiResult<AssignRoleResponse>.Ok(new AssignRoleResponse(id), string.Empty));
    }

    [HttpDelete]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Assign, "UserRole")]
    public async Task<IActionResult> Revoke(
        [FromQuery] Guid userId,
        [FromQuery] Guid roleId,
        [FromQuery] Guid? communityId,
        CancellationToken ct)
    {
        await userRoles.RevokeAsync(userId, roleId, communityId, ct);
        return Ok(ApiResult<RevokeRoleResponse>.Ok(new RevokeRoleResponse(true), string.Empty));
    }

    [HttpGet("{userId:guid}")]
    [RequirePermission(PermissionAction.View, "UserRole")]
    public async Task<IActionResult> ListForUser(Guid userId, CancellationToken ct)
    {
        var items = await userRoles.ListForUserAsync(userId, ct);
        return Ok(ApiResult<IReadOnlyList<UserRoleDto>>.Ok(items, string.Empty));
    }

    public sealed class AssignRoleBody
    {
        [Required(ErrorMessage = "userId is required")]
        public Guid UserId { get; init; }

        [Required(ErrorMessage = "roleId is required")]
        public Guid RoleId { get; init; }

        public Guid? CommunityId { get; init; }
    }

    public sealed record AssignRoleResponse(Guid UserRoleId);

    public sealed record RevokeRoleResponse(bool Revoked);
}
