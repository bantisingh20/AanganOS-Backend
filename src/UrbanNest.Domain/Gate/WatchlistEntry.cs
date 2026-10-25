using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Gate;

/// <summary>
/// A blacklist/watchlist entry naming a person or vehicle that must be flagged (and controlled) at
/// the gate (Req 26.1). Carries its owning <see cref="TenantOwnedEntity.CommunityId"/>.
///
/// <para><b>Not <c>IUnitScoped</c>.</b> The watchlist is a community-wide security register, not
/// resident self-view data — it is scoped by community (the tenant filter) and never pinned to one
/// unit. Only staff with the View permission on <c>WatchlistEntry</c> may read it (Req 26.3).</para>
///
/// <para><b>The match key (Req 26.1, 26.5).</b> <see cref="SubjectIdentifier"/> is the normalized
/// value a presented subject is matched against — a visitor name, a phone number or a vehicle number
/// depending on <see cref="SubjectType"/>. The entity config indexes <c>(CommunityId,
/// SubjectIdentifier)</c> so the gate match (run on every walk-in registration / entry) is a single
/// indexed equality lookup, not a scan. The service normalizes the identifier (trim + upper-invariant)
/// on both store and match so casing/spacing can't slip a match.</para>
///
/// <para><b>Configurable subject type / severity / status (steering: no hardcoding).</b>
/// <see cref="SubjectType"/> resolves against the editable <c>Watchlist_Subject_Type</c> list
/// (person / vehicle); <see cref="Severity"/> against <c>Watchlist_Severity</c>; <see cref="Status"/>
/// against <c>Watchlist_Status</c> (active / cleared). Only an <c>active</c> entry matches at the
/// gate — a <c>cleared</c> one is retained for the record but no longer flags (Req 26.5). The
/// constant classes below name the seeded codes so the service avoids scattered literals.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token (the clear-once guard), audit columns.</para>
/// </summary>
public class WatchlistEntry : TenantOwnedEntity
{
    /// <summary>
    /// Configurable subject-type code resolved against the <c>Watchlist_Subject_Type</c> master-data
    /// list (person / vehicle, Req 26.1). Stored as the stable code, never a frozen enum.
    /// </summary>
    public string SubjectType { get; set; } = WatchlistSubjectTypes.Person;

    /// <summary>
    /// The normalized value a presented subject is matched against (Req 26.1): a visitor name, phone
    /// or vehicle number. Stored trimmed + upper-invariant so the gate match is case/space-insensitive.
    /// Required; the indexed match key.
    /// </summary>
    public string SubjectIdentifier { get; set; } = string.Empty;

    /// <summary>Why the subject is watchlisted (Req 26.1). Required free text.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Configurable severity code resolved against the <c>Watchlist_Severity</c> master-data list
    /// (low / medium / high / critical). Stored as the stable code, never a frozen enum.
    /// </summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>
    /// Configurable status code resolved against the <c>Watchlist_Status</c> master-data list
    /// (active / cleared, Req 26.5). Starts <c>active</c> (flags at the gate); a clear sets
    /// <c>cleared</c> (retained but no longer flags). Stored as the stable code, never a frozen enum.
    /// </summary>
    public string Status { get; set; } = WatchlistStatuses.Active;

    /// <summary>Whether the entry currently flags at the gate (active, not cleared). Computed, not stored.</summary>
    public bool IsActive =>
        string.Equals(Status, WatchlistStatuses.Active, StringComparison.Ordinal);
}

/// <summary>
/// Well-known default <c>Watchlist_Subject_Type</c> codes (Req 26.1). Seeded master-data codes kept
/// as constants so the service sets/compares without a magic string. <b>Data</b>, not a behavioural
/// enum — the list is editable; this only names the known codes.
/// </summary>
public static class WatchlistSubjectTypes
{
    /// <summary>A watchlisted person (matched by name/phone).</summary>
    public const string Person = "person";

    /// <summary>A watchlisted vehicle (matched by vehicle/registration number).</summary>
    public const string Vehicle = "vehicle";
}

/// <summary>
/// Well-known default <c>Watchlist_Status</c> codes (Req 26.5). Seeded master-data codes kept as
/// constants. <b>Data</b>, not a behavioural enum — the list is editable.
/// </summary>
public static class WatchlistStatuses
{
    /// <summary>Initial status on add (Req 26.1): flags any matching subject at the gate (Req 26.5).</summary>
    public const string Active = "active";

    /// <summary>Cleared by staff (Req 26.5): retained for the record but no longer flags.</summary>
    public const string Cleared = "cleared";
}
