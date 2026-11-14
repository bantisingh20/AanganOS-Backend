using System.ComponentModel.DataAnnotations;
using App.Api.Endpoints;
using App.Api.Security;
using App.Application.Exceptions;
using App.Application.Identity;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
public sealed class UsersController(IUserService users) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "User")]
    public async Task<IActionResult> Create([FromBody] CreateUserBody body, CancellationToken ct)
    {
        var dto = await users.CreateAsync(new CreateUserRequest(body.Email, body.Phone, body.PasswordHash, body.MfaEnabled), ct);
        return Ok(ApiResult<UserDto>.Ok(dto, string.Empty));
    }

    [HttpGet]
    [RequirePermission(PermissionAction.View, "User")]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] UserStatus? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        if (status is not null)
        {
            filters.Add(new FilterTerm("status", FilterOperator.Equals, status.Value.ToString()));
        }

        var parameters = new ListQueryParameters
        {
            Page = page,
            PageSize = pageSize,
            Search = string.IsNullOrWhiteSpace(search) ? null : search,
            Filters = filters,
            Sort = EndpointSort.Parse(sort),
        };

        var data = await users.ListAsync(parameters, ct);
        return Ok(ApiResult<PagedData<UserDto>>.Ok(data, string.Empty));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionAction.View, "User")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var dto = await users.GetAsync(id, includeArchived: false, ct)
            ?? throw new NotFoundException("User not found.");
        return Ok(ApiResult<UserDto>.Ok(dto, string.Empty));
    }

    [HttpPut("{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "User")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserBody body, CancellationToken ct)
    {
        var dto = await users.UpdateAsync(id, new UpdateUserRequest(body.Email, body.Phone, body.Status, body.MfaEnabled, body.Name), ct);
        return Ok(ApiResult<UserDto>.Ok(dto, string.Empty));
    }

    [HttpDelete("{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Delete, "User")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await users.DeleteAsync(id, ct);
        return Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
    }

    public sealed class CreateUserBody
    {
        [EmailAddress(ErrorMessage = "email must be a valid email address")]
        [MaxLength(256)]
        public string? Email { get; init; }

        [Phone(ErrorMessage = "phone must be a valid phone number")]
        [MaxLength(32)]
        public string? Phone { get; init; }

        [MaxLength(512)]
        public string? PasswordHash { get; init; }

        public bool MfaEnabled { get; init; }
    }

    public sealed class UpdateUserBody
    {
        [EmailAddress(ErrorMessage = "email must be a valid email address")]
        [MaxLength(256)]
        public string? Email { get; init; }

        [Phone(ErrorMessage = "phone must be a valid phone number")]
        [MaxLength(32)]
        public string? Phone { get; init; }

        public UserStatus? Status { get; init; }

        public bool? MfaEnabled { get; init; }

        [MaxLength(200)]
        public string? Name { get; init; }
    }
}
