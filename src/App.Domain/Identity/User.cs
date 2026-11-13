using App.Domain.Common;

namespace App.Domain.Identity;

/// <summary>
/// An authenticated principal of the platform (Req 3.1). Modeled as a <see cref="PlatformEntity"/>
/// — a user is NOT owned by a single community; the communities a user may access are conferred by
/// their <see cref="UserRole"/> scope rows (what <c>ClaimsTenantContextMapper</c> reads into the
/// authorized-communities set). This mirrors how Configuration/MasterDataItem are platform-scoped
/// and matches the design's Identity/RBAC entity table.
///
/// <para><b>Security.</b> <see cref="PasswordHash"/> is only a column at this task — the salted
/// hashing, verification and the lockout counters' transitions are Auth_Service (Task 5.2). The
/// value is never logged (steering: never log secret values); audit of user mutations records the
/// email/status, never the hash.</para>
/// </summary>
public class User : PlatformEntity
{
    public string? Name { get; set; }

    /// <summary>Login/contact email. Unique across users (filtered unique index, Req 3.1). Optional (a user may be phone-only).</summary>
    public string? Email { get; set; }

    /// <summary>Login/contact phone. Unique across users (filtered unique index). Optional (a user may be email-only).</summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Salted password hash (Req 2.5). Only stored here at Task 5.1; produced/verified by
    /// Auth_Service (Task 5.2). Null until a credential is set. Never logged.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>Lifecycle state (independent of soft-delete). Auth_Service denies non-Active users.</summary>
    public UserStatus Status { get; set; } = UserStatus.Active;

    /// <summary>Consecutive failed-login counter; the column backing the 5-failure lockout (Req 1.6, driven by Task 5.2).</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>
    /// When the current run of consecutive failures began (UTC). Used to enforce the "within 15
    /// minutes" part of Req 1.6: failures older than the configured window don't count toward the
    /// lockout threshold, so the counter restarts rather than accumulating across hours. Set by
    /// Auth_Service (Task 5.2); null when there is no active failure run.
    /// </summary>
    public DateTime? LockoutWindowStartedAtUtc { get; set; }

    /// <summary>When set and in the future, the account is locked out until this instant (Req 1.6, driven by Task 5.2).</summary>
    public DateTime? LockoutUntil { get; set; }

    /// <summary>Whether multi-factor/OTP step-up is enabled for the user (consumed by Task 5.2).</summary>
    public bool MfaEnabled { get; set; }
}
