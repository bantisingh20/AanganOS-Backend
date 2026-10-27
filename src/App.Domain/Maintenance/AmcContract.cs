using App.Domain.Common;

namespace App.Domain.Maintenance;

/// <summary>
/// An Annual Maintenance Contract (AMC) covering an <see cref="Asset"/> with a vendor over a fixed
/// period (Req 37.2): a Facility Manager records the vendor, the covered asset, the start and end
/// (expiry) dates and the user responsible for the contract. While the contract is within the
/// configured expiry-reminder window (<c>maintenance.amc.expiryReminderDays</c>), a bounded
/// background sweep notifies the responsible user that the AMC is approaching expiry (Req 37.3).
///
/// <para><b>Community infrastructure, NOT resident self-view data.</b> Like <see cref="Asset"/>, an
/// AMC belongs to the community, so it is a <see cref="TenantOwnedEntity"/> deliberately NOT
/// <see cref="IUnitScoped"/> — a Facility-Manager register bounded by the community (the tenant
/// global filter).</para>
///
/// <para><b>Configurable status (steering: no hardcoding).</b> <see cref="Status"/> resolves against
/// the editable <c>Amc_Contract_Status</c> master-data list (active / expired / cancelled), stored as
/// a stable code never a frozen enum. The reminder WINDOW (days before expiry) is itself configurable
/// via Config_Service, not a hardcoded constant.</para>
///
/// <para><b>At-most-once expiry reminder under concurrency (Req 37.3, steering: concurrency safety).</b>
/// <see cref="ExpiryReminderSentForEndDateUtc"/> is the at-most-once marker: the sweep only reminds a
/// contract whose marker does not already equal its current <see cref="EndDateUtc"/>, then stamps the
/// marker to the end date — all under the contract's inherited <c>RowVersion</c>, so two concurrent
/// sweeps racing the same contract both read the same unset marker but only one SaveChanges wins; the
/// loser trips a <c>DbUpdateConcurrencyException</c> mapped centrally to CONFLICT and is skipped. The
/// contract can therefore never be reminded twice for the same expiry window. If the end date is later
/// extended (a renewal), the marker no longer matches the new end date, so the NEXT window reminds
/// again — exactly the desired behaviour. Mirrors the one-time escalation stamp on <c>Ticket</c>.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token (Req 22.4), audit columns.</para>
/// </summary>
public class AmcContract : TenantOwnedEntity
{
    /// <summary>The asset this AMC covers (Req 37.2). Required.</summary>
    public Guid AssetId { get; set; }

    /// <summary>Human-readable contract name/reference (Req 37.2). Required.</summary>
    public string ContractReference { get; set; } = string.Empty;

    /// <summary>The vendor providing the maintenance under the contract (Req 37.2). Required free text.</summary>
    public string VendorName { get; set; } = string.Empty;

    /// <summary>Optional vendor contact (phone/email) for the contract (Req 37.2).</summary>
    public string? VendorContact { get; set; }

    /// <summary>When the AMC coverage begins, in UTC (Req 37.2). Required.</summary>
    public DateTime StartDateUtc { get; set; }

    /// <summary>When the AMC coverage ends / expires, in UTC (Req 37.2, 37.3). Required.</summary>
    public DateTime EndDateUtc { get; set; }

    /// <summary>
    /// The user responsible for the contract (Req 37.3), notified when it approaches expiry. Required
    /// so the expiry reminder has a definite recipient (no hardcoded fallback).
    /// </summary>
    public Guid ResponsibleUserId { get; set; }

    /// <summary>
    /// Configurable status code resolved against the <c>Amc_Contract_Status</c> master-data list
    /// (active / expired / cancelled). Starts <c>active</c> on creation. Stored as the stable code,
    /// never a frozen enum.
    /// </summary>
    public string Status { get; set; } = AmcContractStatuses.Active;

    /// <summary>Optional annual contract value (Req 37.2). Non-negative when supplied.</summary>
    public decimal? ContractValue { get; set; }

    /// <summary>Optional free-text notes about the contract (Req 37.2).</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// At-most-once expiry-reminder marker (Req 37.3): the <see cref="EndDateUtc"/> value an expiry
    /// reminder was last sent for, or null if none has been sent. The sweep reminds only when this is
    /// null or does not equal the current end date, so a renewal (new end date) re-arms the reminder
    /// for the next window — see the type remarks.
    /// </summary>
    public DateTime? ExpiryReminderSentForEndDateUtc { get; set; }
}

/// <summary>
/// Well-known default <c>Amc_Contract_Status</c> codes (Req 37.2). Seeded master-data codes kept as
/// constants so the service sets/compares status without a magic string. <b>Data</b>, not a
/// behavioural enum — the status list is editable master data.
/// </summary>
public static class AmcContractStatuses
{
    /// <summary>Initial status on creation (Req 37.2): the contract is in force.</summary>
    public const string Active = "active";

    /// <summary>The contract has passed its end date (Req 37.2, 37.3).</summary>
    public const string Expired = "expired";

    /// <summary>The contract was cancelled before its end date (Req 37.2).</summary>
    public const string Cancelled = "cancelled";
}
