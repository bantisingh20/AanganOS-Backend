namespace UrbanNest.Application.Abstractions.Security;

/// <summary>
/// The claim-type contract between token issuance (Auth_Service, Task 5.2) and the request
/// pipeline (Task 2.2). Defined here, now, so the authentication layer can map a validated
/// bearer token's claims into an <see cref="ITenantContext"/> and 5.2 knows exactly which
/// claims to issue. Standard JWT registered claims (<c>sub</c>, roles) are reused where they
/// fit; the tenant/self-scope claims are UrbanNest-specific and namespaced to avoid clashes.
///
/// Security: the authorized communities are carried in the signed token only — never read from
/// request bodies, query strings, or headers (never trust a client-supplied CommunityId).
/// </summary>
public static class CommunityClaimTypes
{
    /// <summary>Acting user id. Mapped from the JWT <c>sub</c> registered claim.</summary>
    public const string UserId = "sub";

    /// <summary>
    /// One claim per CommunityId the user is authorized for (repeated claim). Issued from the
    /// user's UserRole scope rows by Auth_Service (5.2). Values are GUID strings.
    /// </summary>
    public const string CommunityId = "communityos:community_id";

    /// <summary>Boolean ("true"/"false"); grants the cross-community super-admin short-circuit (Req 5.5).</summary>
    public const string IsPlatformSuperAdmin = "communityos:is_platform_super_admin";

    /// <summary>Boolean ("true"/"false"); when true the resident self-scope predicate applies (Req 5.4).</summary>
    public const string IsResidentSelfScoped = "communityos:is_resident_self_scoped";

    /// <summary>The resident's own unit id (GUID), used by the self-scope predicate when present.</summary>
    public const string ResidentUnitId = "communityos:resident_unit_id";

    /// <summary>The resident's own household id (GUID), used by the self-scope predicate when present.</summary>
    public const string HouseholdId = "communityos:household_id";
}
