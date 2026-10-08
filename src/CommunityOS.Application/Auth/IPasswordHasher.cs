namespace CommunityOS.Application.Auth;

/// <summary>
/// Salted cryptographic password hashing (Req 2.5). A seam over the actual algorithm so Auth_Service
/// never embeds a specific crypto primitive and the implementation (ASP.NET Core's
/// <c>PasswordHasher</c>, PBKDF2) lives in Infrastructure. Also used to hash OTP codes at rest.
///
/// <para><b>Security.</b> <see cref="Hash"/> salts internally (every call for the same input yields
/// a different hash) and <see cref="Verify"/> compares in constant time. Callers store only the
/// returned hash; the plaintext is never persisted or logged (steering: never log secrets).</para>
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Produce a salted hash of <paramref name="plaintext"/> suitable for storage.</summary>
    string Hash(string plaintext);

    /// <summary>
    /// Verify <paramref name="plaintext"/> against a stored <paramref name="hash"/> in constant time.
    /// Returns true when they match.
    /// </summary>
    bool Verify(string hash, string plaintext);
}
