using System.ComponentModel.DataAnnotations;
using CommunityOS.Api.Security;
using CommunityOS.Api.Validation;
using CommunityOS.Application.Community;
using CommunityOS.Application.Exceptions;
using CommunityOS.Contracts.Envelope;
using CommunityOS.Domain.Identity;

namespace CommunityOS.Api.Endpoints;

/// <summary>
/// HTTP surface over <see cref="IHierarchyService"/> (Task 11.3) — the configurable, optional-level
/// property hierarchy (Req 13.1–13.5). Every endpoint uses the <see cref="ApiResult{T}"/> envelope
/// (Req 9.1), carries a <see cref="RequirePermissionAttribute"/> on the <c>HierarchyNode</c> resource
/// type for its action (Req 4.1), validates write bodies (Req 9.2), and applies the "write"
/// rate-limit policy to mutations (Req 65.5).
///
/// <para><b>Hierarchy "list" is a tree, not a page (Req 13.4).</b> A community's hierarchy is small
/// and inherently a parent/child structure, so instead of a paginated list this exposes
/// <c>GET /api/v1/communities/{communityId}/hierarchy</c> returning the whole tenant-scoped tree in a
/// single query (no N+1). The read is scoped to the caller's authorized communities by the EF global
/// tenant query filter.</para>
/// </summary>
public static class HierarchyEndpoints
{
    public static IEndpointRouteBuilder MapHierarchyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/hierarchy-nodes");

        // POST /api/v1/hierarchy-nodes — create (Req 13.1). Cross-community parent / invalid level → VALIDATION_ERROR.
        group.MapPost("/", async (
                CreateHierarchyNodeBody body,
                IHierarchyService hierarchy,
                CancellationToken ct) =>
            {
                var dto = await hierarchy.CreateAsync(
                    new CreateHierarchyNodeRequest(body.CommunityId, body.ParentId, body.Level!, body.Name!), ct);
                return Results.Ok(ApiResult<HierarchyNodeDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "HierarchyNode"));

        // GET /api/v1/communities/{communityId}/hierarchy — the community's full hierarchy tree,
        // tenant-scoped in a single query (Req 13.4).
        app.MapGet("/api/v1/communities/{communityId:guid}/hierarchy", async (
                Guid communityId,
                IHierarchyService hierarchy,
                CancellationToken ct) =>
            {
                var nodes = await hierarchy.GetCommunityHierarchyAsync(communityId, ct);
                return Results.Ok(ApiResult<IReadOnlyList<HierarchyNodeDto>>.Ok(nodes, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "HierarchyNode"));

        // GET /api/v1/hierarchy-nodes/{id} — fetch one, tenant-scoped. Unknown/foreign id → NOT_FOUND.
        group.MapGet("/{id:guid}", async (
                Guid id,
                IHierarchyService hierarchy,
                CancellationToken ct) =>
            {
                var dto = await hierarchy.GetAsync(id, includeArchived: false, ct)
                    ?? throw new NotFoundException("Hierarchy node not found.");
                return Results.Ok(ApiResult<HierarchyNodeDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "HierarchyNode"));

        // PUT /api/v1/hierarchy-nodes/{id} — edit (re-parent re-validated for same-community, Req 13.3).
        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdateHierarchyNodeBody body,
                IHierarchyService hierarchy,
                CancellationToken ct) =>
            {
                var dto = await hierarchy.UpdateAsync(id,
                    new UpdateHierarchyNodeRequest(body.ParentId, body.UpdateParent, body.Level, body.Name), ct);
                return Results.Ok(ApiResult<HierarchyNodeDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "HierarchyNode"));

        // DELETE /api/v1/hierarchy-nodes/{id} — soft-delete (Req 13.5).
        group.MapDelete("/{id:guid}", async (
                Guid id,
                IHierarchyService hierarchy,
                CancellationToken ct) =>
            {
                await hierarchy.DeleteAsync(id, ct);
                return Results.Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Delete, "HierarchyNode"));

        return app;
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

        // When true the ParentId above is applied (allowing null = make this a root node); when
        // false the parent link is left untouched.
        public bool UpdateParent { get; init; }

        [MaxLength(100)]
        public string? Level { get; init; }

        [MaxLength(256)]
        public string? Name { get; init; }
    }
}
