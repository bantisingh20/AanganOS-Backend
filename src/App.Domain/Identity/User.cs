using App.Domain.Common;

namespace App.Domain.Identity;

/// <summary>
/// An authenticated principal of the platform.
/// </summary>
public class User : PlatformEntity
{
    public string? Name { get; set; }

    /// <summary>Login/contact email. Unique across users (filtered unique index). Optional (a user may be phone-only).</summary>
    public string? Email { get; set; }

    /// <summary>Login/contact phone. Unique across users (filtered unique index). Optional (a user may be email-only).</summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Salted password hash. Only stored here; produced/verified by Auth_Service. Null until a credential is set. Never logged.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>Lifecycle state (independent of soft-delete). Auth_Service denies non-Active users.</summary>
    public UserStatus Status { get; set; } = UserStatus.Active;

    /// <summary>Consecutive failed-login counter.</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>
    /// When the current run of consecutive failures began (UTC).
    /// </summary>
    public DateTime? LockoutWindowStartedAtUtc { get; set; }

    /// <summary>When set and in the future, the account is locked out until this instant.</summary>
    public DateTime? LockoutUntil { get; set; }

    /// <summary>Whether multi-factor/OTP step-up is enabled for the user.</summary>
    public bool MfaEnabled { get; set; }

    /// <summary>Direct single role assigned to this user.</summary>
    public Guid? RoleId { get; set; }
    public Role? Role { get; set; }

    /// <summary>Direct single community assigned to this user (null for superadmin).</summary>
    public Guid? CommunityId { get; set; }

    /// <summary>Direct organization assigned to this user.</summary>
    public Guid? OrganizationId { get; set; }
    public App.Domain.Community.Organization? Organization { get; set; }
}
