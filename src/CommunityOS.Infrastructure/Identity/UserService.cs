using CommunityOS.Application.Abstractions.Audit;
using CommunityOS.Application.Audit;
using CommunityOS.Application.Exceptions;
using CommunityOS.Application.Identity;
using CommunityOS.Application.Querying;
using CommunityOS.Contracts.Envelope;
using CommunityOS.Domain.Identity;
using CommunityOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommunityOS.Infrastructure.Identity;

/// <summary>
/// EF-backed <see cref="IUserService"/> (Req 3.1, 3.7). Create/edit/soft-delete/list users.
///
/// <para><b>Concurrency-safe uniqueness (Req 3.1).</b> Duplicate email/phone is NOT pre-checked
/// with a read-then-write (which would race two concurrent creates past the gap); the write relies
/// on the filtered unique indexes and translates a violation into a CONFLICT via
/// <see cref="UniqueConstraintGuard"/>.</para>
///
/// <para><b>Security.</b> The password hash is a column only here (set at create from an
/// already-hashed value supplied by Auth_Service, Task 5.2); it is never returned in a
/// <see cref="UserDto"/> and never written to the audit detail (steering: never log secrets).</para>
///
/// <para><b>Audit (Req 6.1).</b> Create/edit/delete each append an <see cref="AuditEntry"/>
/// recording the acting user (stamped by Audit_Service), the action and the user id — never the
/// hash. <b>Soft-delete (Req 3.7):</b> delete archives the row (EF global filter + the audit
/// interceptor turn Remove into a soft delete); it stays retrievable via
/// <c>IgnoreQueryFilters</c> for audit.</para>
/// </summary>
public sealed class UserService(AppDbContext db, IAuditService audit, IPageSizePolicy pageSizePolicy, IAsyncQueryExecutor executor)
    : IUserService
{
    private static readonly ListQueryConfiguration<User> QueryConfig =
        new ListQueryConfiguration<User>()
            .WithDefaultSort(u => u.Email)
            .Sortable("email", u => u.Email)
            .Sortable("phone", u => u.Phone)
            .Sortable("status", u => u.Status)
            .Filterable("status", u => u.Status)
            .Filterable("mfaEnabled", u => u.MfaEnabled)
            .Searchable(u => u.Email)
            .Searchable(u => u.Phone);

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireContact(request.Email, request.Phone);

        var user = new User
        {
            Email = Normalize(request.Email),
            Phone = Normalize(request.Phone),
            PasswordHash = request.PasswordHash,
            MfaEnabled = request.MfaEnabled,
            Status = UserStatus.Active,
        };
        db.Users.Add(user);

        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken),
            "A user with the same email or phone already exists.");

        await audit.AppendAsync(
            new AuditEntry("Create", "User", user.Id.ToString(), CommunityId: null, Detail: null),
            cancellationToken);

        return ToDto(user);
    }

    public async Task<UserDto> UpdateAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        if (request.Email is not null) user.Email = Normalize(request.Email);
        if (request.Phone is not null) user.Phone = Normalize(request.Phone);
        if (request.Status is not null) user.Status = request.Status.Value;
        if (request.MfaEnabled is not null) user.MfaEnabled = request.MfaEnabled.Value;

        RequireContact(user.Email, user.Phone);

        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken),
            "A user with the same email or phone already exists.");

        await audit.AppendAsync(
            new AuditEntry("Update", "User", user.Id.ToString(), CommunityId: null, Detail: null),
            cancellationToken);

        return ToDto(user);
    }

    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        // Remove is converted to a soft delete by the audit interceptor (Req 3.7).
        db.Users.Remove(user);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Delete", "User", user.Id.ToString(), CommunityId: null, Detail: null),
            cancellationToken);
    }

    public async Task<UserDto?> GetAsync(Guid userId, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        IQueryable<User> q = db.Users.AsNoTracking();
        if (includeArchived)
        {
            q = q.IgnoreQueryFilters(); // retrieve soft-deleted for audit (Req 3.7)
        }

        var user = await q.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return user is null ? null : ToDto(user);
    }

    public Task<PagedData<UserDto>> ListAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // The global soft-delete filter excludes archived users from ordinary lists (Req 3.7).
        return db.Users.AsNoTracking().ToPagedResultAsync(
            parameters, QueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
    }

    private static UserDto ToDto(User u) =>
        new(u.Id, u.Email, u.Phone, u.Status, u.MfaEnabled, u.IsDeleted);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void RequireContact(string? email, string? phone)
    {
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phone))
        {
            throw new ValidationException("email", "A user must have an email or a phone.");
        }
    }
}
