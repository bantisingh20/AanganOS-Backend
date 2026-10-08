using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Gate;

/// <summary>
/// A walk-in visitor a guard registers at the gate (Req 24.1), pending a resident's approval before
/// entry is authorized (Req 24.3, 24.4, 24.5). Carries its owning
/// <see cref="TenantOwnedEntity.CommunityId"/> and the <see cref="HostUnitId"/> the visitor is
/// requesting to visit.
///
/// <para><b>Resident self-view (Req 5.4).</b> Implements <see cref="IUnitScoped"/> via its non-null
/// <see cref="HostUnitId"/> surfaced as <c>UnitId</c>, so the DbContext resident self-scope filter
/// restricts a resident principal to walk-in requests for their own unit — the requests they must
/// approve/reject. No bespoke predicate in the service.</para>
///
/// <para><b>Configurable category (Req 24.1, 25.1, steering: no hardcoding).</b>
/// <see cref="Category"/> is a stable code validated at runtime against the editable
/// <c>Visitor_Category</c> list (guest / cab / delivery / service_provider / contractor) — not a
/// frozen enum. The full category behaviour (recurring, entry/exit) is Task 16.2; this task defines
/// the field and validates it against the list.</para>
///
/// <para><b>Approval gate (Req 24.3, 24.4, 24.5).</b> <see cref="Status"/> starts <c>pending</c>:
/// while pending, entry is withheld (Req 24.5) and <see cref="IsAdmittable"/> is false — the rule
/// a later entry/exit event (Task 16.2) honours so a non-approved walk-in cannot generate an entry.
/// A resident with the Approve permission moves it to <c>approved</c> (recording
/// <see cref="ApprovedByResidentId"/> + <see cref="ApprovalDecisionAtUtc"/>, Req 24.3) or
/// <c>rejected</c> (Req 24.4). Status is a configurable <c>Visitor_Status</c> code, not an enum.</para>
///
/// <para><b>Photo evidence (Req 24.1).</b> <see cref="PhotoFileId"/> references a File_Service
/// <c>StoredFile</c> (captured at the gate), not inline bytes — the opaque reference pattern.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token (the approve-once guard, Req 24.3), audit columns.</para>
/// </summary>
public class Visitor : TenantOwnedEntity, IUnitScoped
{
    /// <summary>
    /// The host unit the visitor is requesting (Req 24.1). Non-null in practice; typed nullable to
    /// satisfy <see cref="IUnitScoped"/>, which the resident self-scope filter keys on. This IS the
    /// host unit — see <see cref="HostUnitId"/>.
    /// </summary>
    public Guid? UnitId { get; set; }

    /// <summary>The host unit being visited (Req 24.1). Alias over <see cref="UnitId"/> for intent.</summary>
    public Guid HostUnitId
    {
        get => UnitId ?? Guid.Empty;
        set => UnitId = value;
    }

    /// <summary>The visitor's name (Req 24.1). Required.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional visitor phone (Req 24.1).</summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Configurable visitor-category code resolved against the <c>Visitor_Category</c> master-data
    /// list (guest / cab / delivery / service_provider / contractor, Req 24.1, 25.1). Stored as the
    /// stable code, never a frozen enum.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Optional File_Service reference to the captured gate photo (Req 24.1).</summary>
    public Guid? PhotoFileId { get; set; }

    /// <summary>Optional captured vehicle number (Req 24.1).</summary>
    public string? VehicleNumber { get; set; }

    /// <summary>Optional captured gate location/identifier (Req 24.1).</summary>
    public string? GateLocation { get; set; }

    /// <summary>
    /// Optional ID-proof type code resolved against the editable <c>ID_Proof_Type</c> master-data
    /// list (aadhaar / pan / driving_license / voter_id / passport). Captured for future reference;
    /// stored as the stable code, never a frozen enum.
    /// </summary>
    public string? IdProofType { get; set; }

    /// <summary>Optional captured ID-proof number (e.g. last digits of an Aadhaar / license).</summary>
    public string? IdProofNumber { get; set; }

    /// <summary>Optional free-text purpose of the visit (e.g. "deliver parcel", "AC repair").</summary>
    public string? Purpose { get; set; }

    /// <summary>Optional free-text person the visitor is here to meet (beyond the host unit link).</summary>
    public string? WhomToMeet { get; set; }

    /// <summary>Optional expected visit duration in minutes, captured at the gate for reference.</summary>
    public int? ExpectedDurationMinutes { get; set; }

    /// <summary>
    /// Configurable status code resolved against the <c>Visitor_Status</c> master-data list
    /// (pending / approved / rejected / entered / exited, Req 24.3, 24.4). Starts <c>pending</c>.
    /// Stored as the stable code, never a frozen enum. <c>entered</c>/<c>exited</c> are set by the
    /// Task 16.2 entry/exit workflow — defined here for completeness.
    /// </summary>
    public string Status { get; set; } = VisitorStatuses.Pending;

    /// <summary>The resident who approved/rejected the walk-in (Req 24.3). Null while pending.</summary>
    public Guid? ApprovedByResidentId { get; set; }

    /// <summary>When the approval/rejection decision was recorded (Req 24.3). Null while pending.</summary>
    public DateTime? ApprovalDecisionAtUtc { get; set; }

    /// <summary>
    /// Whether this walk-in may be admitted (Req 24.4, 24.5). Only an <c>approved</c> walk-in is
    /// admittable; a <c>pending</c> or <c>rejected</c> one is not — the rule the Task 16.2 entry/exit
    /// workflow enforces so a non-approved visitor cannot generate an entry event. Computed, not
    /// stored.
    /// </summary>
    public bool IsAdmittable =>
        string.Equals(Status, VisitorStatuses.Approved, StringComparison.Ordinal);
}

/// <summary>
/// Well-known default <c>Visitor_Status</c> codes (Req 24.3, 24.4). Seeded master-data codes kept as
/// constants so the service sets/compares status without a magic string. <b>Data</b>, not a
/// behavioural enum — the status list is editable; this is only a convenience for the known codes.
/// </summary>
public static class VisitorStatuses
{
    /// <summary>Initial status on walk-in registration (Req 24.1): entry withheld until approval (Req 24.5).</summary>
    public const string Pending = "pending";

    /// <summary>A resident approved the walk-in (Req 24.3): admittable.</summary>
    public const string Approved = "approved";

    /// <summary>A resident rejected the walk-in (Req 24.4): not admittable; no entry event permitted.</summary>
    public const string Rejected = "rejected";

    /// <summary>The visitor has entered (set by the Task 16.2 entry workflow).</summary>
    public const string Entered = "entered";

    /// <summary>The visitor has exited (set by the Task 16.2 exit workflow).</summary>
    public const string Exited = "exited";
}
