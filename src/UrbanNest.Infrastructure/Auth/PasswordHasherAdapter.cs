using UrbanNest.Application.Auth;
using UrbanNest.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace UrbanNest.Infrastructure.Auth;

/// <summary>
/// <see cref="IPasswordHasher"/> over ASP.NET Core's <see cref="PasswordHasher{TUser}"/> (Req 2.5).
///
/// <para><b>Why this and not a new dependency.</b> The platform already ships a vetted, salted
/// password hasher (PBKDF2 with a per-hash random salt, HMAC-SHA256/512, iteration count baked into
/// the hash format and verified in constant time). Reusing it satisfies "salted cryptographic
/// hashing" without adding a BCrypt/Argon2 package (ponytail: the platform already covers this).
/// The same primitive hashes OTP codes at rest — a short-lived single-use secret that must not be
/// recoverable from the database.</para>
///
/// <para>The hasher is user-agnostic here (the <c>null!</c> user argument is unused by the default
/// implementation), so one shared instance serves passwords and OTPs.</para>
/// </summary>
public sealed class PasswordHasherAdapter : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        return _inner.HashPassword(null!, plaintext);
    }

    public bool Verify(string hash, string plaintext)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(plaintext))
        {
            return false;
        }

        var result = _inner.VerifyHashedPassword(null!, hash, plaintext);
        return result is PasswordVerificationResult.Success
            or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
