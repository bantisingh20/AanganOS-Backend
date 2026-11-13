namespace App.Application.Abstractions;

/// <summary>
/// Per-request tenant/user context resolved by the auth pipeline from the authenticated
/// principal. The data layer reads this to enforce multi-tenant isolation via global query
/// filters (Req 5.1–5.5) and to stamp audit columns with the acting user.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The set of CommunityIds the current user may access, derived from their UserRole scope.
    /// Empty for an unauthenticated or community-less principal.
    /// </summary>
    IReadOnlyCollection<Guid> AuthorizedCommunityIds { get; }

    /// <summary>
    /// When true the tenant predicate short-circuits to allow cross-community access (Req 5.5).
    /// Soft-delete filtering still applies.
    /// </summary>
    bool IsPlatformSuperAdmin { get; }

    /// <summary>
    /// When true, resident self-scoping is applied in addition to tenant filtering: the user
    /// may only see rows for their own Unit/Household (Req 5.4, 66.3).
    /// </summary>
    bool IsResidentSelfScoped { get; }

    /// <summary>The resident's own unit, used by the self-scope predicate. Null when not a scoped resident.</summary>
    Guid? ResidentUnitId { get; }

    /// <summary>The resident's own household, used by the self-scope predicate. Null when not a scoped resident.</summary>
    Guid? HouseholdId { get; }

    /// <summary>Identifier of the acting user, stamped into audit columns. Null for system/anonymous.</summary>
    string? UserId { get; }
}
