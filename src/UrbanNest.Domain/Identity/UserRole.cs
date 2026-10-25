using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Identity;

/// <summary>
/// The assignment of a <see cref="Role"/> to a <see cref="User"/>, optionally scoped to one
/// community (Req 3.3). This is the row that defines a user's <b>authorized communities</b>: the
/// set of distinct <see cref="CommunityId"/> across a user's UserRoles is exactly what
/// Auth_Service (Task 5.2) issues as community claims and <c>ClaimsTenantContextMapper</c> reads
/// into <c>ITenantContext.AuthorizedCommunityIds</c>.
///
/// <para>Assigning a role creates one of these (Req 3.3); removing it revokes the row so the
/// role's permissions no longer apply to the user (Req 3.4) — the Task 5.3 effective-permissions
/// query only unions permissions from existing (non-soft-deleted) UserRoles. Unique(UserId,
/// RoleId, CommunityId) (Req 3.3) prevents a duplicate assignment; because <see cref="CommunityId"/>
/// is nullable it is enforced with filtered unique indexes.</para>
///
/// <para>Soft-deletable (<see cref="PlatformEntity"/>) so a revoke is traceable: the row is
/// archived rather than physically removed, keeping the audit history of who had what access and
/// when it was revoked (Req 3.4, 3.7).</para>
/// </summary>
public class UserRole : PlatformEntity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid RoleId { get; set; }
    public Role? Role { get; set; }

    /// <summary>
    /// Community this assignment is scoped to (Req 3.3). Null for a platform-wide assignment (e.g. a
    /// platform-administration role granted across the platform). Part of Unique(UserId, RoleId,
    /// CommunityId), and the value projected into the user's authorized-communities claim set.
    /// </summary>
    public Guid? CommunityId { get; set; }
}
