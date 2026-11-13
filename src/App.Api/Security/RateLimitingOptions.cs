using System.ComponentModel.DataAnnotations;

namespace App.Api.Security;

/// <summary>
/// Rate-limiting tunables bound from the <c>RateLimiting</c> configuration section (options
/// pattern — permit counts and windows are never hardcoded, Req 65.5). Two policies:
/// <list type="bullet">
/// <item><b>Auth</b> — the strict policy on login / forgot-password / reset-password, the prime
/// brute-force / enumeration targets.</item>
/// <item><b>Write</b> — the general policy on mutating (POST/PUT/PATCH/DELETE) endpoints.</item>
/// </list>
/// Both are fixed-window limiters partitioned by caller identity (see <c>RateLimiterPolicies</c>).
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Master switch; lets a host disable limiting (e.g. an internal environment).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Strict limiter for the pre-auth endpoints (login/forgot/reset).</summary>
    [Required]
    public FixedWindowPolicyOptions Auth { get; set; } = new();

    /// <summary>General limiter for mutating endpoints.</summary>
    [Required]
    public FixedWindowPolicyOptions Write { get; set; } = new();
}

/// <summary>A single fixed-window policy: at most <see cref="PermitLimit"/> requests per window.</summary>
public sealed class FixedWindowPolicyOptions
{
    /// <summary>Maximum requests allowed per partition within one window.</summary>
    [Range(1, 100000)]
    public int PermitLimit { get; set; } = 60;

    /// <summary>Window length in seconds.</summary>
    [Range(1, 86400)]
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Max requests allowed to queue when the window is full (0 = reject immediately).</summary>
    [Range(0, 10000)]
    public int QueueLimit { get; set; }
}
