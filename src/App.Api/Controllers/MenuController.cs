using System.ComponentModel.DataAnnotations;
using App.Api.Endpoints;
using App.Application.Identity;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

[ApiController]
[Authorize]
public sealed class MenuController(IMenuService menu) : ControllerBase
{
    [HttpGet("api/v1/menu")]
    public async Task<IActionResult> GetMenu(CancellationToken ct)
    {
        var data = await menu.GetMenuAsync(ct);
        return Ok(ApiResult<IReadOnlyList<MenuNodeDto>>.Ok(data, string.Empty));
    }

    [HttpGet("api/v1/menu-items")]
    public async Task<IActionResult> ListItems([FromQuery] string? surface, CancellationToken ct)
    {
        MenuSurface? s = Enum.TryParse<MenuSurface>(surface, ignoreCase: true, out var parsed) ? parsed : null;
        var data = await menu.ListAllAsync(s, ct);
        return Ok(ApiResult<IReadOnlyList<MenuItemDto>>.Ok(data, string.Empty));
    }

    [HttpPost("api/v1/menu-items")]
    public async Task<IActionResult> CreateItem([FromBody] MenuItemBody body, CancellationToken ct)
    {
        var dto = await menu.CreateAsync(new CreateMenuItemRequest(
            body.ParentId, body.Key ?? string.Empty, body.Label ?? string.Empty, body.Icon, body.Route,
            body.Surface, body.RequiredPermissionActions, body.RequiredPermissionResource,
            body.SortOrder, body.IsActive, body.ShowInSidebar), ct);
        return Ok(ApiResult<MenuItemDto>.Ok(dto, string.Empty));
    }

    [HttpPut("api/v1/menu-items/{id:guid}")]
    public async Task<IActionResult> UpdateItem(Guid id, [FromBody] MenuItemBody body, CancellationToken ct)
    {
        var dto = await menu.UpdateAsync(id, new UpdateMenuItemRequest(
            body.ParentId, body.Label ?? string.Empty, body.Icon, body.Route,
            body.Surface, body.RequiredPermissionActions, body.RequiredPermissionResource,
            body.SortOrder, body.IsActive, body.ShowInSidebar), ct);
        return Ok(ApiResult<MenuItemDto>.Ok(dto, string.Empty));
    }

    [HttpDelete("api/v1/menu-items/{id:guid}")]
    public async Task<IActionResult> DeleteItem(Guid id, CancellationToken ct)
    {
        await menu.DeleteAsync(id, ct);
        return Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
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
