namespace CommunityOS.Domain.Identity;

/// <summary>
/// Lifecycle state of a <see cref="User"/> principal. Independent of soft-delete: an
/// <see cref="Active"/> user can log in; a <see cref="Suspended"/> user is retained but denied
/// access by Auth_Service (Task 5.2). Soft-delete (archival) is a separate concern on the base
/// entity (Req 3.7).
/// </summary>
public enum UserStatus
{
    /// <summary>Normal, able to authenticate.</summary>
    Active = 0,

    /// <summary>Temporarily disabled by an administrator; cannot authenticate.</summary>
    Suspended = 1,
}
