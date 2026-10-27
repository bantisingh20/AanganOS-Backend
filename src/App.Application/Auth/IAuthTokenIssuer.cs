namespace App.Application.Auth;

/// <summary>
/// Issues the signed bearer token for a successful login (Req 1.1). The seam keeps the JWT library
/// in Infrastructure and lets Auth_Service stay transport-agnostic. The issuer MUST produce a token
/// carrying exactly the claim contract the request pipeline validates (Task 2.2 /
/// <c>CommunityClaimTypes</c>): <c>sub</c>, one <c>communityos:community_id</c> per authorized
/// community, the super-admin / resident self-scope flags, the user's role names, and a <c>jti</c>
/// equal to <see cref="IssuedToken.TokenId"/> so the matching <c>LoginSession</c> can revoke it.
/// </summary>
public interface IAuthTokenIssuer
{
    /// <summary>Build and sign a token from the resolved claim inputs.</summary>
    IssuedToken Issue(TokenIssueRequest request);
}

/// <summary>
/// The claim inputs Auth_Service resolves from the authenticated user before issuance. The issuer
/// turns these into the <c>CommunityClaimTypes</c> claims — the one place the claim contract is
/// materialised on the issuance side.
/// </summary>
/// <param name="UserId">The authenticated user's id → the <c>sub</c> claim.</param>
/// <param name="AuthorizedCommunityIds">Distinct communities from the user's active UserRole rows → repeated community claims.</param>
/// <param name="IsPlatformSuperAdmin">Whether the user holds a platform-scoped role → super-admin claim.</param>
/// <param name="RoleNames">The user's assigned role names → role claims (Req 1.1 returns roles).</param>
/// <param name="IsResidentSelfScoped">Resident self-scope flag (foundation: false; residents are Phase 2).</param>
/// <param name="ResidentUnitId">Resident's own unit, when self-scoped.</param>
/// <param name="HouseholdId">Resident's own household, when self-scoped.</param>
public sealed record TokenIssueRequest(
    Guid UserId,
    IReadOnlyCollection<Guid> AuthorizedCommunityIds,
    bool IsPlatformSuperAdmin,
    IReadOnlyCollection<string> RoleNames,
    bool IsResidentSelfScoped = false,
    Guid? ResidentUnitId = null,
    Guid? HouseholdId = null,
    IReadOnlyCollection<string>? Permissions = null);

/// <summary>The signed token plus the identifiers Auth_Service records in the <c>LoginSession</c>.</summary>
/// <param name="AccessToken">The signed JWT string returned to the client.</param>
/// <param name="TokenId">The token's unique id (<c>jti</c>); stored as <c>LoginSession.TokenId</c> for revocation.</param>
/// <param name="IssuedAtUtc">Issuance instant (UTC).</param>
/// <param name="ExpiresAtUtc">Expiry instant (UTC).</param>
public sealed record IssuedToken(
    string AccessToken,
    string TokenId,
    DateTime IssuedAtUtc,
    DateTime ExpiresAtUtc);
