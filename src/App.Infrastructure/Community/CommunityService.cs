using App.Application.Abstractions.Caching;
using App.Application.Audit;
using App.Application.Community;
using App.Application.Config;
using App.Application.Exceptions;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Community;

using CommunityEntity = Domain.Community.Community;

/// <summary>
/// EF-backed <see cref="ICommunityService"/> (Req 12.1, 12.2, 12.5).
///
/// <para><b>CommunityId assignment (Req 12.1).</b> Create sets the new row's
/// <c>CommunityId</c> equal to its own generated <c>Id</c>, so that id becomes the tenant
/// discriminator stamped on all child data and the standard tenant global filter scopes the
/// Community row itself to the owning tenant on later reads (no bespoke predicate). The create
/// write (<c>Add</c>+<c>SaveChanges</c>) is not subject to the read-side query filter, so a
/// super-admin creating a brand-new community is never blocked by it.</para>
///
/// <para><b>Configurable type (Req 12.2).</b> The submitted community-type is validated against the
/// active <c>Community_Type</c> master-data list resolved at runtime through Config_Service — not a
/// compiled enum — so types stay editable (steering: no hardcoding).</para>
///
/// <para><b>Audit + soft-delete.</b> Each mutation appends an <see cref="AuditEntry"/> (Req 6.1);
/// delete archives the row via the audit interceptor's Remove→soft-delete conversion (Req 12.5),
/// and it stays retrievable via <c>IgnoreQueryFilters</c> for audit.</para>
/// </summary>
public sealed class CommunityService(
    AppDbContext db,
    IAuditService audit,
    IConfigService config,
    IPageSizePolicy pageSizePolicy,
    IAsyncQueryExecutor executor,
    IAppCache cache)
    : ICommunityService
{
    // Allow-list: client-facing fields a caller may sort/filter/search on (Req 12.4). No arbitrary
    // client fields reach the query (steering: no hardcoding, injection-safe).
    private static readonly ListQueryConfiguration<CommunityEntity> QueryConfig =
        new ListQueryConfiguration<CommunityEntity>()
            .WithDefaultSort(c => c.Name)
            .Sortable("name", c => c.Name)
            .Sortable("communityType", c => c.CommunityType)
            .Filterable("communityType", c => c.CommunityType)
            .Filterable("organizationId", c => c.OrganizationId)
            .Sortable("code", c => c.Code)
            .Searchable(c => c.Name)
            .Searchable(c => c.Code);

    public async Task<CommunityDto> CreateAsync(CreateCommunityRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireName(request.Name);
        if (!string.IsNullOrWhiteSpace(request.CommunityType))
        {
            await RequireValidCommunityTypeAsync(request.CommunityType, cancellationToken);
        }
        await RequireOrganizationExistsAsync(request.OrganizationId, cancellationToken);
        var code = NormalizeCode(request.Code);
        await RequireCodeAvailableAsync(request.OrganizationId, code, exceptCommunityId: null, cancellationToken);

        var community = new CommunityEntity
        {
            OrganizationId = request.OrganizationId,
            Name = request.Name.Trim(),
            Code = code,
            CommunityType = string.IsNullOrWhiteSpace(request.CommunityType) ? "apartment_society" : request.CommunityType.Trim(),
        };
        // The tenant discriminator for a community IS its own id (Req 12.1): all child data will
        // carry this value, and the global tenant filter scopes this row to the owning tenant.
        community.CommunityId = community.Id;

        db.Communities.Add(community);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Create", "Community", community.Id.ToString(), community.Id, Detail: null),
            cancellationToken);

        return ToDto(community);
    }

    public async Task<CommunityDto> UpdateAsync(Guid communityId, UpdateCommunityRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var community = await db.Communities.FirstOrDefaultAsync(c => c.Id == communityId, cancellationToken)
            ?? throw new NotFoundException("Community not found.");

        if (request.Name is not null)
        {
            RequireName(request.Name);
            community.Name = request.Name.Trim();
        }

        if (request.CommunityType is not null)
        {
            if (!string.IsNullOrWhiteSpace(request.CommunityType))
            {
                await RequireValidCommunityTypeAsync(request.CommunityType, cancellationToken);
                community.CommunityType = request.CommunityType.Trim();
            }
            else
            {
                community.CommunityType = null;
            }
        }

        var codeChanged = false;
        if (request.Code is not null)
        {
            var code = NormalizeCode(request.Code);
            await RequireCodeAvailableAsync(community.OrganizationId, code, community.Id, cancellationToken);
            codeChanged = community.Code != code;
            community.Code = code;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (codeChanged)
        {
            // Unit lists carry the community code (A-101); drop cached pages so the new code shows now.
            await cache.BumpGenerationAsync(UnitsCacheKeys.CommunityGeneration(community.Id), cancellationToken);
            await cache.BumpGenerationAsync(UnitsCacheKeys.SuperGeneration, cancellationToken);
        }

        await audit.AppendAsync(
            new AuditEntry("Update", "Community", community.Id.ToString(), community.Id, Detail: null),
            cancellationToken);

        return ToDto(community);
    }

    public async Task DeleteAsync(Guid communityId, CancellationToken cancellationToken = default)
    {
        var community = await db.Communities.FirstOrDefaultAsync(c => c.Id == communityId, cancellationToken)
            ?? throw new NotFoundException("Community not found.");

        // Remove is converted to a soft delete by the audit interceptor; the archived community
        // stays traceable (Req 12.5).
        db.Communities.Remove(community);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Delete", "Community", community.Id.ToString(), community.Id, Detail: null),
            cancellationToken);
    }

    public async Task<CommunityDto?> GetAsync(Guid communityId, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        IQueryable<CommunityEntity> q = db.Communities.AsNoTracking();
        if (includeArchived)
        {
            q = q.IgnoreQueryFilters(); // retrieve soft-deleted for audit (Req 12.5)
        }

        var community = await q.FirstOrDefaultAsync(c => c.Id == communityId, cancellationToken);
        return community is null ? null : ToDto(community);
    }

    public async Task<PagedData<CommunityDto>> ListAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // Tenant-scoped by the global query filter (Req 12.4): a community admin sees only their own
        // community row(s); a super-admin sees across. The soft-delete filter excludes archived rows.
        var paged = await db.Communities.AsNoTracking().ToPagedResultAsync(
            parameters, QueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);

        var orgIds = paged.Items.Select(c => c.OrganizationId).Distinct().ToList();
        var orgNames = orgIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Organizations.AsNoTracking().IgnoreQueryFilters()
                .Where(o => orgIds.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, o => o.Name, cancellationToken);

        return new PagedData<CommunityDto>
        {
            Items = paged.Items
                .Select(c => orgNames.TryGetValue(c.OrganizationId, out var n) ? c with { OrganizationName = n } : c)
                .ToList(),
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalCount = paged.TotalCount,
        };
    }

    private static CommunityDto ToDto(CommunityEntity c) =>
        new(c.Id, c.OrganizationId, c.Name, c.CommunityType, c.IsDeleted, Code: c.Code);

    /// <summary>Trims the wing code; blank means "no code".</summary>
    private static string? NormalizeCode(string? code)
    {
        var trimmed = code?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }
        if (trimmed.Length > 20)
        {
            throw new ValidationException("code", "The code can be at most 20 characters.");
        }
        return trimmed;
    }

    /// <summary>Two wings of the same organization may not share a code (A-101 must be unambiguous).</summary>
    private async Task RequireCodeAvailableAsync(Guid organizationId, string? code, Guid? exceptCommunityId, CancellationToken cancellationToken)
    {
        if (code is null)
        {
            return;
        }

        var taken = await db.Communities.AsNoTracking().IgnoreQueryFilters()
            .AnyAsync(c => c.OrganizationId == organizationId
                && !c.IsDeleted
                && c.Code == code
                && (exceptCommunityId == null || c.Id != exceptCommunityId), cancellationToken);
        if (taken)
        {
            throw new ValidationException("code", $"Another community in this organization already uses the code '{code}'.");
        }
    }

    private static void RequireName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("name", "A community name is required.");
        }
    }

    private async Task RequireValidCommunityTypeAsync(string? communityType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(communityType))
        {
            return;
        }

        // Validate against the configurable Community_Type list resolved at runtime (Req 12.2) —
        // not against a frozen enum, so an operator can add/retire types without a code change.
        var allowed = await config.GetMasterDataAsync(MasterDataKeys.CommunityType, cancellationToken: cancellationToken);
        if (!allowed.Any(e => string.Equals(e.Code, communityType.Trim(), StringComparison.Ordinal)))
        {
            throw new ValidationException("communityType", $"'{communityType}' is not a valid community type.");
        }
    }

    private async Task RequireOrganizationExistsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var exists = await db.Organizations.AsNoTracking().AnyAsync(o => o.Id == organizationId, cancellationToken);
        if (!exists)
        {
            throw new ValidationException("organizationId", "The specified organization does not exist.");
        }
    }
}
