using System.ComponentModel.DataAnnotations;
using CommunityOS.Api.Security;
using CommunityOS.Api.Validation;
using CommunityOS.Application.Community;
using CommunityOS.Application.Exceptions;
using CommunityOS.Application.Querying;
using CommunityOS.Contracts.Envelope;
using CommunityOS.Domain.Identity;

namespace CommunityOS.Api.Endpoints;

/// <summary>
/// HTTP surface over <see cref="ICommunityService"/> (Task 11.3) — the tenant-root community
/// (Req 12.1, 12.2, 12.5). Every endpoint uses the <see cref="ApiResult{T}"/> envelope (Req 9.1),
/// carries a <see cref="RequirePermissionAttribute"/> on the <c>Community</c> resource type for its
/// action (Req 4.1), validates write bodies (Req 9.2), applies the "write" rate-limit policy to
/// mutations (Req 65.5), and the list is paginated/filter/sort/search through the shared query infra
/// (Req 9.3). The list is <b>scoped to the caller's authorized communities</b> by the EF global
/// tenant query filter — a community admin sees only their own community row, a super-admin sees
/// across (Req 12.4) — not by a bespoke predicate here. The community-type validation and
/// CONFLICT/NOT_FOUND handling live in the service.
/// </summary>
public static class CommunityEndpoints
{
    public static IEndpointRouteBuilder MapCommunityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/communities");

        // POST /api/v1/communities — create (Req 12.1). Invalid type/unknown org → VALIDATION_ERROR.
        group.MapPost("/", async (
                CreateCommunityBody body,
                ICommunityService communities,
                CancellationToken ct) =>
            {
                var dto = await communities.CreateAsync(
                    new CreateCommunityRequest(body.OrganizationId, body.Name!, body.CommunityType!), ct);
                return Results.Ok(ApiResult<CommunityDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "Community"));

        // GET /api/v1/communities — paginated/filter/sort/search list, scoped to authorized
        // communities by the tenant filter (Req 12.4). Non-archived only.
        group.MapGet("/", async (
                string? search,
                string? sort,
                string? communityType,
                Guid? organizationId,
                int? page,
                int? pageSize,
                ICommunityService communities,
                CancellationToken ct) =>
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
                return Results.Ok(ApiResult<PagedData<CommunityDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Community"));

        // GET /api/v1/communities/{id} — fetch one, tenant-scoped. Unknown/foreign id → NOT_FOUND.
        group.MapGet("/{id:guid}", async (
                Guid id,
                ICommunityService communities,
                CancellationToken ct) =>
            {
                var dto = await communities.GetAsync(id, includeArchived: false, ct)
                    ?? throw new NotFoundException("Community not found.");
                return Results.Ok(ApiResult<CommunityDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Community"));

        // PUT /api/v1/communities/{id} — edit (Req 12.2). Invalid type → VALIDATION_ERROR; unknown → NOT_FOUND.
        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdateCommunityBody body,
                ICommunityService communities,
                CancellationToken ct) =>
            {
                var dto = await communities.UpdateAsync(id, new UpdateCommunityRequest(body.Name, body.CommunityType), ct);
                return Results.Ok(ApiResult<CommunityDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Community"));

        // DELETE /api/v1/communities/{id} — soft-delete (Req 12.5).
        group.MapDelete("/{id:guid}", async (
                Guid id,
                ICommunityService communities,
                CancellationToken ct) =>
            {
                await communities.DeleteAsync(id, ct);
                return Results.Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Delete, "Community"));

        return app;
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
