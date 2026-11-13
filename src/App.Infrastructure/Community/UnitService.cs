using System.Globalization;
using System.Text;
using App.Application.Abstractions;
using App.Application.Abstractions.Caching;
using App.Application.Audit;
using App.Application.Community;
using App.Application.Config;
using App.Application.Exceptions;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Community;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Community;

/// <summary>
/// EF-backed <see cref="IUnitService"/> (Req 14.1–14.4, 14.6, 22.1, 22.4).
///
/// <para><b>Configurable type/status (Req 14.2, 14.3).</b> <c>UnitType</c>/<c>UnitStatus</c> are
/// validated at runtime against the editable <c>Unit_Type</c>/<c>Unit_Status</c> master-data lists
/// resolved for the unit's own community (so a per-community override applies) — not frozen enums,
/// so an operator can add/retire a type or status without a code change.</para>
///
/// <para><b>Concurrency-safe uniqueness (Req 14.4, 22.1).</b> A duplicate <c>(CommunityId,
/// UnitNumber)</c> is NOT pre-checked with a read-then-write (which would race two concurrent
/// creates past the gap and let both insert). The write relies on the DB unique index
/// (UX_Unit_CommunityId_UnitNumber) as the single source of truth and translates the race loser's
/// violation into a CONFLICT via <see cref="UniqueConstraintGuard"/>. Written to stay correct when
/// two requests create the same number at the same time: exactly one wins, the other gets CONFLICT.</para>
///
/// <para><b>Optimistic concurrency on edit (Req 22.4).</b> The inherited <c>RowVersion</c> token is
/// checked on <c>SaveChanges</c>; a conflicting concurrent update raises
/// <see cref="DbUpdateConcurrencyException"/>, which this service maps to a <see cref="ConflictException"/>
/// (CONFLICT / 409) so a lost update is reported rather than silently overwriting.</para>
///
/// <para><b>Audit + tenant scope.</b> Create/update/delete each append an <see cref="AuditEntry"/>
/// (Req 6.1); reads are tenant-scoped by the global filter (Req 5.2) and use <c>AsNoTracking</c>;
/// delete is a soft delete that stays traceable (Req 14.6).</para>
/// </summary>
public sealed class UnitService(
    AppDbContext db,
    IAuditService audit,
    IConfigService config,
    IPageSizePolicy pageSizePolicy,
    IAsyncQueryExecutor executor,
    IAppCache cache,
    ITenantContext tenant,
    IOptions<CacheOptions> cacheOptions)
    : IUnitService
{
    private const string DuplicateMessage = "A unit with the same number already exists in this community.";

    private readonly TimeSpan _listTtl = TimeSpan.FromSeconds(cacheOptions.Value.UnitsListTtlSeconds);

    // Allow-list for the list endpoint (Req 14.5). unitStatus is both sortable and filterable so the
    // "units in this community with status X" query hits the (CommunityId, UnitStatus) index at scale.
    // No arbitrary client fields reach the query (steering: no hardcoding, injection-safe).
    private static readonly ListQueryConfiguration<Unit> QueryConfig =
        new ListQueryConfiguration<Unit>()
            .WithDefaultSort(u => u.UnitNumber)
            .Sortable("unitNumber", u => u.UnitNumber)
            .Sortable("unitType", u => u.UnitType)
            .Sortable("unitStatus", u => u.UnitStatus)
            .Sortable("floor", u => u.Floor)
            .Filterable("unitType", u => u.UnitType)
            .Filterable("unitStatus", u => u.UnitStatus)
            .Filterable("floor", u => u.Floor)
            .Filterable("hierarchyNodeId", u => u.HierarchyNodeId)
            .Filterable("communityId", u => u.CommunityId)
            .Searchable(u => u.UnitNumber);

    public async Task<UnitDto> CreateAsync(CreateUnitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireUnitNumber(request.UnitNumber);
        await RequireValidUnitTypeAsync(request.UnitType, request.CommunityId, cancellationToken);
        await RequireValidUnitStatusAsync(request.UnitStatus, request.CommunityId, cancellationToken);

        if (request.HierarchyNodeId is Guid nodeId)
        {
            await RequireNodeInSameCommunityAsync(nodeId, request.CommunityId, cancellationToken);
        }

        var unit = new Unit
        {
            CommunityId = request.CommunityId,
            HierarchyNodeId = request.HierarchyNodeId,
            UnitNumber = request.UnitNumber.Trim(),
            UnitType = request.UnitType.Trim(),
            Area = request.Area,
            Floor = request.Floor?.Trim(),
            Bedrooms = request.Bedrooms,
            UnitStatus = request.UnitStatus.Trim(),
            CustomAttributes = ToStore(request.CustomAttributes),
        };

        db.Units.Add(unit);

        // Concurrency-safe: let the DB unique index decide the winner (no read-then-write pre-check).
        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), DuplicateMessage);

        await audit.AppendAsync(
            new AuditEntry("Create", "Unit", unit.Id.ToString(), unit.CommunityId, Detail: null),
            cancellationToken);

        await InvalidateCommunityAsync(unit.CommunityId, cancellationToken);

        return ToDto(unit);
    }

    public async Task<UnitDto> UpdateAsync(Guid unitId, UpdateUnitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == unitId, cancellationToken)
            ?? throw new NotFoundException("Unit not found.");

        if (request.UnitNumber is not null)
        {
            RequireUnitNumber(request.UnitNumber);
            unit.UnitNumber = request.UnitNumber.Trim();
        }

        if (request.UnitType is not null)
        {
            await RequireValidUnitTypeAsync(request.UnitType, unit.CommunityId, cancellationToken);
            unit.UnitType = request.UnitType.Trim();
        }

        if (request.UnitStatus is not null)
        {
            await RequireValidUnitStatusAsync(request.UnitStatus, unit.CommunityId, cancellationToken);
            unit.UnitStatus = request.UnitStatus.Trim();
        }

        if (request.UpdateHierarchyNode)
        {
            if (request.HierarchyNodeId is Guid newNodeId)
            {
                await RequireNodeInSameCommunityAsync(newNodeId, unit.CommunityId, cancellationToken);
            }

            unit.HierarchyNodeId = request.HierarchyNodeId; // null => unlink
        }

        if (request.UpdateArea)
        {
            unit.Area = request.Area;
        }

        if (request.UpdateFloor)
        {
            unit.Floor = request.Floor?.Trim();
        }

        if (request.UpdateBedrooms)
        {
            unit.Bedrooms = request.Bedrooms;
        }

        if (request.CustomAttributes is not null)
        {
            unit.CustomAttributes = ToStore(request.CustomAttributes);
        }

        // A renamed unit can collide with an existing number → CONFLICT via the DB unique index.
        // The optimistic-concurrency token mismatch (Req 22.4) — another request updated this row
        // first — is mapped to CONFLICT centrally in AppDbContext.SaveChangesAsync, so there is no
        // per-service DbUpdateConcurrencyException catch here.
        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), DuplicateMessage);

        await audit.AppendAsync(
            new AuditEntry("Update", "Unit", unit.Id.ToString(), unit.CommunityId, Detail: null),
            cancellationToken);

        await InvalidateCommunityAsync(unit.CommunityId, cancellationToken);

        return ToDto(unit);
    }

    public async Task DeleteAsync(Guid unitId, CancellationToken cancellationToken = default)
    {
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == unitId, cancellationToken)
            ?? throw new NotFoundException("Unit not found.");

        // Remove is converted to a soft delete by the audit interceptor; the archived unit stays
        // traceable (Req 14.6).
        db.Units.Remove(unit);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Delete", "Unit", unit.Id.ToString(), unit.CommunityId, Detail: null),
            cancellationToken);

        await InvalidateCommunityAsync(unit.CommunityId, cancellationToken);
    }

    public async Task<UnitDto?> GetAsync(Guid unitId, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        IQueryable<Unit> q = db.Units.AsNoTracking();
        if (includeArchived)
        {
            q = q.IgnoreQueryFilters(); // retrieve soft-deleted for audit (Req 14.6)
        }

        var unit = await q.FirstOrDefaultAsync(u => u.Id == unitId, cancellationToken);
        return unit is null ? null : ToDto(unit);
    }

    public async Task<PagedData<UnitDto>> ListAsync(ListQueryParameters parameters, bool availableForOwner = false, Guid? organizationId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var genToken = await ScopeGenerationTokenAsync(cancellationToken);
        var cacheKey = BuildListCacheKey(genToken, parameters, availableForOwner, organizationId);

        var result = await cache.GetOrCreateAsync<PagedData<UnitDto>>(
            cacheKey,
            _listTtl,
            async ct => await QueryListAsync(parameters, availableForOwner, organizationId, ct),
            cancellationToken);

        return result ?? await QueryListAsync(parameters, availableForOwner, organizationId, cancellationToken);
    }

    private async Task<PagedData<UnitDto>> QueryListAsync(ListQueryParameters parameters, bool availableForOwner, Guid? organizationId, CancellationToken cancellationToken)
    {
        var source = db.Units.AsNoTracking();

        if (availableForOwner)
        {
            source = source.Where(u => !db.HouseholdMembers
                .Where(h => h.UnitId == u.Id && h.EndDate == null)
                .Join(db.Residents, h => h.ResidentId, r => r.Id, (h, r) => r.ResidentType)
                .Any(t => t == "owner"));
        }

        if (organizationId is { } orgId)
        {
            source = source.Where(u => db.Communities.Any(c => c.Id == u.CommunityId && c.OrganizationId == orgId));
        }

        var paged = await source.ToPagedResultAsync(
            parameters, QueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);

        var names = await ResolveCommunityOrgNamesAsync(
            db, paged.Items.Select(u => u.CommunityId), cancellationToken);

        return new PagedData<UnitDto>
        {
            Items = paged.Items
                .Select(u => names.TryGetValue(u.CommunityId, out var n)
                    ? u with { CommunityName = n.Community, OrganizationName = n.Organization }
                    : u)
                .ToList(),
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalCount = paged.TotalCount,
        };
    }

    internal static async Task<Dictionary<Guid, (string Community, string Organization)>> ResolveCommunityOrgNamesAsync(
        AppDbContext db, IEnumerable<Guid> communityIds, CancellationToken cancellationToken)
    {
        var ids = communityIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, (string, string)>();
        }

        var rows = await db.Communities.AsNoTracking().IgnoreQueryFilters()
            .Where(c => ids.Contains(c.Id))
            .Join(
                db.Organizations.AsNoTracking().IgnoreQueryFilters(),
                c => c.OrganizationId,
                o => o.Id,
                (c, o) => new { c.Id, Community = c.Name, Organization = o.Name })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Id, r => (r.Community, r.Organization));
    }

    // --- units list cache: per-tenant-scope + per-param key, per-community generation invalidation ---

    private string TenantScopeToken()
    {
        if (tenant.IsPlatformSuperAdmin)
        {
            return "super";
        }
        var ids = tenant.AuthorizedCommunityIds;
        if (ids.Count == 0)
        {
            return "none";
        }
        return string.Join(',', ids.OrderBy(id => id));
    }

    // The read key embeds the generation of every community in the caller's scope (generation is
    // keyed PER COMMUNITY, so a write by anyone invalidates every reader of that community regardless
    // of the writer's scope). Super-admin spans all communities and can't enumerate them, so its
    // reads are keyed under a single "super" generation (busted by any write) plus the bounded TTL.
    private async Task<string> ScopeGenerationTokenAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsPlatformSuperAdmin)
        {
            var superGen = await cache.GetGenerationAsync(UnitsCacheKeys.SuperGeneration, cancellationToken);
            return $"super:{superGen}";
        }

        var ids = tenant.AuthorizedCommunityIds.OrderBy(id => id).ToList();
        if (ids.Count == 0)
        {
            return "none";
        }

        var parts = new List<string>(ids.Count);
        foreach (var id in ids)
        {
            var g = await cache.GetGenerationAsync(UnitsCacheKeys.CommunityGeneration(id), cancellationToken);
            parts.Add($"{id}={g}");
        }
        return string.Join(',', parts);
    }

    private async Task InvalidateCommunityAsync(Guid communityId, CancellationToken cancellationToken)
    {
        await cache.BumpGenerationAsync(UnitsCacheKeys.CommunityGeneration(communityId), cancellationToken);
        // A super-admin may have cached a cross-tenant page that includes this community; bump the
        // shared super generation too so those reads re-resolve.
        await cache.BumpGenerationAsync(UnitsCacheKeys.SuperGeneration, cancellationToken);
    }

    private string BuildListCacheKey(string genToken, ListQueryParameters p, bool availableForOwner, Guid? organizationId)
    {
        var sb = new StringBuilder("units:list:");
        sb.Append(TenantScopeToken()).Append(":g[").Append(genToken).Append("]:");
        sb.Append("pg=").Append(p.Page?.ToString(CultureInfo.InvariantCulture) ?? "_");
        sb.Append(";ps=").Append(p.PageSize?.ToString(CultureInfo.InvariantCulture) ?? "_");
        sb.Append(";q=").Append(p.Search?.Trim().ToLowerInvariant() ?? "_");
        sb.Append(";afo=").Append(availableForOwner ? '1' : '0');
        sb.Append(";org=").Append(organizationId?.ToString() ?? "_");
        foreach (var s in p.Sort)
        {
            sb.Append(";s=").Append(s.Field).Append(s.Direction == SortDirection.Descending ? "-" : "+");
        }
        foreach (var f in p.Filters.OrderBy(f => f.Field, StringComparer.Ordinal))
        {
            sb.Append(";f=").Append(f.Field).Append((int)f.Operator).Append('=').Append(f.Value);
        }
        return sb.ToString();
    }

    private static UnitDto ToDto(Unit u) =>
        new(u.Id, u.CommunityId, u.HierarchyNodeId, u.UnitNumber, u.UnitType, u.Area, u.Floor,
            u.Bedrooms, u.UnitStatus,
            new Dictionary<string, string>(u.CustomAttributes), u.IsDeleted);

    private static Dictionary<string, string> ToStore(IReadOnlyDictionary<string, string>? attributes) =>
        attributes is null ? [] : new Dictionary<string, string>(attributes);

    private static void RequireUnitNumber(string? unitNumber)
    {
        if (string.IsNullOrWhiteSpace(unitNumber))
        {
            throw new ValidationException("unitNumber", "A unit number is required.");
        }
    }

    private async Task RequireValidUnitTypeAsync(string? unitType, Guid communityId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(unitType))
        {
            throw new ValidationException("unitType", "A unit type is required.");
        }

        // Validate against the configurable Unit_Type list resolved for this community (Req 14.2) —
        // not a frozen enum, so types stay editable per community.
        var allowed = await config.GetMasterDataAsync(MasterDataKeys.UnitType, communityId, cancellationToken: cancellationToken);
        if (!allowed.Any(e => string.Equals(e.Code, unitType.Trim(), StringComparison.Ordinal)))
        {
            throw new ValidationException("unitType", $"'{unitType}' is not a valid unit type.");
        }
    }

    private async Task RequireValidUnitStatusAsync(string? unitStatus, Guid communityId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(unitStatus))
        {
            throw new ValidationException("unitStatus", "A unit status is required.");
        }

        // Validate against the configurable Unit_Status list resolved for this community (Req 14.3).
        var allowed = await config.GetMasterDataAsync(MasterDataKeys.UnitStatus, communityId, cancellationToken: cancellationToken);
        if (!allowed.Any(e => string.Equals(e.Code, unitStatus.Trim(), StringComparison.Ordinal)))
        {
            throw new ValidationException("unitStatus", $"'{unitStatus}' is not a valid unit status.");
        }
    }

    private async Task RequireNodeInSameCommunityAsync(Guid nodeId, Guid communityId, CancellationToken cancellationToken)
    {
        // Tenant-scoped read: the global filter already hides foreign-tenant nodes. Load the node's
        // CommunityId and reject a cross-community link with a validation error (same convention as
        // HierarchyService's same-community parent check).
        var nodeCommunityId = await db.HierarchyNodes.AsNoTracking()
            .Where(n => n.Id == nodeId)
            .Select(n => (Guid?)n.CommunityId)
            .FirstOrDefaultAsync(cancellationToken);

        if (nodeCommunityId is null || nodeCommunityId.Value != communityId)
        {
            throw new ValidationException("hierarchyNodeId", "The hierarchy node must belong to the same community.");
        }
    }
}
