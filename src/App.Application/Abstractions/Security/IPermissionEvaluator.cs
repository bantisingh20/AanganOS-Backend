namespace App.Application.Abstractions.Security;

/// <summary>
/// Seam for the API-level permission gate. Task 2.2 provides the pipeline integration point and
/// the 403-on-missing-permission plumbing; <b>Task 5.3</b> replaces the default implementation
/// with the real evaluator that computes the union of Role→Permission across the user's assigned
/// UserRoles for the requested (Action, resourceType) (Req 4.1–4.3).
///
/// The evaluator receives the already-authenticated <see cref="ITenantContext"/> (so it can scope
/// the permission lookup to the acting user and their authorized communities) and the required
/// permission the endpoint declared. It returns a decision only — it performs no state change and
/// throws nothing; the pipeline turns a <c>false</c> into the FORBIDDEN envelope (Req 4.2).
/// </summary>
public interface IPermissionEvaluator
{
    /// <summary>
    /// Returns true when the acting user is permitted to perform <paramref name="requirement"/>.
    /// Caller guarantees the request is already authenticated (a null/anonymous principal is
    /// rejected earlier with UNAUTHENTICATED, Req 1.5).
    /// </summary>
    ValueTask<bool> IsPermittedAsync(
        ITenantContext tenant,
        PermissionRequirement requirement,
        CancellationToken cancellationToken = default);
}
