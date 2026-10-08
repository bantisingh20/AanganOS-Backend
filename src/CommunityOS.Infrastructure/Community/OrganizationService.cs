using CommunityOS.Application.Audit;
using CommunityOS.Application.Community;
using CommunityOS.Application.Exceptions;
using CommunityOS.Application.Querying;
using CommunityOS.Contracts.Envelope;
using CommunityOS.Domain.Community;
using CommunityOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommunityOS.Infrastructure.Community;

/// <summary>
/// EF-backed <see cref="IOrganizationService"/> (Req 12.1). Create/edit/soft-delete/get/list the
/// platform-scoped organization. Mutations are audited (Req 6.1); delete archives the row (the
/// audit interceptor converts Remove into a soft delete) and it stays retrievable via
/// <c>IgnoreQueryFilters</c> for audit. The list goes through the shared pagination infra and only
/// the allow-listed fields may be sorted/searched (no arbitrary client fields).
/// </summary>
public sealed class OrganizationService(
    AppDbContext db,
    IAuditService audit,
    IPageSizePolicy pageSizePolicy,
    IAsyncQueryExecutor executor) : IOrganizationService
{
    // Allow-list: what a client may sort/search on (steering: no arbitrary client LINQ fields).
    private static readonly ListQueryConfiguration<Organization> QueryConfig =
        new ListQueryConfiguration<Organization>()
            .WithDefaultSort(o => o.Name)
            .Sortable("name", o => o.Name)
            .Searchable(o => o.Name);

    public async Task<OrganizationDto> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireName(request.Name);

        var org = new Organization { Name = request.Name.Trim() };
        db.Organizations.Add(org);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Create", "Organization", org.Id.ToString(), CommunityId: null, Detail: null),
            cancellationToken);

        return ToDto(org);
    }

    public async Task<OrganizationDto> UpdateAsync(Guid organizationId, UpdateOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken)
            ?? throw new NotFoundException("Organization not found.");

        if (request.Name is not null)
        {
            RequireName(request.Name);
            org.Name = request.Name.Trim();
        }

        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Update", "Organization", org.Id.ToString(), CommunityId: null, Detail: null),
            cancellationToken);

        return ToDto(org);
    }

    public async Task DeleteAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken)
            ?? throw new NotFoundException("Organization not found.");

        // Remove is converted to a soft delete by the audit interceptor.
        db.Organizations.Remove(org);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Delete", "Organization", org.Id.ToString(), CommunityId: null, Detail: null),
            cancellationToken);
    }

    public async Task<OrganizationDto?> GetAsync(Guid organizationId, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        IQueryable<Organization> q = db.Organizations.AsNoTracking();
        if (includeArchived)
        {
            q = q.IgnoreQueryFilters(); // retrieve soft-deleted for audit
        }

        var org = await q.FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);
        return org is null ? null : ToDto(org);
    }

    public Task<PagedData<OrganizationDto>> ListAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // The global soft-delete filter excludes archived organizations from ordinary lists.
        return db.Organizations.AsNoTracking().ToPagedResultAsync(
            parameters, QueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
    }

    private static OrganizationDto ToDto(Organization o) => new(o.Id, o.Name, o.IsDeleted);

    private static void RequireName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("name", "An organization name is required.");
        }
    }
}
