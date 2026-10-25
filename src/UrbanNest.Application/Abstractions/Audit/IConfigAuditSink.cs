namespace UrbanNest.Application.Abstractions.Audit;

/// <summary>
/// The audit seam Config_Service writes through when a configuration value or master-data entry is
/// edited (Req 8.2). Task 4.1 (Audit_Service / append-only Audit_Log) is not built yet; this
/// interface is the clean boundary it will implement. Until then a no-op implementation is
/// registered so config edits are never blocked — Config_Service depends only on this seam, so
/// 4.1 plugs in without touching Config_Service.
///
/// ponytail: deliberately minimal — just enough shape (who/what/scope/old→new) for 4.1 to record a
/// faithful Audit_Log entry. Upgrade path: Task 4.1 replaces the no-op with the real append-only
/// sink; no change needed here or at the call site.
/// </summary>
public interface IConfigAuditSink
{
    /// <summary>
    /// Record that a configuration/master-data change occurred. Must not throw into the business
    /// path for a non-critical audit failure once 4.1 implements it; the no-op trivially satisfies
    /// that. The change is already persisted when this is called (audit records the fact).
    /// </summary>
    Task RecordConfigChangeAsync(ConfigChange change, CancellationToken cancellationToken = default);
}

/// <summary>
/// Immutable description of a single config/master-data edit for the audit trail (Req 8.2):
/// the acting user, the key/list touched, its scope and community, and the before/after values.
/// </summary>
/// <param name="ResourceType">"Configuration" or "MasterDataItem".</param>
/// <param name="Key">The configuration key or master-data list key + code.</param>
/// <param name="CommunityId">Owning community for a community-scoped edit; null for a platform edit.</param>
/// <param name="OldValue">The prior value, or null when the row was newly created.</param>
/// <param name="NewValue">The persisted new value.</param>
/// <param name="ActingUser">Identifier of the user who made the change (from the tenant context).</param>
public readonly record struct ConfigChange(
    string ResourceType,
    string Key,
    Guid? CommunityId,
    string? OldValue,
    string NewValue,
    string? ActingUser);
