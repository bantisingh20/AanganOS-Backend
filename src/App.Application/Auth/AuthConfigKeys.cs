namespace App.Application.Auth;

/// <summary>
/// Config_Service keys for the per-community-overridable Auth_Service thresholds (steering: no
/// hardcoding; thresholds come from Config_Service/options, never literals). The constant is only
/// the key; the value lives in config/seed data and falls back to <see cref="AuthOptions"/> when no
/// community/platform override is set.
/// </summary>
public static class AuthConfigKeys
{
    /// <summary>Consecutive failed attempts before lockout (overrides <see cref="AuthOptions.LockoutThreshold"/>).</summary>
    public const string LockoutThreshold = "auth.lockout.threshold";

    /// <summary>Lockout counting window / lock duration in minutes (overrides <see cref="AuthOptions.LockoutWindowMinutes"/>).</summary>
    public const string LockoutWindowMinutes = "auth.lockout.windowMinutes";

    /// <summary>OTP validity window in minutes (overrides <see cref="AuthOptions.OtpExpiryMinutes"/>).</summary>
    public const string OtpExpiryMinutes = "auth.otp.expiryMinutes";
}
