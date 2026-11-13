using App.Application.Querying;
using App.Contracts.Envelope;

namespace App.Application.Identity;

public sealed record AdminUserScopeDto(Guid? CommunityId, Guid? OrganizationId);

public sealed record AdminUserDto(
    Guid Id,
    string? Name,
    string? Email,
    string? Phone,
    string Status,
    IReadOnlyList<string> Roles,
    IReadOnlyList<AdminUserRoleScopeDto> Assignments);

public sealed record AdminUserRoleScopeDto(Guid RoleId, string RoleName, Guid? CommunityId);

public sealed record CreateAdminUserRequest(
    string Identifier,
    string Password,
    string RoleName,
    Guid? OrganizationId,
    Guid? CommunityId,
    string? Name);

public sealed record UpdateAdminUserRequest(
    string? Name,
    string? Password,
    string RoleName,
    string Status,
    Guid? OrganizationId,
    Guid? CommunityId);

public interface IAdminUserService
{
    Task<PagedData<AdminUserDto>> ListAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default);

    Task<AdminUserDto> CreateAsync(CreateAdminUserRequest request, CancellationToken cancellationToken = default);

    Task<AdminUserDto> UpdateAsync(Guid userId, UpdateAdminUserRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default);
}
