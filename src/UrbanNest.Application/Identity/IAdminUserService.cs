using UrbanNest.Application.Querying;
using UrbanNest.Contracts.Envelope;

namespace UrbanNest.Application.Identity;

public sealed record AdminUserScopeDto(Guid? CommunityId, Guid? OrganizationId);

public sealed record AdminUserDto(
    Guid Id,
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
    Guid? CommunityId);

public interface IAdminUserService
{
    Task<PagedData<AdminUserDto>> ListAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default);

    Task<AdminUserDto> CreateAsync(CreateAdminUserRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default);
}
