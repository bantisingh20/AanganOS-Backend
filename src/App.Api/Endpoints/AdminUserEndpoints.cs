using System.ComponentModel.DataAnnotations;
using App.Api.Validation;
using App.Application.Identity;
using App.Application.Querying;
using App.Contracts.Envelope;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace App.Api.Endpoints;

public static class AdminUserEndpoints
{
    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin-users").RequireAuthorization();

        group.MapGet("/", async (
                string? search,
                int? page,
                int? pageSize,
                IAdminUserService admins,
                CancellationToken ct) =>
            {
                var parameters = new ListQueryParameters
                {
                    Page = page,
                    PageSize = pageSize,
                    Search = string.IsNullOrWhiteSpace(search) ? null : search,
                };
                var data = await admins.ListAsync(parameters, ct);
                return Results.Ok(ApiResult<PagedData<AdminUserDto>>.Ok(data, string.Empty));
            });

        group.MapPost("/", async (CreateAdminUserBody body, IAdminUserService admins, CancellationToken ct) =>
            {
                var dto = await admins.CreateAsync(new CreateAdminUserRequest(
                    body.Identifier ?? string.Empty,
                    body.Password ?? string.Empty,
                    body.RoleName ?? string.Empty,
                    body.OrganizationId,
                    body.CommunityId,
                    body.Name), ct);
                return Results.Ok(ApiResult<AdminUserDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>();

        group.MapPut("/{id:guid}", async (Guid id, UpdateAdminUserBody body, IAdminUserService admins, CancellationToken ct) =>
            {
                var dto = await admins.UpdateAsync(id, new UpdateAdminUserRequest(
                    body.Name,
                    string.IsNullOrEmpty(body.Password) ? null : body.Password,
                    body.RoleName ?? string.Empty,
                    body.Status ?? string.Empty,
                    body.OrganizationId,
                    body.CommunityId), ct);
                return Results.Ok(ApiResult<AdminUserDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>();

        group.MapDelete("/{id:guid}", async (Guid id, IAdminUserService admins, CancellationToken ct) =>
            {
                await admins.DeleteAsync(id, ct);
                return Results.Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
            });

        return app;
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
