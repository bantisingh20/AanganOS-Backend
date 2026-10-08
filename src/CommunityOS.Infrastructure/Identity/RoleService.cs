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
/// EF-backed <see cref="IRoleService"/> (Req 3.5, 3.6, 3.7). Defines a role with a set of
/// (Action, ResourceType) permission grants, redefines it, soft-deletes it, and lists roles.
///
/// <para><b>Concurrency-safe uniqueness (Req 3.6).</b> A duplicate role name within a scope is NOT
/// pre-checked with a read-then-write (which would race two concurrent defines past the gap); the
/// write relies on the filtered unique indexes (UX_Role_Platform/Community_Name_Scope) and
/// translates a violation into a CONFLICT via <see cref="UniqueConstraintGuard"/>.</para>
///
/// <para><b>Permission resolution (Req 3.5).</b> The requested grants are expressed as
/// (Action, ResourceType) pairs — the vocabulary callers speak — and resolved to catalog
/// <see cref="Permission"/> ids in a single <c>IN</c> query (no N+1). An unknown pair is a
/// validation error, never a silent skip, so a role can't be defined with a permission that
/// doesn't exist in the seeded/registered catalog (Req 3.2).</para>
///
/// <para><b>Audit (Req 6.1).</b> Define/update/delete each append an <see cref="AuditEntry"/>.
/// <b>Soft-delete (Req 3.7):</b> delete archives the row (retrievable via <c>IgnoreQueryFilters</c>).
/// Redefining replaces the permission grant set wholesale (plain join rows are hard-replaced).</para>
/// </summary>
public sealed class RoleService(AppDbContext db, IAuditService audit, IPageSizePolicy pageSizePolicy, IAsyncQueryExecutor executor)
    : IRoleService
{
    private static readonly ListQueryConfiguration<Role> QueryConfig =
        new ListQueryConfiguration<Role>()
            .WithDefaultSort(r => r.Name)
            .Sortable("name", r => r.Name)
            .Sortable("scope", r => r.Scope)
            .Filterable("scope", r => r.Scope)
            .Filterable("communityId", r => r.CommunityId)
            .Searchable(r => r.Name);

    private const string DuplicateMessage = "A role with the same name already exists in this scope.";

    public async Task<RoleDto> DefineAsync(DefineRoleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateScope(request);

        var permissions = await ResolveGrantsAsync(request.Permissions, cancellationToken);

        var role = new Role
        {
            Name = request.Name.Trim(),
            Scope = request.Scope,
            CommunityId = request.CommunityId,
            RolePermissions = permissions.Select(p => new RolePermission { PermissionId = p.Id }).ToList(),
        };
        db.Roles.Add(role);

        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), DuplicateMessage);

        await audit.AppendAsync(
            new AuditEntry("Create", "Role", role.Id.ToString(), role.CommunityId, Detail: null),
            cancellationToken);

        return ToDto(role, permissions);
    }

    public async Task<RoleDto> UpdateAsync(Guid roleId, DefineRoleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateScope(request);

        var role = await db.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken)
            ?? throw new NotFoundException("Role not found.");

        var permissions = await ResolveGrantsAsync(request.Permissions, cancellationToken);

        role.Name = request.Name.Trim();
        role.Scope = request.Scope;
        role.CommunityId = request.CommunityId;

        // Replace the grant set wholesale: drop existing join rows, add the resolved set.
        db.RemoveRange(role.RolePermissions);
        role.RolePermissions = permissions.Select(p => new RolePermission { RoleId = role.Id, PermissionId = p.Id }).ToList();

        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), DuplicateMessage);

        await audit.AppendAsync(
            new AuditEntry("Update", "Role", role.Id.ToString(), role.CommunityId, Detail: null),
            cancellationToken);

        return ToDto(role, permissions);
    }

    public async Task DeleteAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken)
            ?? throw new NotFoundException("Role not found.");

        // Remove is converted to a soft delete by the audit interceptor (Req 3.7).
        db.Roles.Remove(role);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Delete", "Role", role.Id.ToString(), role.CommunityId, Detail: null),
            cancellationToken);
    }

    public async Task<RoleDto?> GetAsync(Guid roleId, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        IQueryable<Role> q = db.Roles.AsNoTracking()
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission);
        if (includeArchived)
        {
            q = q.IgnoreQueryFilters(); // retrieve soft-deleted for audit (Req 3.7)
        }

        var role = await q.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);
        return role is null ? null : ToDto(role, role.RolePermissions.Select(rp => rp.Permission!));
    }

    public Task<PagedData<RoleDto>> ListAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // The global soft-delete filter excludes archived roles from ordinary lists (Req 3.7).
        // Project grants in the same query (no N+1) — the list projection is evaluated in SQL.
        return db.Roles.AsNoTracking()
            .Select(r => new RoleProjection(
                r.Id, r.Name, r.Scope, r.CommunityId, r.IsDeleted,
                r.RolePermissions.Select(rp => new PermissionGrant(rp.Permission!.Action, rp.Permission.ResourceType)).ToList()))
            .ToPagedResultAsync(parameters, ProjectionConfig, p =>
                    new RoleDto(p.Id, p.Name, p.Scope, p.CommunityId, p.Permissions, p.IsDeleted),
                pageSizePolicy, executor, cancellationToken);
    }

    // Allow-list for the projected shape used by ListAsync (same fields as QueryConfig).
    private static readonly ListQueryConfiguration<RoleProjection> ProjectionConfig =
        new ListQueryConfiguration<RoleProjection>()
            .WithDefaultSort(r => r.Name)
            .Sortable("name", r => r.Name)
            .Sortable("scope", r => r.Scope)
            .Filterable("scope", r => r.Scope)
            .Filterable("communityId", r => r.CommunityId)
            .Searchable(r => r.Name);

    private sealed record RoleProjection(
        Guid Id, string Name, RoleScope Scope, Guid? CommunityId, bool IsDeleted,
        IReadOnlyCollection<PermissionGrant> Permissions);

    /// <summary>
    /// Resolve the requested (Action, ResourceType) grants to catalog permissions in one query
    /// (Req 3.5). Unknown pair → validation error (Req 3.2). De-duplicates the request first.
    /// </summary>
    private async Task<IReadOnlyList<Permission>> ResolveGrantsAsync(
        IReadOnlyCollection<PermissionGrant> grants, CancellationToken cancellationToken)
    {
        if (grants is null || grants.Count == 0)
        {
            return [];
        }

        var wanted = grants
            .Select(g => new { Action = g.Action, ResourceType = (g.ResourceType ?? string.Empty).Trim() })
            .Distinct()
            .ToList();

        var resourceTypes = wanted.Select(w => w.ResourceType).Distinct().ToList();
        var actions = wanted.Select(w => w.Action).Distinct().ToList();

        // Single joined query over the small candidate set (filtered by the resource types/actions
        // asked for), then exact-match in memory — no per-grant round trip (no N+1).
        var candidates = await db.Permissions.AsNoTracking()
            .Where(p => resourceTypes.Contains(p.ResourceType) && actions.Contains(p.Action))
            .ToListAsync(cancellationToken);

        var resolved = new List<Permission>(wanted.Count);
        foreach (var w in wanted)
        {
            var match = candidates.FirstOrDefault(p => p.ResourceType == w.ResourceType && p.Action == w.Action)
                ?? throw new ValidationException("permissions",
                    $"Unknown permission '{w.Action} {w.ResourceType}'. Register it in the permission catalog first.");
            resolved.Add(match);
        }

        return resolved;
    }

    private static void ValidateScope(DefineRoleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("name", "A role name is required.");
        }

        // A community role must carry a community; a platform role must not (keeps the scope and the
        // nullable-CommunityId uniqueness filters coherent — Req 3.6).
        switch (request.Scope)
        {
            case RoleScope.Community when request.CommunityId is null:
                throw new ValidationException("communityId", "A community-scoped role requires a community.");
            case RoleScope.Platform when request.CommunityId is not null:
                throw new ValidationException("communityId", "A platform-scoped role must not carry a community.");
        }
    }

    private static RoleDto ToDto(Role role, IEnumerable<Permission> permissions) =>
        new(role.Id, role.Name, role.Scope, role.CommunityId,
            permissions.Select(p => new PermissionGrant(p.Action, p.ResourceType)).ToList(),
            role.IsDeleted);
}
