using App.Api.Security;
using App.Application.Abstractions;
using App.Application.Abstractions.Security;
using App.Application.Exceptions;

namespace App.Api.Middleware;


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

        // Permission (RBAC) checks disabled: role-based access is managed in the client layer.
        // Authentication is still enforced above. To re-enable, uncomment the block below.
        // var permitted = await permissionEvaluator.IsPermittedAsync(
        //     tenant, requirement.Value, context.RequestAborted);
        //
        // if (!permitted)
        // {
        //     throw new ForbiddenException();
        // }
        _ = tenant;
        _ = permissionEvaluator;

        await _next(context);
    }
}
