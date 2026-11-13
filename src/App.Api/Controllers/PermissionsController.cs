using App.Api.Security;
using App.Application.Identity;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

[ApiController]
[Route("api/v1/permissions")]
public sealed class PermissionsController(IPermissionCatalog catalog) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionAction.View, "Permission")]
    public async Task<IActionResult> List([FromQuery] string? resourceType, CancellationToken ct)
    {
        var items = await catalog.ListAsync(string.IsNullOrWhiteSpace(resourceType) ? null : resourceType, ct);
        return Ok(ApiResult<IReadOnlyList<PermissionDto>>.Ok(items, string.Empty));
    }
}
