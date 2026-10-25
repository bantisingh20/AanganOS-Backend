using UrbanNest.Application.Audit;
using UrbanNest.Application.Community;
using UrbanNest.Application.Config;
using UrbanNest.Application.Exceptions;
using UrbanNest.Domain.Community;
using UrbanNest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace UrbanNest.Infrastructure.Community;

/// <summary>
/// EF-backed <see cref="IHierarchyService"/> (Req 13.1–13.5).
///
/// <para><b>Optional levels (Req 13.2).</b> A node's parent may be any higher node in the same
/// community or null (a root node); there is no fixed level chain. The level code is validated
/// against the configurable <c>Hierarchy_Level</c> master-data list resolved at runtime, not a
/// frozen enum.</para>
///
/// <para><b>Same-community parent (Req 13.3).</b> When a parent is supplied the service loads it
/// (tenant-scoped) and rejects it with a <see cref="ValidationException"/> if it is not in the same
/// community. Because the global tenant filter already hides foreign-tenant rows, a parent in
/// another community resolves to "not found" and is reported as a validation error on
/// <c>parentId</c>; an explicit community-mismatch check also guards the (super-admin) cross-tenant
/// read path.</para>
///
/// <para><b>Tenant-scoped reads + no N+1 (Req 13.4).</b> Retrieving a community's hierarchy is a
/// single indexed <c>AsNoTracking</c> query over <c>(CommunityId, ParentId)</c>; the tree shape is
/// carried by each node's <c>ParentId</c>, so no per-node follow-up queries are issued. Delete is a
/// soft delete that stays traceable (Req 13.5).</para>
/// </summary>
public sealed class HierarchyService(AppDbContext db, IAuditService audit, IConfigService config)
    : IHierarchyService
{
    public async Task<HierarchyNodeDto> CreateAsync(CreateHierarchyNodeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireName(request.Name);
        await RequireValidLevelAsync(request.Level, cancellationToken);

        if (request.ParentId is Guid parentId)
        {
            await RequireParentInSameCommunityAsync(parentId, request.CommunityId, cancellationToken);
        }

        var node = new HierarchyNode
        {
            CommunityId = request.CommunityId,
            ParentId = request.ParentId,
            Level = request.Level.Trim(),
            Name = request.Name.Trim(),
        };

        db.HierarchyNodes.Add(node);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Create", "HierarchyNode", node.Id.ToString(), node.CommunityId, Detail: null),
            cancellationToken);

        return ToDto(node);
    }

    public async Task<HierarchyNodeDto> UpdateAsync(Guid nodeId, UpdateHierarchyNodeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var node = await db.HierarchyNodes.FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken)
            ?? throw new NotFoundException("Hierarchy node not found.");

        if (request.Name is not null)
        {
            RequireName(request.Name);
            node.Name = request.Name.Trim();
        }

        if (request.Level is not null)
        {
            await RequireValidLevelAsync(request.Level, cancellationToken);
            node.Level = request.Level.Trim();
        }

        if (request.UpdateParent)
        {
            if (request.ParentId is Guid newParentId)
            {
                if (newParentId == node.Id)
                {
                    throw new ValidationException("parentId", "A hierarchy node cannot be its own parent.");
                }

                await RequireParentInSameCommunityAsync(newParentId, node.CommunityId, cancellationToken);
            }

            node.ParentId = request.ParentId; // null => make this a root node
        }

        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Update", "HierarchyNode", node.Id.ToString(), node.CommunityId, Detail: null),
            cancellationToken);

        return ToDto(node);
    }

    public async Task DeleteAsync(Guid nodeId, CancellationToken cancellationToken = default)
    {
        var node = await db.HierarchyNodes.FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken)
            ?? throw new NotFoundException("Hierarchy node not found.");

        // Remove is converted to a soft delete by the audit interceptor; the archived node stays
        // traceable (Req 13.5).
        db.HierarchyNodes.Remove(node);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Delete", "HierarchyNode", node.Id.ToString(), node.CommunityId, Detail: null),
            cancellationToken);
    }

    public async Task<HierarchyNodeDto?> GetAsync(Guid nodeId, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        IQueryable<HierarchyNode> q = db.HierarchyNodes.AsNoTracking();
        if (includeArchived)
        {
            q = q.IgnoreQueryFilters(); // retrieve soft-deleted for audit (Req 13.5)
        }

        var node = await q.FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken);
        return node is null ? null : ToDto(node);
    }

    public async Task<IReadOnlyList<HierarchyNodeDto>> GetCommunityHierarchyAsync(Guid communityId, CancellationToken cancellationToken = default)
    {
        // Single tenant-scoped query over the (CommunityId, ParentId) index — the global filter
        // enforces tenant scope (Req 13.4) and excludes archived nodes; no per-node follow-up (no N+1).
        var nodes = await db.HierarchyNodes.AsNoTracking()
            .Where(n => n.CommunityId == communityId)
            .OrderBy(n => n.ParentId)
            .ThenBy(n => n.Name)
            .Select(n => new HierarchyNodeDto(n.Id, n.CommunityId, n.ParentId, n.Level, n.Name, n.IsDeleted))
            .ToListAsync(cancellationToken);

        return nodes;
    }

    private static HierarchyNodeDto ToDto(HierarchyNode n) =>
        new(n.Id, n.CommunityId, n.ParentId, n.Level, n.Name, n.IsDeleted);

    private static void RequireName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("name", "A hierarchy node name is required.");
        }
    }

    private async Task RequireValidLevelAsync(string? level, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            throw new ValidationException("level", "A hierarchy level is required.");
        }

        // Validate against the configurable Hierarchy_Level list (Req 13.2) — optional, editable
        // levels, not a frozen enum.
        var allowed = await config.GetMasterDataAsync(MasterDataKeys.HierarchyLevel, cancellationToken: cancellationToken);
        if (!allowed.Any(e => string.Equals(e.Code, level.Trim(), StringComparison.Ordinal)))
        {
            throw new ValidationException("level", $"'{level}' is not a valid hierarchy level.");
        }
    }

    private async Task RequireParentInSameCommunityAsync(Guid parentId, Guid communityId, CancellationToken cancellationToken)
    {
        // Tenant-scoped read: the global filter already hides foreign-tenant parents. Load the
        // parent's CommunityId and reject a cross-community parent with a validation error (Req 13.3).
        var parentCommunityId = await db.HierarchyNodes.AsNoTracking()
            .Where(n => n.Id == parentId)
            .Select(n => (Guid?)n.CommunityId)
            .FirstOrDefaultAsync(cancellationToken);

        if (parentCommunityId is null || parentCommunityId.Value != communityId)
        {
            throw new ValidationException("parentId", "The parent node must belong to the same community.");
        }
    }
}
