using App.Api.Security;
using App.Application.Abstractions;
using App.Application.Abstractions.Security;
using App.Application.Exceptions;

namespace App.Api.Middleware;


public sealed class PermissionAuthorizationMiddleware(
    RequestDelegate next,
    PermissionEnforcementOptions options,
    ILogger<PermissionAuthorizationMiddleware> logger)
{
    private readonly RequestDelegate _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        CurrentUser currentUser,
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

        // RBAC: the caller's role must hold the (Action, ResourceType) the endpoint declares.
        // Super admin bypasses inside the evaluator. Switch: Security:EnforcePermissions
        // (false = audit only: log the would-be denial and let the request through).
        var permitted = await permissionEvaluator.IsPermittedAsync(
            currentUser, requirement.Value, context.RequestAborted);

        if (!permitted)
        {
            if (options.EnforcePermissions)
            {
                logger.LogInformation(
                    "[RBAC] Denied {Action}:{Resource} for user {UserId} (roles {Roles}) on {Method} {Path}",
                    requirement.Value.Action, requirement.Value.ResourceType, currentUser.UserId,
                    string.Join(",", currentUser.Roles), context.Request.Method, context.Request.Path);
                throw new ForbiddenException();
            }

            logger.LogWarning(
                "[RBAC audit] would deny {Action}:{Resource} for user {UserId} (roles {Roles}) on {Method} {Path}",
                requirement.Value.Action, requirement.Value.ResourceType, currentUser.UserId,
                string.Join(",", currentUser.Roles), context.Request.Method, context.Request.Path);
        }

        await _next(context);
    }
}
