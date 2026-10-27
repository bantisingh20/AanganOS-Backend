using App.Application.Abstractions;
using App.Application.Abstractions.Security;

namespace App.Infrastructure.Security;

/// <summary>
/// Placeholder <see cref="IPermissionEvaluator"/> for Task 2.2: it confirms the request is
/// authenticated (a resolved user id) and otherwise grants the action. It exists only so the
/// authorization pipeline is wired end-to-end before the RBAC permission store (Task 5.1) and the
/// real evaluator (Task 5.3) land.
///
/// ponytail: deliberate stub with a known ceiling — it does NOT evaluate Role→Permission, so it
/// is permissive for any authenticated principal. Task 5.3 has landed: <see cref="AuthorizationService"/>
/// is the real evaluator and is registered ahead of this stub (via AddInfrastructure), so this
/// placeholder is no longer used by the API host. It survives only as the TryAdd fail-safe for a
/// host that composes pipeline security without the infrastructure layer. Unauthenticated callers
/// never reach here — the authorization middleware returns UNAUTHENTICATED first (Req 1.5).
/// </summary>
public sealed class AllowAuthenticatedPermissionEvaluator : IPermissionEvaluator
{
    public ValueTask<bool> IsPermittedAsync(
        ITenantContext tenant,
        PermissionRequirement requirement,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(!string.IsNullOrEmpty(tenant.UserId));
}
