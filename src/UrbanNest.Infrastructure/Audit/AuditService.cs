using UrbanNest.Application.Abstractions;
using UrbanNest.Application.Audit;
using UrbanNest.Application.Querying;
using UrbanNest.Contracts.Envelope;
using UrbanNest.Domain.Audit;
using UrbanNest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace UrbanNest.Infrastructure.Audit;

/// <summary>
/// EF-backed Audit_Service (Req 6). The real append-only trail behind <see cref="IAuditService"/>.
///
/// <para><b>Append (Req 6.1, 6.2).</b> <see cref="AppendAsync"/> is a single INSERT. The acting
/// user comes from <see cref="ITenantContext.UserId"/> and the timestamp from <see cref="IClock"/>
/// — never from the caller — so the trail records who/when authoritatively. There is no update or
/// delete method; the <c>AuditSaveChangesInterceptor</c> additionally rejects any Modified/Deleted
/// state on an <c>AuditLog</c> row, so the table is append-only end to end (Req 6.3).</para>
///
/// <para><b>Read (Req 6.5).</b> <see cref="QueryAsync"/> scopes rows to the caller's tenant: a
/// platform super admin sees across all communities; a community admin sees only entries whose
/// <c>CommunityId</c> is in their authorized set. Scoping is applied here (not via an EF global
/// filter) because <c>AuditLog.CommunityId</c> is nullable and the shared tenant filter would hide
/// platform events and assumes a non-null discriminator — see the modeling note on
/// <see cref="AuditLog"/>. The query is <c>AsNoTracking</c> and pushed to SQL through the shared
/// pagination/filter/sort helper against the <c>(CommunityId, Timestamp)</c> index.</para>
///
/// <para><b>Soft-delete survival (Req 6.4).</b> Audit rows are independent records that are never
/// deleted, so a soft-deleted resource's entries remain fully queryable — nothing special is
/// needed beyond keeping them out of any soft-delete filter.</para>
/// </summary>
public sealed class AuditService(AppDbContext db, ITenantContext tenant, IClock clock, IPageSizePolicy pageSizePolicy, IAsyncQueryExecutor executor)
    : IAuditService
{
    // Allow-list of client-facing fields for the read endpoint. Declared once here (steering: no
    // magic strings sprinkled around) and reused for sort/filter/search; the newest-first default
    // uses the indexed (CommunityId, Timestamp) ordering.
    private static readonly ListQueryConfiguration<AuditLog> QueryConfig =
        new ListQueryConfiguration<AuditLog>()
            .WithDefaultSort(a => a.TimestampUtc, SortDirection.Descending)
            .Sortable("timestamp", a => a.TimestampUtc)
            .Sortable("action", a => a.Action)
            .Sortable("resourceType", a => a.ResourceType)
            .Filterable("action", a => a.Action)
            .Filterable("resourceType", a => a.ResourceType)
            .Filterable("resourceId", a => a.ResourceId)
            .Filterable("actingUserId", a => a.ActingUserId)
            .Filterable("communityId", a => a.CommunityId)
            .Searchable(a => a.Action)
            .Searchable(a => a.ResourceType)
            .Searchable(a => a.ResourceId);

    public async Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.ResourceType);

        db.AuditLogs.Add(new AuditLog
        {
            ActingUserId = tenant.UserId,
            Action = entry.Action,
            ResourceType = entry.ResourceType,
            ResourceId = entry.ResourceId,
            CommunityId = entry.CommunityId,
            TimestampUtc = clock.UtcNow,
            Detail = entry.Detail,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedData<AuditLogEntry>> QueryAsync(
        ListQueryParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // Tenant scoping (Req 6.5): super admin sees all; a community admin sees only their
        // authorized communities. Filtered in the database, never in memory.
        IQueryable<AuditLog> source = db.AuditLogs.AsNoTracking();
        if (!tenant.IsPlatformSuperAdmin)
        {
            var authorized = tenant.AuthorizedCommunityIds;
            source = source.Where(a => a.CommunityId != null && authorized.Contains(a.CommunityId.Value));
        }

        return await source.ToPagedResultAsync(
            parameters,
            QueryConfig,
            a => new AuditLogEntry(
                a.Id, a.ActingUserId, a.Action, a.ResourceType, a.ResourceId,
                a.CommunityId, a.TimestampUtc, a.Detail),
            pageSizePolicy,
            executor,
            cancellationToken);
    }
}
