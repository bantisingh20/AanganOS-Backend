using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Gate;

/// <summary>
/// A pre-approved visitor invitation a resident creates for their OWN unit (Req 23.1), carrying a
/// unique QR token + OTP, a validity window and a bounded use count. Carries its owning
/// <see cref="TenantOwnedEntity.CommunityId"/> and the <see cref="HostUnitId"/> the pass admits a
/// visitor to.
///
/// <para><b>Resident self-view (Req 5.4, 23.5).</b> Implements <see cref="IUnitScoped"/> via its
/// non-null <see cref="HostUnitId"/> surfaced as <c>UnitId</c>, so the DbContext applies the
/// resident self-scope filter: a resident-self-scoped principal only sees passes for their own
/// unit — the pass they created. No bespoke predicate in the service.</para>
///
/// <para><b>Opaque QR + hashed OTP (steering: security first).</b> <see cref="QrToken"/> is an
/// opaque, high-entropy, community-unique code — the gate scans it; it reveals nothing about the
/// pass. <see cref="OtpHash"/> stores only a salted hash of the short OTP shared with the visitor
/// (the plaintext is never persisted, Req 2.5 convention), so a leaked DB row cannot replay the
/// OTP. Verification compares in constant time.</para>
///
/// <para><b>Concurrency-safe use count (Req 23.3, 23.4).</b> A verification that accepts the pass
/// increments <see cref="UseCount"/> under the inherited <c>RowVersion</c>: two concurrent scans
/// race on the row-version check and exactly one wins, so a single-use pass can never be admitted
/// twice. When <see cref="UseCount"/> reaches <see cref="MaxUses"/> the pass is marked
/// <c>used</c>.</para>
///
/// <para><b>Configurable status (steering: no hardcoding).</b> <see cref="Status"/> is a stable
/// code resolved against the editable <c>Visit_Pass_Status</c> list (active / used / expired /
/// revoked) — not a frozen enum. The <see cref="VisitPassStatuses"/> constants name the seeded
/// codes so the service avoids scattered literals; expiry is derived from the validity window
/// (Req 23.2) rather than a stored transition.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token, audit columns.</para>
/// </summary>
public class VisitPass : TenantOwnedEntity, IUnitScoped
{
    /// <summary>
    /// The unit this pass admits a visitor to (Req 23.1). Non-null in practice (a pass is always for
    /// a known host unit); typed as nullable to satisfy <see cref="IUnitScoped"/>, which the resident
    /// self-scope filter keys on. This IS the host unit — see <see cref="HostUnitId"/>.
    /// </summary>
    public Guid? UnitId { get; set; }

    /// <summary>The host unit being visited (Req 23.1). Alias over <see cref="UnitId"/> for intent.</summary>
    public Guid HostUnitId
    {
        get => UnitId ?? Guid.Empty;
        set => UnitId = value;
    }

    /// <summary>Optional inviting resident (Req 23.1). The resident who created the pass for their own unit.</summary>
    public Guid? HostResidentId { get; set; }

    /// <summary>The invited visitor's name (Req 23.1). Required.</summary>
    public string VisitorName { get; set; } = string.Empty;

    /// <summary>Optional visitor contact phone captured on the invite.</summary>
    public string? Phone { get; set; }

    /// <summary>Optional captured vehicle number.</summary>
    public string? VehicleNumber { get; set; }

    /// <summary>Optional vehicle type code resolved against the editable <c>Vehicle_Type</c> list.</summary>
    public string? VehicleType { get; set; }

    /// <summary>Optional number of people expected under this pass. Defaults to 1 when set.</summary>
    public int? NumberOfPersons { get; set; }

    /// <summary>Optional free-text person / flat the visitor is here to meet (beyond the host unit).</summary>
    public string? WhomToMeet { get; set; }

    /// <summary>Optional expected date of visit (the day the visitor is expected).</summary>
    public DateTime? VisitDateUtc { get; set; }

    /// <summary>Optional File_Service reference to a captured visitor photo.</summary>
    public Guid? PhotoFileId { get; set; }

    /// <summary>
    /// Opaque, community-unique QR code the gate scans (Req 23.1). High-entropy random string, not
    /// derived from pass data — it leaks nothing and is the indexed lookup key at the gate.
    /// </summary>
    public string QrToken { get; set; } = string.Empty;

    /// <summary>
    /// Salted hash of the short OTP shared with the visitor (Req 23.1). Only the hash is stored; the
    /// plaintext OTP is returned once on create and never persisted or logged (steering: secrets).
    /// </summary>
    public string OtpHash { get; set; } = string.Empty;

    /// <summary>Start of the validity window (Req 23.1). The pass is not valid before this time.</summary>
    public DateTime ValidFromUtc { get; set; }

    /// <summary>End of the validity window (Req 23.1, 23.2). The pass expires at this time.</summary>
    public DateTime ValidUntilUtc { get; set; }

    /// <summary>Maximum number of accepted verifications (Req 23.4). Defaults to 1 (single-use).</summary>
    public int MaxUses { get; set; } = 1;

    /// <summary>Accepted verifications so far (Req 23.3, 23.4). Incremented concurrency-safely on accept.</summary>
    public int UseCount { get; set; }

    /// <summary>
    /// Configurable status code resolved against the <c>Visit_Pass_Status</c> master-data list
    /// (active / used / expired / revoked, Req 23.4). Stored as the stable code, never a frozen enum.
    /// Starts <c>active</c>; set <c>used</c> when uses are exhausted, <c>revoked</c> when cancelled.
    /// </summary>
    public string Status { get; set; } = VisitPassStatuses.Active;
}

/// <summary>
/// Well-known default <c>Visit_Pass_Status</c> codes (Req 23.4). These are the seeded master-data
/// codes, kept as constants so the service sets/compares status without a magic string. They are
/// <b>data</b>, not a behavioural enum — the status list is editable; this is only a convenience for
/// the known codes.
/// </summary>
public static class VisitPassStatuses
{
    /// <summary>Initial status on create (Req 23.1): the pass can be verified within its window.</summary>
    public const string Active = "active";

    /// <summary>Uses exhausted (<c>UseCount</c> reached <c>MaxUses</c>, Req 23.4).</summary>
    public const string Used = "used";

    /// <summary>Past its valid-until time (Req 23.2). Derived from the window; a convenience code.</summary>
    public const string Expired = "expired";

    /// <summary>Cancelled by the host/admin before use (Req 23.4).</summary>
    public const string Revoked = "revoked";
}
