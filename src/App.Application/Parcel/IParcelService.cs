namespace App.Application.Parcel;

/// <summary>
/// Parcel_Service (Task 18.1, Req 28.1–28.4, 29.3): parcel registration and the configurable status
/// state machine. Custody-location assignment (Task 18.2), authorized collector + verified handover
/// (Task 18.3), the append-only custody timeline + SLA/lost/damaged/returned flows (Task 18.4) and
/// the HTTP controllers (Task 18.5) extend this same seam in later tasks.
///
/// <para><b>Registration (Req 28.1, 28.4, 29.3).</b> A guard with the <c>Create</c> permission on
/// <c>Parcel</c> registers a received parcel for a recipient unit in their community: the service
/// validates the category/condition against the configurable <c>Parcel_Category</c>/
/// <c>Parcel_Condition</c> lists, stamps the received timestamp, sets the status to <c>received</c>,
/// records the optional photo/condition-evidence File_Service references (Req 28.4) and flags the
/// parcel priority when its category is a configurable priority category (medicine/perishable/urgent,
/// Req 29.3). Audited.</para>
///
/// <para><b>Status state machine (Req 28.2, 28.3).</b> A status change is permitted only when the
/// <c>from-&gt;to</c> pair is present in the editable <c>Parcel_Status_Transition</c> allow-list for
/// the community — an illegal transition is rejected with a validation error (Req 28.3), with no
/// hardcoded transition table. The transition records exactly once under the parcel <c>RowVersion</c>
/// (a concurrency loser → CONFLICT). Audited.</para>
///
/// <para><b>Tenant + self scope.</b> The recipient unit must be in the caller's community
/// (same-community validation); <c>Parcel</c> is <c>IUnitScoped</c>, so a resident principal only
/// sees parcels addressed to their own unit via the DbContext self-scope filter. Reads use
/// <c>AsNoTracking</c>; mutations are audited.</para>
/// </summary>
public interface IParcelService
{
    /// <summary>
    /// Register a received parcel at the gate/reception (Req 28.1). Requires the <c>Create</c>
    /// permission on <c>Parcel</c> + an authorized/same-community recipient unit; validates the
    /// category/condition against the configurable lists; stamps the received timestamp; sets the
    /// status to <c>received</c>; records optional photo/condition-evidence references (Req 28.4);
    /// and flags priority when the category is a configurable priority category (Req 29.3). Returns
    /// the created parcel. Audited.
    /// </summary>
    Task<ParcelDto> RegisterParcelAsync(RegisterParcelRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Change a parcel's status through the configurable state machine (Req 28.2, 28.3). Requires the
    /// <c>Edit</c> permission on <c>Parcel</c> + an authorized community; validates the target status
    /// against <c>Parcel_Status</c> and the <c>from-&gt;to</c> transition against the editable
    /// <c>Parcel_Status_Transition</c> allow-list — an illegal transition → VALIDATION_ERROR, nothing
    /// mutated (Req 28.3). The transition records exactly once under the parcel <c>RowVersion</c>
    /// (a concurrency loser → CONFLICT). Returns the updated parcel. Audited.
    /// </summary>
    Task<ParcelDto> ChangeParcelStatusAsync(Guid parcelId, ChangeParcelStatusRequest request, CancellationToken cancellationToken = default);

    /// <summary>Fetch one parcel by id, tenant/self-scoped; unknown/foreign id → null (Req 28.5).</summary>
    Task<ParcelDto?> GetParcelAsync(Guid parcelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated/filtered/sorted/searchable parcel list (Req 28.5). Tenant + resident-self-scoped by
    /// the global query filter (a resident only sees parcels addressed to their own unit); the
    /// allow-listed <c>ListQueryConfiguration</c> fixes which fields a client may sort/filter/search
    /// on, so no arbitrary client LINQ reaches the query. <c>AsNoTracking</c> projection, page size
    /// clamped by the shared policy — mirrors the other module list endpoints.
    /// </summary>
    Task<App.Contracts.Envelope.PagedData<ParcelDto>> ListParcelsAsync(
        App.Application.Querying.ListQueryParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a custody location — a secure bin/shelf parcels are held in (Task 18.2, Req 29.1).
    /// Requires the <c>Create</c> permission on <c>CustodyLocation</c> + an authorized community; the
    /// code is unique within the community (a duplicate → CONFLICT via the DB unique index, no
    /// read-then-write pre-check). Returns the created location. Audited.
    /// </summary>
    Task<CustodyLocationDto> CreateCustodyLocationAsync(CreateCustodyLocationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Move a parcel into custody and assign it a custody location (Task 18.2, Req 29.1, 29.2, 29.4).
    /// Requires the <c>Edit</c> permission on <c>Parcel</c> + an authorized community; validates the
    /// custody location is in the parcel's community and that the configurable
    /// <c>current-&gt;in_custody</c> transition is allowed (Req 28.3); records the assignment on the
    /// parcel; and notifies the recipient unit's residents of the received/held parcel (Req 29.2) —
    /// with priority handling (the priority template + full channel set) when the parcel is flagged
    /// priority (Req 29.3). The notification rides the same transaction via the outbox. Exactly one
    /// parcel may actively hold a given custody location: a racing duplicate assignment of the same
    /// location is rejected as a CONFLICT via the filtered unique index (Req 29.4), and a concurrent
    /// transition losing the parcel <c>RowVersion</c> check is likewise a CONFLICT. Returns the
    /// updated parcel. Audited.
    /// </summary>
    Task<ParcelDto> AssignCustodyLocationAsync(Guid parcelId, AssignCustodyLocationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// A resident authorizes a collector to pick up a parcel addressed to the resident's OWN unit
    /// (Task 18.3, Req 30.1). Requires the <c>Create</c> permission on <c>ParcelAuthorization</c> +
    /// an authorized community; the parcel must be in the caller's community (and, for a resident
    /// principal, their own unit via the self-scope filter). The service generates a one-time OTP
    /// (salted-hash at rest, like the visit-pass OTP) and an opaque, community-unique QR token for
    /// the collector, resolves the verification method against the configurable
    /// <c>Parcel_Verification_Method</c> list (falling back to the community default), requires an
    /// expected ID reference when the method includes ID verification, and stamps the validity window
    /// from Config_Service. Returns the authorization plus the one-time OTP + QR token (returned once;
    /// never persisted in plaintext). Audited.
    /// </summary>
    Task<AuthorizeCollectorResult> AuthorizeCollectorAsync(AuthorizeCollectorRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// A guard performs a verified, idempotent handover of a parcel to an authorized collector
    /// (Task 18.3, Req 30.2–30.5). Requires the <c>Edit</c> permission on <c>Parcel</c> + an
    /// authorized community. The service verifies the collector by the authorization's configured
    /// method — OTP (by salted-hash compare), QR (opaque token), ID (reference match) or a
    /// combination (all required). A FAILED verification is rejected with a VALIDATION_ERROR and the
    /// parcel is LEFT in its current custody status — no handover row, no state change (Req 30.3). On
    /// success it records the collector name/relationship/verification method/proof reference/handling
    /// guard/timestamp and transitions the parcel to <c>handed_over</c> through the configurable
    /// state machine (Req 30.4). The whole operation is wrapped by the request's
    /// <c>Idempotency-Key</c> so a retry carrying the same key performs the handover exactly once and
    /// replays the first result (Req 30.5). Returns the recorded handover. Audited (success and the
    /// failed attempt).
    /// </summary>
    Task<ParcelHandoverDto> PerformHandoverAsync(PerformHandoverRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Record a parcel as lost or damaged (Task 18.4, Req 31.3). Requires the <c>Edit</c> permission
    /// on <c>Parcel</c> + an authorized community. The service transitions the parcel to the
    /// <c>lost_damaged</c> status through the configurable state machine (an illegal transition from
    /// the current status → VALIDATION_ERROR, nothing mutated), appends an append-only
    /// custody-timeline entry, and creates a LINKED <c>Incident</c> (reusing the SecurityOps incident
    /// creation path — configurable category/severity, escalation + responder notification) recording
    /// the circumstances and any evidence. The transition records exactly once under the parcel
    /// <c>RowVersion</c> (a concurrency loser → CONFLICT). Returns the updated parcel. Audited.
    /// </summary>
    Task<ParcelDto> RecordLostDamagedAsync(Guid parcelId, RecordLostDamagedRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Record a parcel as returned to the delivery provider (Task 18.4, Req 31.4). Requires the
    /// <c>Edit</c> permission on <c>Parcel</c> + an authorized community. The service transitions the
    /// parcel to the <c>returned</c> status through the configurable state machine (an illegal
    /// transition → VALIDATION_ERROR, nothing mutated), clears any active custody-location assignment
    /// and appends an append-only custody-timeline return entry. The transition records exactly once
    /// under the parcel <c>RowVersion</c> (a concurrency loser → CONFLICT). Returns the updated
    /// parcel. Audited.
    /// </summary>
    Task<ParcelDto> RecordReturnedAsync(Guid parcelId, RecordReturnedRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve a parcel's custody timeline in chronological order (Task 18.4, Req 31.5). Requires the
    /// <c>View</c> permission on <c>CustodyTimelineEntry</c> + an authorized community; the entries are
    /// tenant-scoped by the global filter (a foreign-tenant parcel's timeline is never returned). An
    /// unknown/foreign parcel id yields an empty list. <c>AsNoTracking</c> ordered indexed read, no N+1.
    /// </summary>
    Task<IReadOnlyList<CustodyTimelineEntryDto>> GetCustodyTimelineAsync(Guid parcelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Background custody-SLA sweep (Task 18.4, Req 31.2): find parcels that have sat in custody past
    /// their configurable Parcel_SLA and have not yet been escalated, escalate each (append an
    /// append-only <c>sla_escalated</c> timeline entry and stamp the parcel so it is escalated at most
    /// once), and enqueue the SLA-exceeded notification to the recipient residents AND the community
    /// admin via Notification_Service (push + email, Req 64.2). The sweep is BOUNDED — it processes at
    /// most <paramref name="maxParcels"/> overdue parcels per call — and is invoked by the hosted
    /// custody-SLA worker (which runs under a cross-tenant/super-admin context, like the outbox
    /// worker). Side-effects ride the transactional outbox and commit with the escalation; the
    /// per-parcel escalation stamp + the parcel <c>RowVersion</c> keep it idempotent and
    /// concurrency-safe (a parcel is never double-escalated, and a losing concurrent writer is skipped
    /// rather than re-notifying). Returns the number of parcels escalated. Audited per parcel.
    /// </summary>
    Task<int> EscalateOverdueCustodyParcelsAsync(int maxParcels, CancellationToken cancellationToken = default);
}
