using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Identity;

/// <summary>
/// A single-use one-time code for a password reset / identity-verification flow (Req 2.1–2.4).
///
/// <para><b>Security.</b> The code is stored only as a <see cref="HashedCode"/> (a salted hash,
/// same algorithm as a password) — never in plaintext, so a database read cannot recover a live
/// OTP. Single-use: once consumed <see cref="UsedAtUtc"/> is stamped and the token can never be
/// redeemed again (Req 2.3). Expires within 10 minutes of generation via <see cref="ExpiresAtUtc"/>
/// (Req 2.2); an expired or already-used token is rejected (Req 2.4).</para>
/// </summary>
public class OtpToken : PlatformEntity
{
    /// <summary>The user the OTP was issued to.</summary>
    public Guid UserId { get; set; }

    /// <summary>What the OTP authorizes.</summary>
    public OtpPurpose PurposeType { get; set; }

    /// <summary>Salted hash of the OTP code (never the plaintext code). Verified at redemption.</summary>
    public string HashedCode { get; set; } = string.Empty;

    /// <summary>When the OTP expires (UTC); generated-at + the configured expiry window (Req 2.2).</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>When the OTP was consumed (UTC). Null while unused; set on successful redemption (Req 2.3).</summary>
    public DateTime? UsedAtUtc { get; set; }
}
