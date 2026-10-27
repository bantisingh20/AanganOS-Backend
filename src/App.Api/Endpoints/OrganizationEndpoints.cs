using System.ComponentModel.DataAnnotations;
using App.Api.Security;
using App.Api.Validation;
using App.Application.Community;
using App.Application.Exceptions;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;

namespace App.Api.Endpoints;

/// <summary>
/// HTTP surface over <see cref="IOrganizationService"/> (Task 11.3) — the platform-scoped top-level
/// tenant owner (Req 12.1). Every endpoint uses the <see cref="ApiResult{T}"/> envelope (Req 9.1),
/// carries a <see cref="RequirePermissionAttribute"/> on the <c>Organization</c> resource type for
/// its action (Req 4.1), validates write bodies at the trust boundary (Req 9.2), applies the "write"
/// rate-limit policy to mutations (Req 65.5), and the list is paginated through the shared query
/// infra with an allow-listed sort/search field map (Req 9.3). Service CONFLICT/NOT_FOUND exceptions
/// flow through the terminal exception middleware.
/// </summary>
public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations");

        // POST /api/v1/organizations — create (Req 12.1).
        group.MapPost("/", async (
                CreateOrganizationBody body,
                IOrganizationService orgs,
                CancellationToken ct) =>
            {
                var dto = await orgs.CreateAsync(new CreateOrganizationRequest(body.Name!), ct);
                return Results.Ok(ApiResult<OrganizationDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "Organization"));

        // GET /api/v1/organizations — paginated/sort/search list (Req 9.3). Non-archived only.
        group.MapGet("/", async (
                string? search,
                string? sort,
                int? page,
                int? pageSize,
                IOrganizationService orgs,
                CancellationToken ct) =>
            {
                var parameters = new ListQueryParameters
                {
                    Page = page,
                    PageSize = pageSize,
                    Search = string.IsNullOrWhiteSpace(search) ? null : search,
                    Sort = EndpointSort.Parse(sort),
                };

                var data = await orgs.ListAsync(parameters, ct);
                return Results.Ok(ApiResult<PagedData<OrganizationDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Organization"));

        // GET /api/v1/organizations/{id} — fetch one. Unknown id → NOT_FOUND.
        group.MapGet("/{id:guid}", async (
                Guid id,
                IOrganizationService orgs,
                CancellationToken ct) =>
            {
                var dto = await orgs.GetAsync(id, includeArchived: false, ct)
                    ?? throw new NotFoundException("Organization not found.");
                return Results.Ok(ApiResult<OrganizationDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Organization"));

        // PUT /api/v1/organizations/{id} — edit. Unknown id → NOT_FOUND.
        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdateOrganizationBody body,
                IOrganizationService orgs,
                CancellationToken ct) =>
            {
                var dto = await orgs.UpdateAsync(id, new UpdateOrganizationRequest(body.Name), ct);
                return Results.Ok(ApiResult<OrganizationDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Organization"));

        // DELETE /api/v1/organizations/{id} — soft-delete. Retained + retrievable with includeArchived.
        group.MapDelete("/{id:guid}", async (
                Guid id,
                IOrganizationService orgs,
                CancellationToken ct) =>
            {
                await orgs.DeleteAsync(id, ct);
                return Results.Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Delete, "Organization"));

        return app;
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
