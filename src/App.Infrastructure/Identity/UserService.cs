using App.Application.Abstractions.Audit;
using App.Application.Audit;
using App.Application.Exceptions;
using App.Application.Identity;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Identity;

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
            Name = request.Name?.Trim(),
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
        if (request.Name is not null) user.Name = request.Name.Trim();
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
