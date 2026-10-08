using CommunityOS.Application.Abstractions.Security;
using CommunityOS.Domain.Identity;

namespace CommunityOS.Api.Security;

/// <summary>
/// Declares the permission a protected endpoint requires. Placed as endpoint metadata (controller
/// action attribute, or <c>.WithMetadata(new RequirePermissionAttribute(...))</c> on a minimal-API
/// route). The <see cref="PermissionAuthorizationMiddleware"/> reads it, enforces authentication
/// first (401 UNAUTHENTICATED when the principal is anonymous, Req 1.5) and then asks the
/// <see cref="IPermissionEvaluator"/> whether the acting user may perform the action — a negative
/// decision short-circuits with the FORBIDDEN envelope and no operation runs (Req 4.2).
///
/// This is the pipeline seam for Task 5.3: declaring required permissions on endpoints is valid
/// now; the real evaluation plugs in behind <see cref="IPermissionEvaluator"/> without touching
/// the endpoints.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequirePermissionAttribute(PermissionAction action, string resourceType) : Attribute
{
    public PermissionRequirement Requirement { get; } = new(action, resourceType);
}
