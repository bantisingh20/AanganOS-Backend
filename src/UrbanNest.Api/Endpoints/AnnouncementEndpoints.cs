using System.ComponentModel.DataAnnotations;
using UrbanNest.Api.Security;
using UrbanNest.Api.Validation;
using UrbanNest.Application.Community;
using UrbanNest.Application.Exceptions;
using UrbanNest.Application.Querying;
using UrbanNest.Contracts.Envelope;
using UrbanNest.Domain.Identity;

namespace UrbanNest.Api.Endpoints;

public static class AnnouncementEndpoints
{
    public static IEndpointRouteBuilder MapAnnouncementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/announcements");

        // GET — paginated/filter(category)/sort/search list, newest-first. Residents get active-only;
        // an admin management view passes includeInactive=true to also see deactivated notices.
        group.MapGet("/", async (
                string? search,
                string? sort,
                string? category,
                bool? includeInactive,
                int? page,
                int? pageSize,
                IAnnouncementService announcements,
                CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                if (!string.IsNullOrWhiteSpace(category))
                {
                    filters.Add(new FilterTerm("category", FilterOperator.Equals, category));
                }

                var parameters = new ListQueryParameters
                {
                    Page = page,
                    PageSize = pageSize,
                    Search = string.IsNullOrWhiteSpace(search) ? null : search,
                    Filters = filters,
                    Sort = EndpointSort.Parse(sort),
                };

                var data = await announcements.ListAnnouncementsAsync(parameters, includeInactive ?? false, ct);
                return Results.Ok(ApiResult<PagedData<AnnouncementDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Announcement"));

        // GET /{id} — fetch one, tenant-scoped. Unknown/foreign id → NOT_FOUND.
        group.MapGet("/{id:guid}", async (
                Guid id,
                IAnnouncementService announcements,
                CancellationToken ct) =>
            {
                var dto = await announcements.GetAnnouncementAsync(id, ct)
                    ?? throw new NotFoundException("Announcement not found.");
                return Results.Ok(ApiResult<AnnouncementDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Announcement"));

        // POST — publish a new announcement (admin). Invalid category → VALIDATION_ERROR.
        group.MapPost("/", async (
                CreateAnnouncementBody body,
                IAnnouncementService announcements,
                CancellationToken ct) =>
            {
                var dto = await announcements.CreateAnnouncementAsync(
                    new CreateAnnouncementRequest(body.CommunityId, body.Title!, body.Body!, body.Category!), ct);
                return Results.Ok(ApiResult<AnnouncementDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "Announcement"));

        // PUT /{id} — edit title/body/category (admin). Unknown id → NOT_FOUND.
        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdateAnnouncementBody body,
                IAnnouncementService announcements,
                CancellationToken ct) =>
            {
                var dto = await announcements.UpdateAnnouncementAsync(
                    id, new UpdateAnnouncementRequest(body.Title, body.Body, body.Category), ct);
                return Results.Ok(ApiResult<AnnouncementDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Announcement"));

        // POST /{id}/activate — show on the board again (admin). Edit-gated.
        group.MapPost("/{id:guid}/activate", async (
                Guid id,
                IAnnouncementService announcements,
                CancellationToken ct) =>
            {
                var dto = await announcements.SetAnnouncementActiveAsync(id, true, ct);
                return Results.Ok(ApiResult<AnnouncementDto>.Ok(dto, string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Announcement"));

        // POST /{id}/deactivate — hide from the board (admin), reversibly. Edit-gated.
        group.MapPost("/{id:guid}/deactivate", async (
                Guid id,
                IAnnouncementService announcements,
                CancellationToken ct) =>
            {
                var dto = await announcements.SetAnnouncementActiveAsync(id, false, ct);
                return Results.Ok(ApiResult<AnnouncementDto>.Ok(dto, string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Announcement"));

        // DELETE /{id} — soft-delete (admin). Retained + traceable.
        group.MapDelete("/{id:guid}", async (
                Guid id,
                IAnnouncementService announcements,
                CancellationToken ct) =>
            {
                await announcements.DeleteAnnouncementAsync(id, ct);
                return Results.Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Delete, "Announcement"));

        return app;
    }

    public sealed class CreateAnnouncementBody
    {
        [Required(ErrorMessage = "communityId is required")]
        public Guid CommunityId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "title is required")]
        [MaxLength(200)]
        public string? Title { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "body is required")]
        [MaxLength(4000)]
        public string? Body { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "category is required")]
        [MaxLength(50)]
        public string? Category { get; init; }
    }

    public sealed class UpdateAnnouncementBody
    {
        [MaxLength(200)]
        public string? Title { get; init; }

        [MaxLength(4000)]
        public string? Body { get; init; }

        [MaxLength(50)]
        public string? Category { get; init; }
    }
}
