using CommunityOS.Api.Security;
using CommunityOS.Application.Abstractions;
using CommunityOS.Application.Abstractions.Security;
using CommunityOS.Application.Exceptions;

namespace CommunityOS.Api.Middleware;

/// <summary>
/// API-level authorization stage. For an endpoint carrying a <see cref="RequirePermissionAttribute"/>:
/// <list type="number">
///   <item><description>reject an unauthenticated caller with UNAUTHENTICATED / 401 (Req 1.5) — the 401 plumbing belongs here;</description></item>
///   <item><description>ask the <see cref="IPermissionEvaluator"/> whether the acting user may perform the declared (Action, resourceType); a negative decision throws <see cref="ForbiddenException"/> → FORBIDDEN / 403 with no operation run (Req 4.2).</description></item>
/// </list>
/// Endpoints with no permission metadata pass through untouched (public endpoints like /health).
/// Both exceptions bubble to the terminal exception middleware which renders the standard envelope,
/// so correlation id and shape stay consistent.
///
/// Seam for Task 5.3: this middleware owns the integration point and the 401/403 plumbing; the
/// real permission evaluation plugs in behind <see cref="IPermissionEvaluator"/>.
/// </summary>
public sealed class PermissionAuthorizationMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenant,
        IPermissionEvaluator permissionEvaluator)
    {
        var requirement = context.GetEndpoint()?.Metadata.GetMetadata<RequirePermissionAttribute>()?.Requirement;

        if (requirement is null)
        {
            // Unprotected endpoint — nothing to enforce.
            await _next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            throw new UnauthenticatedException();
        }

        var permitted = await permissionEvaluator.IsPermittedAsync(
            tenant, requirement.Value, context.RequestAborted);

        if (!permitted)
        {
            throw new ForbiddenException();
        }

        await _next(context);
    }
}
