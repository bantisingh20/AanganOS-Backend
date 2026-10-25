using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Identity;

/// <summary>
/// A server-side record of an issued bearer token so a logout can revoke it (Req 1.3). The JWT is
/// stateless; carrying its <see cref="TokenId"/> (the token's <c>jti</c>) here lets the bearer
/// validation reject a token whose session has been revoked, so a logged-out token stops working
/// even before it expires.
///
/// <para><b>Security.</b> This stores only the opaque token id and lifetime — never the token
/// string itself, and never any credential. Platform-scoped (a session belongs to a user, not a
/// single community).</para>
/// </summary>
public class LoginSession : PlatformEntity
{
    /// <summary>The user this session was issued to.</summary>
    public Guid UserId { get; set; }

    /// <summary>The token's unique id (JWT <c>jti</c>); the handle logout revokes and validation checks.</summary>
    public string TokenId { get; set; } = string.Empty;

    /// <summary>When the token was issued (UTC).</summary>
    public DateTime IssuedAtUtc { get; set; }

    /// <summary>When the token expires (UTC); a session past this is dead regardless of revocation.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>When the session was revoked (logout, Req 1.3). Null while active.</summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>
    /// Salted hash of this session's refresh-token SECRET (never the plaintext). The client holds a
    /// refresh token of the form <c>&lt;sessionId&gt;.&lt;secret&gt;</c>; on refresh the server looks the
    /// session up by its <see cref="Common.PlatformEntity.Id"/> and verifies the secret against this
    /// hash. Rotated on every successful refresh (one-time use → replay protection). Null when the
    /// session has no refresh token.
    /// </summary>
    public string? RefreshTokenHash { get; set; }

    /// <summary>When the refresh token expires (UTC). After this, refresh fails and the user must re-login.</summary>
    public DateTime? RefreshTokenExpiresAtUtc { get; set; }
}
