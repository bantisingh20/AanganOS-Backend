using CommunityOS.Application.Audit;
using CommunityOS.Application.Identity;
using CommunityOS.Domain.Identity;
using CommunityOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommunityOS.Infrastructure.Identity;

/// <summary>
/// EF-backed <see cref="IPermissionCatalog"/> (Req 3.2). Lists the known (ResourceType, Action)
/// catalog — seeded as editable data via <c>PermissionEntityConfiguration.HasData</c> — and lets
/// an admin register additional entries without a code change (steering: resource types are data,
/// not compiled gates).
///
/// <para><b>Idempotent + concurrency-safe register.</b> Register returns an existing entry
/// unchanged; a race where two callers register the same pair relies on the
/// Unique(ResourceType, Action) index — the loser's violation is caught and resolved to the row
/// that won, so the method never surfaces a conflict for an identical register (Req 3.2).</para>
/// </summary>
public sealed class PermissionCatalog(AppDbContext db, IAuditService audit) : IPermissionCatalog
{
    public async Task<IReadOnlyList<PermissionDto>> ListAsync(string? resourceType = null, CancellationToken cancellationToken = default)
    {
        IQueryable<Permission> q = db.Permissions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(resourceType))
        {
            var rt = resourceType.Trim();
            q = q.Where(p => p.ResourceType == rt);
        }

        return await q
            .OrderBy(p => p.ResourceType).ThenBy(p => p.Action)
            .Select(p => new PermissionDto(p.Id, p.ResourceType, p.Action))
            .ToListAsync(cancellationToken);
    }

    public async Task<PermissionDto> RegisterAsync(string resourceType, PermissionAction action, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        var rt = resourceType.Trim();

        var existing = await db.Permissions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ResourceType == rt && p.Action == action, cancellationToken);
        if (existing is not null)
        {
            return new PermissionDto(existing.Id, existing.ResourceType, existing.Action);
        }

        var permission = new Permission { ResourceType = rt, Action = action };
        db.Permissions.Add(permission);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            // A concurrent register of the same (ResourceType, Action) won the race; detach our
            // losing insert and return the row that exists now — register stays idempotent (Req 3.2).
            db.Entry(permission).State = EntityState.Detached;
            var winner = await db.Permissions.AsNoTracking()
                .FirstAsync(p => p.ResourceType == rt && p.Action == action, cancellationToken);
            return new PermissionDto(winner.Id, winner.ResourceType, winner.Action);
        }

        await audit.AppendAsync(
            new AuditEntry("Create", "Permission", permission.Id.ToString(), CommunityId: null,
                Detail: $"{action} {rt}"),
            cancellationToken);

        return new PermissionDto(permission.Id, permission.ResourceType, permission.Action);
    }
}
