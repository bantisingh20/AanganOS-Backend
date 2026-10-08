using CommunityOS.Api.Security;
using CommunityOS.Application.Community;
using CommunityOS.Application.Exceptions;
using CommunityOS.Application.Querying;
using CommunityOS.Contracts.Envelope;
using CommunityOS.Domain.Identity;

namespace CommunityOS.Api.Endpoints;

/// <summary>
/// HTTP surface over the MVP notices slice (<see cref="IAnnouncementService"/>) — the community
/// notice board. Mirrors the established minimal-API style of <see cref="ParcelEndpoints"/>: the
/// <see cref="ApiResult{T}"/> envelope (Req 9.1), a <see cref="RequirePermissionAttribute"/> on
/// <c>(View, Announcement)</c> (Req 4.1), and the shared paginated list infra
/// (<see cref="ListQueryParameters"/> + the service's allow-listed query config, Req 9.3). The board
/// is scoped to the caller's authorized communities by the EF global tenant query filter — no
/// bespoke predicate here. Read-only in the MVP (no publish endpoint).
/// </summary>
public static class AnnouncementEndpoints
{
    public static IEndpointRouteBuilder MapAnnouncementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/announcements");

        // GET — paginated/filter(category)/sort/search list, newest-first.
        group.MapGet("/", async (
                string? search,
                string? sort,
                string? category,
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

                var data = await announcements.ListAnnouncementsAsync(parameters, ct);
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

        return app;
    }
}
