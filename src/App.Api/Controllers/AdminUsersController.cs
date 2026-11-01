using System.ComponentModel.DataAnnotations;
using App.Api.Endpoints;
using App.Application.Identity;
using App.Application.Querying;
using App.Contracts.Envelope;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

[ApiController]
[Route("api/v1/admin-users")]
[Authorize]
public sealed class AdminUsersController(IAdminUserService admins) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var parameters = new ListQueryParameters
        {
            Page = page,
            PageSize = pageSize,
            Search = string.IsNullOrWhiteSpace(search) ? null : search,
        };
        var data = await admins.ListAsync(parameters, ct);
        return Ok(ApiResult<PagedData<AdminUserDto>>.Ok(data, string.Empty));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAdminUserBody body, CancellationToken ct)
    {
        var dto = await admins.CreateAsync(new CreateAdminUserRequest(
            body.Identifier ?? string.Empty,
            body.Password ?? string.Empty,
            body.RoleName ?? string.Empty,
            body.OrganizationId,
            body.CommunityId,
            body.Name), ct);
        return Ok(ApiResult<AdminUserDto>.Ok(dto, string.Empty));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAdminUserBody body, CancellationToken ct)
    {
        var dto = await admins.UpdateAsync(id, new UpdateAdminUserRequest(
            body.Name,
            string.IsNullOrEmpty(body.Password) ? null : body.Password,
            body.RoleName ?? string.Empty,
            body.Status ?? string.Empty,
            body.OrganizationId,
            body.CommunityId), ct);
        return Ok(ApiResult<AdminUserDto>.Ok(dto, string.Empty));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await admins.DeleteAsync(id, ct);
        return Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
    }

    public sealed class CreateAdminUserBody
    {
        [MaxLength(200)]
        public string? Name { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "identifier is required")]
        [MaxLength(256)]
        public string? Identifier { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "password is required")]
        [MaxLength(256)]
        public string? Password { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "roleName is required")]
        [MaxLength(128)]
        public string? RoleName { get; init; }

        public Guid? OrganizationId { get; init; }

        public Guid? CommunityId { get; init; }
    }

    public sealed class UpdateAdminUserBody
    {
        [MaxLength(200)]
        public string? Name { get; init; }

        [MaxLength(256)]
        public string? Password { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "roleName is required")]
        [MaxLength(128)]
        public string? RoleName { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "status is required")]
        [MaxLength(32)]
        public string? Status { get; init; }

        public Guid? OrganizationId { get; init; }

        public Guid? CommunityId { get; init; }
    }
}
