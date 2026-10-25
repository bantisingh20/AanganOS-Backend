using UrbanNest.Application.Querying;
using UrbanNest.Contracts.Envelope;

namespace UrbanNest.Application.Identity;

/// <summary>
/// User management (Req 3.1, 3.7). Create/edit/soft-delete/list users. Each mutation is recorded in
/// the audit trail via the Audit_Service (Req 6.1); the password hash is never returned or logged.
/// Soft-deleted users are excluded from <see cref="ListAsync"/> but remain retrievable for audit
/// via <see cref="GetAsync"/> with <c>includeArchived</c> (Req 3.7).
/// </summary>
public interface IUserService
{
    /// <summary>Create a user with the submitted attributes (Req 3.1). Duplicate email/phone → conflict.</summary>
    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    /// <summary>Edit a user's attributes (Req 3.1). Duplicate email/phone → conflict; unknown id → not found.</summary>
    Task<UserDto> UpdateAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken = default);

    /// <summary>Soft-delete (archive) a user (Req 3.7). The row is retained and still retrievable with <c>includeArchived</c>.</summary>
    Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Fetch a single user. When <paramref name="includeArchived"/> is true a soft-deleted user is returned (Req 3.7).</summary>
    Task<UserDto?> GetAsync(Guid userId, bool includeArchived = false, CancellationToken cancellationToken = default);

    /// <summary>Paginated/filter/sort/search list of (non-archived) users (Req 9.3).</summary>
    Task<PagedData<UserDto>> ListAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default);
}
