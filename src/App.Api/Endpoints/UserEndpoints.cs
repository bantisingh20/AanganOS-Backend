using System.ComponentModel.DataAnnotations;
using App.Api.Security;
using App.Api.Validation;
using App.Application.Exceptions;
using App.Application.Identity;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;

namespace App.Api.Endpoints;

/// <summary>
/// HTTP surface over <see cref="IUserService"/> (Task 5.1) — user management (Req 3.1, 3.7).
/// Every endpoint uses the <see cref="ApiResult{T}"/> envelope (Req 9.1), carries a
/// <see cref="RequirePermissionAttribute"/> on the <c>User</c> resource type for its action
/// (Req 4.1), validates request bodies at the trust boundary (Req 9.2), and the list is paginated
/// through the shared query infra (Req 9.3). Service CONFLICT/NOT_FOUND exceptions flow through the
/// terminal exception middleware; the password hash is never returned (UserDto excludes it).
/// </summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users");

        // POST /api/v1/users — create (Req 3.1). Duplicate email/phone → CONFLICT from the service.
        group.MapPost("/", async (
                CreateUserBody body,
                IUserService users,
                CancellationToken ct) =>
            {
                var dto = await users.CreateAsync(new CreateUserRequest(body.Email, body.Phone, body.PasswordHash, body.MfaEnabled), ct);
                return Results.Ok(ApiResult<UserDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "User"));

        // GET /api/v1/users — paginated/filter/sort/search list (Req 9.3). Non-archived only.
        group.MapGet("/", async (
                string? search,
                string? sort,
                UserStatus? status,
                int? page,
                int? pageSize,
                IUserService users,
                CancellationToken ct) =>
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
                return Results.Ok(ApiResult<PagedData<UserDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "User"));

        // GET /api/v1/users/{id} — fetch one (Req 3.1). Unknown id → NOT_FOUND.
        group.MapGet("/{id:guid}", async (
                Guid id,
                IUserService users,
                CancellationToken ct) =>
            {
                var dto = await users.GetAsync(id, includeArchived: false, ct)
                    ?? throw new NotFoundException("User not found.");
                return Results.Ok(ApiResult<UserDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "User"));

        // PUT /api/v1/users/{id} — edit (Req 3.1). Unknown id → NOT_FOUND; duplicate → CONFLICT.
        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdateUserBody body,
                IUserService users,
                CancellationToken ct) =>
            {
                var dto = await users.UpdateAsync(id, new UpdateUserRequest(body.Email, body.Phone, body.Status, body.MfaEnabled), ct);
                return Results.Ok(ApiResult<UserDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "User"));

        // DELETE /api/v1/users/{id} — soft-delete (Req 3.7). Retained + retrievable with includeArchived.
        group.MapDelete("/{id:guid}", async (
                Guid id,
                IUserService users,
                CancellationToken ct) =>
            {
                await users.DeleteAsync(id, ct);
                return Results.Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Delete, "User"));

        return app;
    }

    public sealed class CreateUserBody
    {
        [EmailAddress(ErrorMessage = "email must be a valid email address")]
        [MaxLength(256)]
        public string? Email { get; init; }

        [Phone(ErrorMessage = "phone must be a valid phone number")]
        [MaxLength(32)]
        public string? Phone { get; init; }

        // Pre-hashed credential produced by Auth_Service; a client never sends a plaintext password here.
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
    }
}
