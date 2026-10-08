using CommunityOS.Application.Abstractions;
using CommunityOS.Application.Abstractions.Security;
using CommunityOS.Application.Exceptions;
using CommunityOS.Application.Identity;
using CommunityOS.Domain.Identity;

namespace CommunityOS.Infrastructure.Security;

/// <summary>
/// The real Authorization_Service (Task 5.3) — the API-level permission gate that replaces the
/// permissive <c>AllowAuthenticatedPermissionEvaluator</c> placeholder. It implements both the
/// endpoint-gate seam (<see cref="IPermissionEvaluator"/>, consumed by the pipeline middleware)
/// and the reusable resource/export guard (<see cref="IAuthorizationGuard"/>, consumed by module
/// services) so a single request-scoped memoized effective-permission set backs every check.
///
/// <para><b>Least-privilege, default-deny (Req 4.1, 4.3).</b> A permission is granted only when it
/// is explicitly conferred by one of the user's assigned roles — the union of Role→Permission from
/// <see cref="IEffectivePermissionQuery"/>, scoped to the request's authorized communities. No
/// match ⇒ not permitted. The evaluator returns a decision and never throws, never mutates (Req
/// 4.2): the middleware turns a <c>false</c> into the FORBIDDEN/403 envelope.</para>
///
/// <para><b>Super admin without a name check (Req 5.3/5.5).</b> The cross-community short-circuit
/// is driven by <see cref="ITenantContext.IsPlatformSuperAdmin"/> — a signed claim derived from
/// the role data the user holds — not by matching a hardcoded role name (steering: no behaviour-
/// gating role-name strings).</para>
///
/// <para><b>Request-scoped memoization (steering: scalable reads).</b> Registered scoped, this
/// instance lives for one request; the effective-permission set is loaded once (single joined
/// <c>AsNoTracking</c> query) and reused by every subsequent check in that request. Because the
/// instance is per request it can never leak a set across users/tenants — the next request gets a
/// fresh instance and a fresh load keyed to that request's user and authorized communities.</para>
/// </summary>
public sealed class AuthorizationService(
    ITenantContext tenant,
    IEffectivePermissionQuery effectivePermissions)
    : IPermissionEvaluator, IAuthorizationGuard
{
    // Memoized once per request. Null until the first check loads it. Keyed implicitly to this
    // request's user + authorized communities because the instance is request-scoped.
    private IReadOnlyCollection<EffectivePermission>? _cachedPermissions;

    public async ValueTask<bool> IsPermittedAsync(
        ITenantContext tenantContext,
        PermissionRequirement requirement,
        CancellationToken cancellationToken = default)
        => await HasPermissionAsync(requirement.Action, requirement.ResourceType, cancellationToken);

    public async ValueTask<bool> HasPermissionAsync(
        PermissionAction action,
        string resourceType,
        CancellationToken cancellationToken = default)
    {
        // Platform super admin is permitted across communities as conferred by that role (Req 5.3,
        // 5.5) — modeled via the signed flag, not a role-name string.
        if (tenant.IsPlatformSuperAdmin)
        {
            return true;
        }

        // Default-deny: an unresolved/anonymous principal holds nothing. (The middleware already
        // rejects anonymous callers with UNAUTHENTICATED before reaching here; this keeps the
        // evaluator safe if called in any other context.)
        if (!Guid.TryParse(tenant.UserId, out var userId))
        {
            return false;
        }

        var permissions = await LoadPermissionsAsync(userId, cancellationToken);

        // Least-privilege: granted iff the exact (Action, resourceType) is in the conferred union.
        return permissions.Any(p => p.Action == action && ResourceMatches(p.ResourceType, resourceType));
    }

    public void EnsureCommunityAuthorized(Guid communityId)
    {
        // Super admin crosses communities (Req 5.5). Otherwise the target must be in the caller's
        // authorized set, else FORBIDDEN with no state change (Req 5.3, defense-in-depth over the
        // EF global filter).
        if (tenant.IsPlatformSuperAdmin || tenant.AuthorizedCommunityIds.Contains(communityId))
        {
            return;
        }

        throw new ForbiddenException();
    }

    public async ValueTask EnsureCanExportAsync(
        string resourceType, CancellationToken cancellationToken = default)
    {
        // Export ⊆ viewable (Req 4.4, 66.1): the export may only yield records the caller can View.
        if (!await HasPermissionAsync(PermissionAction.View, resourceType, cancellationToken))
        {
            throw new ForbiddenException();
        }
    }

    private async ValueTask<IReadOnlyCollection<EffectivePermission>> LoadPermissionsAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        if (_cachedPermissions is not null)
        {
            return _cachedPermissions;
        }

        _cachedPermissions = await effectivePermissions.GetEffectivePermissionsAsync(
            userId, tenant.AuthorizedCommunityIds, cancellationToken);

        return _cachedPermissions;
    }

    private static bool ResourceMatches(string granted, string required)
        => string.Equals(granted, required, StringComparison.OrdinalIgnoreCase);
}
