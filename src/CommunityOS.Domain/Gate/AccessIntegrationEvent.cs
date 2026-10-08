using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Gate;

/// <summary>
/// An append-only record of a hardware/access-control integration being triggered at a gate
/// (Req 27.5) — a boom barrier opened/closed, a CCTV snapshot requested, an access-control event
/// pushed. Carries its owning <see cref="TenantOwnedEntity.CommunityId"/>.
///
/// <para><b>Why a record, not just a call.</b> Req 27.5 requires the Gate_Service to RECORD each
/// triggered integration event. The actual hardware call goes through the
/// <c>IAccessControlIntegration</c> seam (a no-op/logging default in this phase; real drivers are
/// Phase 12 / Task 40); this row is the durable, tenant-scoped audit of what was triggered and
/// whether the seam reported success — independent of whether a real device was attached.</para>
///
/// <para><b>Not <c>IUnitScoped</c>.</b> A gate operations record scoped by community (the tenant
/// filter), never pinned to one unit.</para>
///
/// <para><b>Configurable action / status (steering: no hardcoding).</b> <see cref="Action"/>
/// resolves against the editable <c>Access_Integration_Action</c> list (open_barrier / close_barrier
/// / cctv_snapshot / access_event); <see cref="Status"/> against <c>Access_Integration_Status</c>
/// (triggered / succeeded / failed). The config declares a <c>(CommunityId, CreatedAtUtc)</c> index
/// so the chronological event log is a single indexed read.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token, audit columns.</para>
/// </summary>
public class AccessIntegrationEvent : TenantOwnedEntity
{
    /// <summary>Optional gate id the event relates to (the Gate master is a later task), or null.</summary>
    public Guid? GateId { get; set; }

    /// <summary>Optional free-text gate/device label captured on the event.</summary>
    public string? GateLabel { get; set; }

    /// <summary>
    /// Configurable integration-action code resolved against the <c>Access_Integration_Action</c>
    /// master-data list (open_barrier / close_barrier / cctv_snapshot / access_event, Req 27.5).
    /// Stored as the stable code, never a frozen enum.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Configurable outcome code resolved against the <c>Access_Integration_Status</c> master-data
    /// list (triggered / succeeded / failed). Reflects what the integration seam reported.
    /// </summary>
    public string Status { get; set; } = AccessIntegrationStatuses.Triggered;

    /// <summary>Optional small, secret-free detail about the trigger (device reference, reason).</summary>
    public string? Detail { get; set; }

    /// <summary>The user who triggered the integration event (Req 27.5), or null for a system trigger.</summary>
    public Guid? TriggeredByUserId { get; set; }
}

/// <summary>
/// Well-known default <c>Access_Integration_Action</c> codes (Req 27.5). Seeded master-data codes
/// kept as constants. <b>Data</b>, not a behavioural enum — the list is editable.
/// </summary>
public static class AccessIntegrationActions
{
    /// <summary>Open the boom barrier.</summary>
    public const string OpenBarrier = "open_barrier";

    /// <summary>Close the boom barrier.</summary>
    public const string CloseBarrier = "close_barrier";

    /// <summary>Request a CCTV snapshot.</summary>
    public const string CctvSnapshot = "cctv_snapshot";

    /// <summary>Push a generic access-control event.</summary>
    public const string AccessEvent = "access_event";
}

/// <summary>
/// Well-known default <c>Access_Integration_Status</c> codes (Req 27.5). Seeded master-data codes
/// kept as constants. <b>Data</b>, not a behavioural enum — the list is editable.
/// </summary>
public static class AccessIntegrationStatuses
{
    /// <summary>The integration was triggered (default when the seam is a no-op).</summary>
    public const string Triggered = "triggered";

    /// <summary>The integration seam reported success.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>The integration seam reported failure (non-fatal; recorded, not thrown).</summary>
    public const string Failed = "failed";
}
