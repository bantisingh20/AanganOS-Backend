namespace UrbanNest.Domain.Identity;

/// <summary>
/// Why an <see cref="OtpToken"/> was issued. Kept as a small closed enum (the foundation only needs
/// password reset); later step-up/MFA flows add members without changing the storage shape.
/// </summary>
public enum OtpPurpose
{
    /// <summary>Password reset / recovery (Req 2.1–2.4).</summary>
    PasswordReset = 0,
}
