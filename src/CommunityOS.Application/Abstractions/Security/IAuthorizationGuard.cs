using CommunityOS.Domain.Identity;

namespace CommunityOS.Application.Abstractions.Security;

/// <summary>
/// Reusable API-layer authorization primitive used by module services/controllers (Task 5.4 and
/// every later phase) on top of the endpoint-level <see cref="IPermissionEvaluator"/> gate. Where
/// the <see cref="IPermissionEvaluator"/> answers the coarse "may this principal perform (Action,
/// resourceType) at all?" declared by <c>RequirePermissionAttribute</c>, this guard answers the
/// two questions that need a <i>resolved</i> target or an <i>additional</i> permission:
///
/// <list type="bullet">
///   <item><description><b>Tenant-isolation on direct id access (Req 5.3).</b> A request that
///   loads a specific record by id must still be rejected when that record's <c>CommunityId</c>
///   is outside the caller's authorized communities. The EF global query filter already hides the
///   row from ordinary queries; this is the API-layer defense-in-depth so a direct id lookup can
///   never leak or mutate a foreign-tenant record.</description></item>
///   <item><description><b>Export ⊆ viewable (Req 4.4, 66.1).</b> An Export operation may only
///   produce records the caller is <i>also</i> permitted to <see cref="PermissionAction.View"/>.
///   Export endpoints call <see cref="EnsureCanExportAsync"/> so an Export grant can never widen
///   the data set beyond the View grant.</description></item>
/// </list>
///
/// All methods either succeed or throw <c>ForbiddenException</c> → FORBIDDEN/403 with no state
/// change; none performs a mutation (steering: security first, default-deny).
/// </summary>
public interface IAuthorizationGuard
{
    /// <summary>
    /// Throws <c>ForbiddenException</c> unless the target <paramref name="communityId"/> is within
    /// the caller's authorized communities (or the caller is a platform super admin). Call this
    /// after resolving a record by id, before returning or mutating it.
    /// </summary>
    void EnsureCommunityAuthorized(Guid communityId);

    /// <summary>
    /// True when the caller holds the given (Action, resourceType) across their authorized
    /// communities. Thin pass-through to the memoized effective-permission set so services can
    /// branch on a permission without a second query. Never throws.
    /// </summary>
    ValueTask<bool> HasPermissionAsync(
        PermissionAction action, string resourceType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Guards an Export operation: throws <c>ForbiddenException</c> unless the caller holds
    /// <see cref="PermissionAction.View"/> on <paramref name="resourceType"/>. The endpoint's
    /// own <see cref="PermissionAction.Export"/> requirement is enforced separately by the gate;
    /// together with the tenant-filtered query this keeps the exported set a subset of the
    /// viewable set (Req 4.4, 66.1).
    /// </summary>
    ValueTask EnsureCanExportAsync(string resourceType, CancellationToken cancellationToken = default);
}
