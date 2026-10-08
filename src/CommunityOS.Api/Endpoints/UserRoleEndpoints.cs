using System.ComponentModel.DataAnnotations;
using CommunityOS.Api.Security;
using CommunityOS.Api.Validation;
using CommunityOS.Application.Identity;
using CommunityOS.Contracts.Envelope;
using CommunityOS.Domain.Identity;

namespace CommunityOS.Api.Endpoints;

/// <summary>
/// HTTP surface over <see cref="IUserRoleService"/> (Task 5.1) — role assignment management
/// (Req 3.3, 3.4). Assigning a role to a user creates a community-scoped <c>UserRole</c>
/// (Req 3.3); revoking it drops the role's permissions (Req 3.4). Both mutations require the
/// <see cref="PermissionAction.Assign"/> permission on the <c>UserRole</c> resource type (Req 4.1);
/// listing a user's assignments requires <see cref="PermissionAction.View"/>. Responses use the
/// <see cref="ApiResult{T}"/> envelope (Req 9.1); the assign body validates at the trust boundary
/// (Req 9.2). Duplicate assignment → CONFLICT, unknown user/role/assignment → NOT_FOUND, both via
/// the exception middleware.
/// </summary>
public static class UserRoleEndpoints
{
    public static IEndpointRouteBuilder MapUserRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/user-roles");

        // POST /api/v1/user-roles — assign a role to a user, scoped to a community (Req 3.3).
        group.MapPost("/", async (
                AssignRoleBody body,
                IUserRoleService userRoles,
                CancellationToken ct) =>
            {
                var id = await userRoles.AssignAsync(body.UserId, body.RoleId, body.CommunityId, ct);
                return Results.Ok(ApiResult<AssignRoleResponse>.Ok(new AssignRoleResponse(id), string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Assign, "UserRole"));

        // DELETE /api/v1/user-roles — revoke the assignment so the role's permissions no longer
        // apply (Req 3.4). The triple (userId, roleId, communityId) identifies the assignment;
        // communityId omitted = the platform-wide assignment.
        group.MapDelete("/", async (
                Guid userId,
                Guid roleId,
                Guid? communityId,
                IUserRoleService userRoles,
                CancellationToken ct) =>
            {
                await userRoles.RevokeAsync(userId, roleId, communityId, ct);
                return Results.Ok(ApiResult<RevokeRoleResponse>.Ok(new RevokeRoleResponse(true), string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Assign, "UserRole"));

        // GET /api/v1/user-roles/{userId} — list a user's active role assignments (Req 3.3).
        group.MapGet("/{userId:guid}", async (
                Guid userId,
                IUserRoleService userRoles,
                CancellationToken ct) =>
            {
                var items = await userRoles.ListForUserAsync(userId, ct);
                return Results.Ok(ApiResult<IReadOnlyList<UserRoleDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "UserRole"));

        return app;
    }

    public sealed class AssignRoleBody
    {
        [Required(ErrorMessage = "userId is required")]
        public Guid UserId { get; init; }

        [Required(ErrorMessage = "roleId is required")]
        public Guid RoleId { get; init; }

        /// <summary>Community scope of the assignment; null = platform-wide (Req 3.3).</summary>
        public Guid? CommunityId { get; init; }
    }

    public sealed record AssignRoleResponse(Guid UserRoleId);

    public sealed record RevokeRoleResponse(bool Revoked);
}
