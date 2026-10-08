using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Maintenance;

/// <summary>
/// A preventive-maintenance schedule for an <see cref="Asset"/> (Req 36.1): a Facility Manager
/// defines a recurring plan (e.g. "service Lift A every 30 days") and the Maintenance_Service
/// generates a preventive <see cref="WorkOrder"/> each time a scheduled occurrence falls due. Carries
/// its owning <see cref="TenantOwnedEntity.CommunityId"/>.
///
/// <para><b>Community infrastructure, NOT resident self-view data.</b> Like <see cref="Asset"/> (and
/// <c>CustodyLocation</c> / <c>ParkingSlot</c>), a plan belongs to the community, so it is a
/// <see cref="TenantOwnedEntity"/> that is deliberately NOT <see cref="IUnitScoped"/> — it is a
/// Facility-Manager register bounded by the community (the tenant global filter), not pinned to one
/// resident's unit.</para>
///
/// <para><b>Configurable frequency + interval (steering: no hardcoding, Req 36.1).</b>
/// <see cref="Frequency"/> resolves against the editable <c>Maintenance_Plan_Frequency</c>
/// master-data list (daily / weekly / monthly / quarterly / half_yearly / yearly / custom), stored
/// as a stable code never a frozen enum. <see cref="IntervalDays"/> is the concrete spacing in days
/// between occurrences the service advances <see cref="NextDueAtUtc"/> by — resolved from the
/// frequency's configured day count (Config_Service) or supplied directly for a <c>custom</c>
/// frequency, so the whole schedule stays configuration-driven.</para>
///
/// <para><b>At-most-once generation under concurrency (Req 36.1, steering: concurrency safety).</b>
/// <see cref="NextDueAtUtc"/> is the next occurrence instant; the background sweep generates a work
/// order only when <c>now &gt;= NextDueAtUtc</c>. Generating a work order stamps
/// <see cref="LastGeneratedAtUtc"/> + <see cref="LastGeneratedForDueAtUtc"/> (the occurrence it
/// generated for) and ADVANCES <see cref="NextDueAtUtc"/> by <see cref="IntervalDays"/> — all under
/// the plan's inherited <c>RowVersion</c>, so two concurrent sweeps racing the same due plan both
/// read the same <c>NextDueAtUtc</c> but only one SaveChanges wins; the loser trips a
/// <c>DbUpdateConcurrencyException</c> mapped centrally to CONFLICT and is skipped. The plan can
/// therefore never double-generate for the same due occurrence. Mirrors the one-time escalation
/// stamp on <c>Ticket</c>/<c>Parcel</c>, extended to also advance the schedule.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter (a retired
/// plan stays traceable but is hidden from ordinary reads), rowversion concurrency token (Req 22.4),
/// audit columns.</para>
/// </summary>
public class MaintenancePlan : TenantOwnedEntity
{
    /// <summary>The asset this preventive plan schedules maintenance for (Req 36.1). Required.</summary>
    public Guid AssetId { get; set; }

    /// <summary>Human-readable plan name/label (e.g. "Lift A - Monthly Service"). Required (Req 36.1).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional free-text description of the preventive work the plan schedules (Req 36.1).</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Configurable frequency code resolved against the <c>Maintenance_Plan_Frequency</c> master-data
    /// list (Req 36.1). Stored as the stable code, never a frozen enum. The concrete spacing is
    /// <see cref="IntervalDays"/>.
    /// </summary>
    public string Frequency { get; set; } = string.Empty;

    /// <summary>
    /// The concrete spacing in days between occurrences (Req 36.1), resolved from the frequency's
    /// configured day count or supplied for a <c>custom</c> frequency. The sweep advances
    /// <see cref="NextDueAtUtc"/> by this many days each time it generates a work order. Always
    /// positive (the service rejects a non-positive interval).
    /// </summary>
    public int IntervalDays { get; set; }

    /// <summary>
    /// The <see cref="WorkOrder.Priority"/> code to stamp on each generated preventive work order
    /// (Req 36.1, 36.2). Validated against the configurable <c>Work_Order_Priority</c> list. Stored
    /// as the stable code, never a frozen enum.
    /// </summary>
    public string Priority { get; set; } = string.Empty;

    /// <summary>
    /// The next occurrence instant the sweep generates a work order for (Req 36.1). Set to the first
    /// occurrence on creation and advanced by <see cref="IntervalDays"/> each generation. A plan with
    /// <see cref="IsActive"/> false is never swept.
    /// </summary>
    public DateTime NextDueAtUtc { get; set; }

    /// <summary>
    /// When the sweep last generated a work order from this plan (Req 36.1), or null if it never has.
    /// Together with <see cref="LastGeneratedForDueAtUtc"/> this is the at-most-once bookkeeping — see
    /// the type remarks.
    /// </summary>
    public DateTime? LastGeneratedAtUtc { get; set; }

    /// <summary>
    /// The occurrence instant the last generated work order was generated FOR (the value
    /// <see cref="NextDueAtUtc"/> held at generation time), or null if none generated yet. Lets the
    /// generated work order and the audit trail reference the exact scheduled occurrence, and makes a
    /// double-generation for the same occurrence detectable.
    /// </summary>
    public DateTime? LastGeneratedForDueAtUtc { get; set; }

    /// <summary>
    /// Whether the plan is active and swept (Req 36.1). A paused/retired plan (<c>false</c>) is left
    /// in the table (traceable) but generates no work orders. Defaults to active on creation.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Well-known default <c>Maintenance_Plan_Frequency</c> codes (Req 36.1). Seeded master-data codes
/// kept as constants so the service resolves the interval without a magic string. <b>Data</b>, not a
/// behavioural enum — the frequency list AND the per-frequency day counts are editable
/// configuration; these constants only name the seeded defaults.
/// </summary>
public static class MaintenancePlanFrequencies
{
    /// <summary>Every day.</summary>
    public const string Daily = "daily";

    /// <summary>Every 7 days.</summary>
    public const string Weekly = "weekly";

    /// <summary>Every ~30 days.</summary>
    public const string Monthly = "monthly";

    /// <summary>Every ~90 days.</summary>
    public const string Quarterly = "quarterly";

    /// <summary>Every ~182 days.</summary>
    public const string HalfYearly = "half_yearly";

    /// <summary>Every ~365 days.</summary>
    public const string Yearly = "yearly";

    /// <summary>A caller-supplied interval in days (the plan carries its own <c>IntervalDays</c>).</summary>
    public const string Custom = "custom";
}
