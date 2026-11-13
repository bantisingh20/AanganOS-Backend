using App.Domain.Common;

namespace App.Domain.Identity;


public class OtpToken : PlatformEntity
{
    /// <summary>The user the OTP was issued to. Null for a pre-account flow (registration), which keys on <see cref="Phone"/>.</summary>
    public Guid? UserId { get; set; }

    /// <summary>The phone the OTP was issued to when there is no user yet (registration). Null for user-keyed OTPs.</summary>
    public string? Phone { get; set; }

    /// <summary>What the OTP authorizes.</summary>
    public OtpPurpose PurposeType { get; set; }

    /// <summary>Salted hash of the OTP code (never the plaintext code). Verified at redemption.</summary>
    public string HashedCode { get; set; } = string.Empty;

    /// <summary>When the OTP expires (UTC); generated-at + the configured expiry window (Req 2.2).</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>When the OTP was consumed (UTC). Null while unused; set on successful redemption (Req 2.3).</summary>
    public DateTime? UsedAtUtc { get; set; }
}
