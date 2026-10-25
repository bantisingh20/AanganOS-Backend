using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Residents;

/// <summary>
/// A single resident's opt-in decision for one notification category (Req 20.1). The
/// Notification_Service consults these rows for OPTIONAL categories only: an optional notification
/// is delivered to the resident just when a row says <see cref="OptedIn"/> (or, when no row exists,
/// the configured default). Mandatory account/security notices never consult preferences and are
/// always sent (Req 20.4) — that decision is made in the Notification_Service, not here.
///
/// <para><b>Per-resident, per-category.</b> <see cref="Unique"/>(<see cref="ResidentId"/>,
/// <see cref="Category"/>) is enforced in the entity configuration, so each category has exactly one
/// preference row per resident (Req 20.1) and the preference check is a single indexed read.</para>
///
/// <para><b>Configurable category (steering: no hardcoding).</b> <see cref="Category"/> is a stable
/// master-data code validated at runtime against the editable <c>Notification_Category</c> list, the
/// same vocabulary the Task 7.1 notification templates classify against — not a frozen enum.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>, so it carries the owning
/// <see cref="TenantOwnedEntity.CommunityId"/> (same community as its resident, set by the service)
/// and inherits the tenant + soft-delete global filter, the rowversion concurrency token and audit
/// columns. It is intentionally NOT <see cref="IUnitScoped"/>: a resident reaches their own
/// preferences through their own <see cref="Resident"/> row, which is already tenant-scoped.</para>
/// </summary>
public class CommunicationPreference : TenantOwnedEntity
{
    /// <summary>The resident this preference belongs to (Req 20.1). Required.</summary>
    public Guid ResidentId { get; set; }

    /// <summary>Navigation to the owning resident.</summary>
    public Resident? Resident { get; set; }

    /// <summary>
    /// Notification-category code resolved against the configurable <c>Notification_Category</c>
    /// master-data list (Req 20.1). Stored as the stable code, never a frozen enum.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Whether the resident has opted into this optional category (Req 20.1). <c>true</c> = receive,
    /// <c>false</c> = suppress. Only meaningful for optional categories (Req 20.3).
    /// </summary>
    public bool OptedIn { get; set; }
}
