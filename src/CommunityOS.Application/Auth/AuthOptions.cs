using System.ComponentModel.DataAnnotations;

namespace CommunityOS.Application.Auth;

/// <summary>
/// Auth_Service tunables bound from the <c>Auth</c> configuration section (options pattern —
/// nothing hardcoded, steering). These are the platform defaults; per-community overrides are
/// resolved at runtime through Config_Service under the <see cref="AuthConfigKeys"/> keys, so a
/// community can tighten/loosen the lockout or OTP window without a code change (Req 1.6, 2.2).
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Consecutive failed-password attempts that trigger a lockout (Req 1.6). Default 5.</summary>
    [Range(1, 100)]
    public int LockoutThreshold { get; init; } = 5;

    /// <summary>
    /// The window, in minutes, over which consecutive failures are counted AND the duration the
    /// account is locked once the threshold is hit (Req 1.6 uses the same 15 minutes for both).
    /// </summary>
    [Range(1, 1440)]
    public int LockoutWindowMinutes { get; init; } = 15;

    /// <summary>How long a generated OTP stays valid, in minutes (Req 2.2). Default 10.</summary>
    [Range(1, 60)]
    public int OtpExpiryMinutes { get; init; } = 10;

    /// <summary>Number of digits in a generated OTP code. Default 6.</summary>
    [Range(4, 10)]
    public int OtpLength { get; init; } = 6;

    /// <summary>
    /// Issued bearer-token lifetime, in minutes. Default 60. The range allows up to 30 days so a
    /// deployment without a refresh-token flow can issue a long-lived session token and avoid
    /// re-prompting the user on every expiry.
    /// </summary>
    [Range(1, 43200)]
    public int TokenLifetimeMinutes { get; init; } = 60;
}
