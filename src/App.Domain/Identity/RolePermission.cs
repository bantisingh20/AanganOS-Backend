namespace App.Domain.Identity;

/// <summary>
/// Join row associating a <see cref="Role"/> with one <see cref="Permission"/> it grants
/// (Req 3.5). PK(RoleId, PermissionId) — a role grants each permission at most once.
///
/// <para>Deliberately a plain join entity, not a soft-deletable/rowversioned base: a role's
/// permission set is replaced wholesale when the role is (re)defined, so individual grants are
/// hard-inserted/deleted rather than archived. Audit of the role definition records the resulting
/// set. ponytail: keeping this minimal avoids soft-delete bookkeeping on a pure link row.</para>
/// </summary>
public class RolePermission
{
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }

    public Guid PermissionId { get; set; }
    public Permission? Permission { get; set; }
}
