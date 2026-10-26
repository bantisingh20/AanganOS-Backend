using System.ComponentModel.DataAnnotations;
using UrbanNest.Api.Validation;
using UrbanNest.Application.Identity;
using UrbanNest.Contracts.Envelope;
using UrbanNest.Domain.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace UrbanNest.Api.Endpoints;

public static class MenuEndpoints
{
    public static IEndpointRouteBuilder MapMenuEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/menu", async (IMenuService menu, CancellationToken ct) =>
            {
                var data = await menu.GetMenuAsync(ct);
                return Results.Ok(ApiResult<IReadOnlyList<MenuNodeDto>>.Ok(data, string.Empty));
            })
            .RequireAuthorization();

        var group = app.MapGroup("/api/v1/menu-items").RequireAuthorization();

        group.MapGet("/", async (string? surface, IMenuService menu, CancellationToken ct) =>
            {
                MenuSurface? s = Enum.TryParse<MenuSurface>(surface, ignoreCase: true, out var parsed) ? parsed : null;
                var data = await menu.ListAllAsync(s, ct);
                return Results.Ok(ApiResult<IReadOnlyList<MenuItemDto>>.Ok(data, string.Empty));
            });

        group.MapPost("/", async (MenuItemBody body, IMenuService menu, CancellationToken ct) =>
            {
                var dto = await menu.CreateAsync(new CreateMenuItemRequest(
                    body.ParentId, body.Key ?? string.Empty, body.Label ?? string.Empty, body.Icon, body.Route,
                    body.Surface, body.RequiredPermissionActions, body.RequiredPermissionResource,
                    body.SortOrder, body.IsActive, body.ShowInSidebar), ct);
                return Results.Ok(ApiResult<MenuItemDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>();

        group.MapPut("/{id:guid}", async (Guid id, MenuItemBody body, IMenuService menu, CancellationToken ct) =>
            {
                var dto = await menu.UpdateAsync(id, new UpdateMenuItemRequest(
                    body.ParentId, body.Label ?? string.Empty, body.Icon, body.Route,
                    body.Surface, body.RequiredPermissionActions, body.RequiredPermissionResource,
                    body.SortOrder, body.IsActive, body.ShowInSidebar), ct);
                return Results.Ok(ApiResult<MenuItemDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>();

        group.MapDelete("/{id:guid}", async (Guid id, IMenuService menu, CancellationToken ct) =>
            {
                await menu.DeleteAsync(id, ct);
                return Results.Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
            });

        return app;
    }

    public sealed class MenuItemBody
    {
        public Guid? ParentId { get; init; }

        [MaxLength(128)]
        public string? Key { get; init; }

        [Required(AllowEmptyStrings = false)]
        [MaxLength(128)]
        public string? Label { get; init; }

        [MaxLength(64)]
        public string? Icon { get; init; }

        [MaxLength(256)]
        public string? Route { get; init; }

        public MenuSurface Surface { get; init; }

        public List<int>? RequiredPermissionActions { get; init; }

        [MaxLength(128)]
        public string? RequiredPermissionResource { get; init; }

        public int SortOrder { get; init; }

        public bool IsActive { get; init; } = true;

        public bool ShowInSidebar { get; init; } = true;
    }
}
