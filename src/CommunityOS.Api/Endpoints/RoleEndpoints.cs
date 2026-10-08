using System.ComponentModel.DataAnnotations;
using CommunityOS.Api.Security;
using CommunityOS.Api.Validation;
using CommunityOS.Application.Exceptions;
using CommunityOS.Application.Identity;
using CommunityOS.Application.Querying;
using CommunityOS.Contracts.Envelope;
using CommunityOS.Domain.Identity;

namespace CommunityOS.Api.Endpoints;

/// <summary>
/// HTTP surface over <see cref="IRoleService"/> (Task 5.1) — role management (Req 3.5, 3.6, 3.7).
/// A role is defined with its permission set (each an (Action, ResourceType) pair). Every endpoint
/// uses the <see cref="ApiResult{T}"/> envelope (Req 9.1), carries a
/// <see cref="RequirePermissionAttribute"/> on the <c>Role</c> resource type (Req 4.1), validates
/// at the trust boundary (Req 9.2), and the list is paginated (Req 9.3). A duplicate role name
/// within scope surfaces as the service's CONFLICT (Req 3.6); unknown permissions as VALIDATION;
/// unknown ids as NOT_FOUND — all via the exception middleware.
/// </summary>
public static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/roles");

        // POST /api/v1/roles — define a role with its permission set (Req 3.5).
        group.MapPost("/", async (
                DefineRoleBody body,
                IRoleService roles,
                CancellationToken ct) =>
            {
                var dto = await roles.DefineAsync(body.ToRequest(), ct);
                return Results.Ok(ApiResult<RoleDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "Role"));

        // GET /api/v1/roles — paginated/filter/sort/search list (Req 9.3).
        group.MapGet("/", async (
                string? search,
                string? sort,
                int? page,
                int? pageSize,
                IRoleService roles,
                CancellationToken ct) =>
            {
                var parameters = new ListQueryParameters
                {
                    Page = page,
                    PageSize = pageSize,
                    Search = string.IsNullOrWhiteSpace(search) ? null : search,
                    Sort = EndpointSort.Parse(sort),
                };

                var data = await roles.ListAsync(parameters, ct);
                return Results.Ok(ApiResult<PagedData<RoleDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Role"));

        // GET /api/v1/roles/{id} — fetch one with its granted permissions. Unknown id → NOT_FOUND.
        group.MapGet("/{id:guid}", async (
                Guid id,
                IRoleService roles,
                CancellationToken ct) =>
            {
                var dto = await roles.GetAsync(id, includeArchived: false, ct)
                    ?? throw new NotFoundException("Role not found.");
                return Results.Ok(ApiResult<RoleDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Role"));

        // PUT /api/v1/roles/{id} — redefine name and/or permission set (Req 3.5). Duplicate → CONFLICT.
        group.MapPut("/{id:guid}", async (
                Guid id,
                DefineRoleBody body,
                IRoleService roles,
                CancellationToken ct) =>
            {
                var dto = await roles.UpdateAsync(id, body.ToRequest(), ct);
                return Results.Ok(ApiResult<RoleDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Role"));

        // DELETE /api/v1/roles/{id} — soft-delete (Req 3.7).
        group.MapDelete("/{id:guid}", async (
                Guid id,
                IRoleService roles,
                CancellationToken ct) =>
            {
                await roles.DeleteAsync(id, ct);
                return Results.Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Delete, "Role"));

        return app;
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
