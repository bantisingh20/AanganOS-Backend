namespace UrbanNest.Application.Auth;

/// <summary>
/// The revocation check the bearer-token validation calls to reject a logged-out token (Req 1.3).
/// A stateless JWT stays "valid" by signature until it expires; this is how a logout takes effect
/// immediately — the token's session id (<c>jti</c>) is looked up and a revoked (or missing)
/// session fails validation.
///
/// <para><b>Scalability.</b> The lookup is a single indexed read on <c>LoginSession.TokenId</c>;
/// it runs once per authenticated request so it must stay O(1)-ish, which the unique index on
/// <c>TokenId</c> provides.</para>
/// </summary>
public interface ISessionRevocationStore
{
    /// <summary>
    /// True when the session identified by <paramref name="tokenId"/> is still active (exists and
    /// not revoked). False when it was revoked by logout or does not exist — the token is rejected.
    /// </summary>
    Task<bool> IsSessionActiveAsync(string tokenId, CancellationToken cancellationToken = default);
}
