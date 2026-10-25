using UrbanNest.Api.Security;
using UrbanNest.Application.Identity;
using UrbanNest.Contracts.Envelope;
using UrbanNest.Domain.Identity;

namespace UrbanNest.Api.Endpoints;

/// <summary>
/// Read surface over the <see cref="IPermissionCatalog"/> (Task 5.1) — Req 3.2. Lists the
/// registered (ResourceType, Action) permissions an admin can grant to a role; an optional
/// <c>resourceType</c> query narrows it. Uses the <see cref="ApiResult{T}"/> envelope (Req 9.1) and
/// requires the <see cref="PermissionAction.View"/> permission on the <c>Permission</c> resource
/// type (Req 4.1).
///
/// <para>The catalog is a small, bounded reference set (the seeded permission vocabulary), so the
/// read returns the full list rather than paging — the shape mirrors other reference reads.</para>
/// </summary>
public static class PermissionEndpoints
{
    public static IEndpointRouteBuilder MapPermissionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/permissions", async (
                string? resourceType,
                IPermissionCatalog catalog,
                CancellationToken ct) =>
            {
                var items = await catalog.ListAsync(
                    string.IsNullOrWhiteSpace(resourceType) ? null : resourceType, ct);
                return Results.Ok(ApiResult<IReadOnlyList<PermissionDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Permission"));

        return app;
    }
}
