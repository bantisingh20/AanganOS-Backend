namespace CommunityOS.Domain.Audit;

/// <summary>
/// A single append-only audit record (Req 6.1–6.5). Written once when an auditable mutation or a
/// login/security event occurs and <b>never updated or deleted through the application</b> — the
/// SaveChanges interceptor rejects any Modified/Deleted state on this type, and the Audit_Service
/// exposes only an append path (design: "Append-only <c>AuditLog</c>, no app-level update/delete").
///
/// <para><b>Why this is a plain entity, not a <c>TenantOwnedEntity</c>.</b> Audit rows must:
/// <list type="bullet">
///   <item>carry a <i>nullable</i> <see cref="CommunityId"/> — platform-level events (e.g. a
///   platform-config edit or a cross-community admin action) have no owning community;</item>
///   <item>survive the soft-delete of the resource they describe (Req 6.4) — because they are
///   separate rows that are never deleted, they naturally outlive the referenced record;</item>
///   <item>be immutable, so the <c>RowVersion</c>/soft-delete columns of the shared bases add no
///   value here.</item>
/// </list>
/// The global tenant query filter on <c>TenantOwnedEntity</c> assumes a non-null
/// <c>CommunityId</c> and would hide platform (null-community) audit rows from everyone. So this
/// type is deliberately filter-free at the EF layer and tenant isolation of <i>reads</i> is
/// enforced one layer up, inside the Audit_Service read path (community admins see only their
/// authorized communities; a platform super admin sees across) — mirroring how Config_Service
/// scopes its platform-scoped rows (Req 6.5, design: Super Admin reads cross-community on
/// explicit audit-retrieval queries).</para>
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Identifier of the user who performed the action, from the request tenant context (Req 6.1). Null for a system/anonymous actor.</summary>
    public string? ActingUserId { get; set; }

    /// <summary>
    /// The action performed, e.g. <c>Create</c>, <c>Update</c>, <c>Delete</c>, <c>Approve</c>,
    /// <c>Export</c>, <c>Login</c>, <c>LoginFailed</c>, <c>Lockout</c> (Req 6.1, 6.2). An open
    /// string, not a frozen enum, so later modules record their own verbs without a schema change
    /// (steering: no hardcoding / do not block later modules).
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The type of resource acted on, e.g. <c>Configuration</c>, <c>User</c>, <c>Parcel</c> (Req 6.1). Open string.</summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>Identifier of the specific resource instance acted on (Req 6.1); may be a key for config/security events.</summary>
    public string? ResourceId { get; set; }

    /// <summary>Owning community for a community-scoped event; <c>null</c> for a platform-level event (Req 6.5).</summary>
    public Guid? CommunityId { get; set; }

    /// <summary>When the action occurred, in UTC, from the injected clock (Req 6.1).</summary>
    public DateTime TimestampUtc { get; set; }

    /// <summary>
    /// Free-form detail of the event (typically a small JSON blob describing before→after values
    /// or the security-event context, Req 6.2). Secret values (passwords, OTPs, tokens) must be
    /// redacted by the caller before they reach here (steering: never log secret values).
    /// </summary>
    public string? Detail { get; set; }
}
