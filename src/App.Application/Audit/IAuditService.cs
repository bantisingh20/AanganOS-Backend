using App.Application.Querying;
using App.Contracts.Envelope;

namespace App.Application.Audit;

/// <summary>
/// Audit_Service (Req 6). The single reusable seam module services inject to record an auditable
/// event, and the read path admins use to review the trail.
///
/// <para><b>Append-only (Req 6.3).</b> There is deliberately <i>no</i> update or delete method:
/// the only way to add to the trail is <see cref="AppendAsync"/>, and the data layer independently
/// rejects any attempt to modify or delete a persisted <c>AuditLog</c> row. A later module records
/// a create/update/delete/approve/export mutation (Req 6.1) or a login/security event (Req 6.2)
/// simply by injecting this service and calling <see cref="AppendAsync"/>.</para>
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Append one immutable audit record (Req 6.1, 6.2). The acting user and timestamp are taken
    /// from the request tenant context and the system clock inside the implementation — callers
    /// supply only what happened (action/resource/community/detail), never who or when, so the
    /// trail cannot be spoofed from a DTO. The caller is responsible for redacting any secret value
    /// out of <see cref="AuditEntry.Detail"/> before calling (steering: never log secrets).
    /// </summary>
    Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read the audit trail, tenant-scoped and paginated (Req 6.5). A community admin sees only
    /// entries for their authorized communities (plus null-community platform events are excluded
    /// from a community-scoped view); a platform super admin sees across all communities. The query
    /// is pushed to the database with the shared pagination/filter/sort infra against the
    /// <c>(CommunityId, Timestamp)</c> / <c>(ResourceType, ResourceId)</c> indexes.
    /// </summary>
    Task<PagedData<AuditLogEntry>> QueryAsync(
        ListQueryParameters parameters,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What the caller describes when appending to the trail: the fact of the event, never the actor
/// or the time (those are stamped by the service from the tenant context / clock, Req 6.1).
/// </summary>
/// <param name="Action">The verb performed (e.g. <c>Create</c>, <c>Login</c>). Open string (Req 6.1).</param>
/// <param name="ResourceType">The resource type acted on (e.g. <c>Configuration</c>). Open string.</param>
/// <param name="ResourceId">Identifier of the specific resource/instance acted on; may be null for some events.</param>
/// <param name="CommunityId">Owning community, or null for a platform-level event (Req 6.5).</param>
/// <param name="Detail">Optional small detail/JSON (before→after or security context); must be secret-free.</param>
public readonly record struct AuditEntry(
    string Action,
    string ResourceType,
    string? ResourceId,
    Guid? CommunityId,
    string? Detail);

/// <summary>Read model for an audit record returned by <see cref="IAuditService.QueryAsync"/>.</summary>
/// <param name="Id">The audit record id.</param>
/// <param name="ActingUserId">Who performed the action (Req 6.1).</param>
/// <param name="Action">The action performed.</param>
/// <param name="ResourceType">The resource type acted on.</param>
/// <param name="ResourceId">The specific resource instance.</param>
/// <param name="CommunityId">Owning community, or null for a platform event.</param>
/// <param name="TimestampUtc">When it happened (UTC).</param>
/// <param name="Detail">Event detail.</param>
public sealed record AuditLogEntry(
    Guid Id,
    string? ActingUserId,
    string Action,
    string ResourceType,
    string? ResourceId,
    Guid? CommunityId,
    DateTime TimestampUtc,
    string? Detail);
