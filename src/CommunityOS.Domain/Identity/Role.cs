using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Identity;

/// <summary>
/// A named collection of <see cref="Permission"/>s assignable to users (Req 3.5). A role is
/// defined either platform-wide or for a single community (<see cref="Scope"/> + optional
/// <see cref="CommunityId"/>). Unique(Name, Scope, CommunityId) (Req 3.6) means the same role name
/// can exist once per scope — enforced at the DB level with filtered unique indexes (because
/// <see cref="CommunityId"/> is nullable), so a duplicate is caught concurrency-safely and mapped
/// to a conflict rather than pre-checked with a read-then-write.
///
/// <para>Platform-scoped entity: a role is a definition, not community-owned data. The owning
/// community of a community role is carried in <see cref="CommunityId"/> for the uniqueness scope,
/// but the global tenant filter is deliberately not applied (same reason as Configuration) so role
/// management is explicit in the service layer.</para>
///
/// <para>No role name is special-cased in code (steering: no behaviour-gating hardcoded role
/// names). Privileges — including the platform-super-admin short-circuit — flow from the
/// permissions/claims a role confers, never from matching a magic name.</para>
/// </summary>
public class Role : PlatformEntity
{
    /// <summary>Human-readable role name, unique within its scope (Req 3.6). Required.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether this role is platform-wide or community-scoped (Req 3.6).</summary>
    public RoleScope Scope { get; set; }

    /// <summary>Owning community for a <see cref="RoleScope.Community"/> role; null for a platform role. Part of Unique(Name, Scope, CommunityId).</summary>
    public Guid? CommunityId { get; set; }

    /// <summary>The permission grants of this role (Req 3.5). Navigations for the role-definition set.</summary>
    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}
