using UrbanNest.Application.Querying;
using UrbanNest.Contracts.Envelope;

namespace UrbanNest.Application.Gate;

/// <summary>
/// Gate_Service (Task 16.1, Req 23.1–23.4, 24.1–24.5): pre-approved visit passes with QR/OTP and
/// walk-in visitor registration + resident approval. The categories/recurring/entry-exit, watchlist/
/// shifts/incidents/SOS and HTTP controllers are Tasks 16.2–16.4.
///
/// <para><b>Pre-approved pass (Req 23.1-23.4, steering: security first).</b> A resident creates a
/// pass for their OWN unit (self-scope / Create). The service generates an opaque, community-unique
/// QR token and a short OTP (only the OTP's salted hash is stored; the plaintext is returned once).
/// Gate verification by QR/OTP accepts a pass that is active, within its validity window and has
/// uses remaining — incrementing the use count under the pass <c>RowVersion</c> so two concurrent
/// scans can never over-use a single-use pass (Req 23.3) — and rejects an expired / exhausted /
/// revoked / invalid pass with the reason (Req 23.4).</para>
///
/// <para><b>Walk-in + approval gate (Req 24.1-24.5).</b> A guard registers a walk-in (category/host
/// unit/vehicle/photo); it starts <c>pending</c> and the host unit's residents are notified through
/// Notification_Service in the SAME transaction (Req 24.2). While pending, entry is withheld
/// (Req 24.5) — a non-approved walk-in is not admittable and cannot generate an entry event
/// (Task 16.2). A resident with the Approve permission approves (recording approver + timestamp,
/// Req 24.3) or rejects (Req 24.4) a pending walk-in for their own unit; the decision records
/// exactly once under the visitor <c>RowVersion</c>.</para>
///
/// <para><b>Tenant + self scope.</b> The host unit must be in the caller's community
/// (same-community validation); VisitPass/Visitor are <c>IUnitScoped</c>, so a resident principal
/// only sees passes/walk-ins for their own unit via the DbContext self-scope filter. All mutations
/// are audited; reads use <c>AsNoTracking</c>.</para>
/// </summary>
public interface IGateService
{
    /// <summary>
    /// Create a pre-approved visit pass for the resident's OWN unit (Req 23.1). Requires the
    /// <c>Create</c> permission on <c>VisitPass</c> + an authorized/same-community host unit;
    /// generates the opaque QR token + OTP and the validity window (service defaults apply when
    /// omitted); status starts <c>active</c>. Returns the created pass plus the one-time plaintext
    /// OTP. A QR-token collision within the community → CONFLICT (regenerated/retried internally).
    /// </summary>
    Task<CreateVisitPassResult> CreateVisitPassAsync(CreateVisitPassRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify a visit pass at the gate by QR token or OTP (Req 23.3, 23.4). Accepts a valid pass
    /// within its window with uses remaining — incrementing the use count concurrency-safely and
    /// marking it <c>used</c> when exhausted — and returns the pass; rejects an expired / exhausted /
    /// revoked / invalid pass with the reason (never leaking which specific pass). Requires the gate
    /// verification grant (<c>Create</c> on <c>Visitor</c>, the guard-facing gate operation).
    /// </summary>
    Task<VerifyVisitPassResult> VerifyVisitPassAsync(VerifyVisitPassRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Register a walk-in visitor at the gate (Req 24.1). Requires the <c>Create</c> permission on
    /// <c>Visitor</c> + an authorized/same-community host unit; validates the category against the
    /// <c>Visitor_Category</c> list; stores the visitor <c>pending</c> (entry withheld, Req 24.5) and
    /// notifies the host unit's residents through Notification_Service in the same transaction
    /// (Req 24.2). Returns the created walk-in.
    /// </summary>
    Task<VisitorDto> RegisterWalkInAsync(RegisterWalkInRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approve a pending walk-in for the resident's own unit (Req 24.3). Requires the <c>Approve</c>
    /// permission on <c>Visitor</c> + an authorized community; sets the status <c>approved</c> and
    /// records the approving resident + timestamp. An approve against a non-pending walk-in →
    /// CONFLICT; the decision records exactly once under the visitor <c>RowVersion</c>.
    /// </summary>
    Task<VisitorDto> ApproveWalkInAsync(Guid visitorId, Guid approvingResidentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reject a pending walk-in for the resident's own unit (Req 24.4). Requires the <c>Approve</c>
    /// permission on <c>Visitor</c> + an authorized community; sets the status <c>rejected</c> (not
    /// admittable — no entry event permitted) and records the deciding resident + timestamp. A reject
    /// against a non-pending walk-in → CONFLICT.
    /// </summary>
    Task<VisitorDto> RejectWalkInAsync(Guid visitorId, Guid rejectingResidentId, RejectWalkInRequest request, CancellationToken cancellationToken = default);

    // ---------------------------------------------------------------------------------------------
    // Task 16.2 — recurring visitors (Req 25.2) + entry/exit capture (Req 25.3, 25.4)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Define a recurring visitor for the resident's OWN unit (Req 25.2). Requires the <c>Create</c>
    /// permission on <c>RecurringVisitor</c> + an authorized/same-community host unit; validates the
    /// category against the <c>Visitor_Category</c> list and the schedule (days-of-week bitmask &gt; 0,
    /// a valid date range, an optional well-formed daily window); stores it <c>active</c> so matching
    /// dates authorize entry without a new approval. Returns the created recurring visitor. Audited.
    /// </summary>
    Task<RecurringVisitorDto> CreateRecurringVisitorAsync(CreateRecurringVisitorRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke a recurring visitor for the resident's own unit (Req 25.2). Requires the <c>Create</c>
    /// permission on <c>RecurringVisitor</c> + an authorized community; sets the status <c>revoked</c>
    /// so it no longer authorizes any date. A revoke of an already-revoked authorization → CONFLICT;
    /// the transition records exactly once under the recurring-visitor <c>RowVersion</c>. Audited.
    /// </summary>
    Task<RecurringVisitorDto> RevokeRecurringVisitorAsync(Guid recurringVisitorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a recurring visitor is authorized to enter at the given instant (Req 25.2): active,
    /// within its valid date range, on a matching day-of-week and (when configured) inside its daily
    /// time window. Tenant-scoped <c>AsNoTracking</c> read; a missing/foreign-tenant id returns false.
    /// </summary>
    Task<bool> IsRecurringAuthorizedAsync(Guid recurringVisitorId, DateTime whenUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Record a visitor's ENTRY at a gate (Req 25.3). Requires the <c>Create</c> permission on
    /// <c>EntryExitEvent</c>. The request names exactly one admitting authorization (walk-in / pass /
    /// recurring); the service ENFORCES the 16.1 admittability rule — an approved walk-in, a pass that
    /// is active/in-window/with uses remaining, or a recurring visitor authorized for now — and
    /// REJECTS a non-admittable one (pending/rejected walk-in, expired/used/revoked pass, out-of-
    /// schedule/revoked recurring) with a validation error (no entry event created, Req 24.4). Stamps
    /// the entry timestamp. Returns the created open event. Audited.
    /// </summary>
    Task<EntryExitEventDto> RecordEntryAsync(RecordEntryRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Record a visitor's EXIT at a gate (Req 25.3). Requires the <c>Create</c> permission on
    /// <c>EntryExitEvent</c>. Matches the SAME named authorization to its matching OPEN event
    /// (ExitTsUtc == null) via a single indexed query and stamps the exit timestamp; an exit with no
    /// open entry is a validation error. The exit stamp is concurrency-safe under the event
    /// <c>RowVersion</c> so a double-exit can't both win. Returns the closed event. Audited.
    /// </summary>
    Task<EntryExitEventDto> RecordExitAsync(RecordExitRequest request, CancellationToken cancellationToken = default);

    // ---------------------------------------------------------------------------------------------
    // Task 16.4 — paginated list/read surface over the gate entities (Req 23.5, 24.2, 25.4, 25.5)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Paginated/filter/sort list of visit passes (Req 23.5), tenant-scoped (and resident self-scoped
    /// for a resident principal — only their own unit's passes) by the global query filter. Allow-list:
    /// filter/sort by status, hostUnitId, validUntilUtc; search by visitor name. OTP is never exposed.
    /// </summary>
    Task<PagedData<VisitPassDto>> ListVisitPassesAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>Fetch one visit pass by id, tenant/self-scoped; unknown/foreign id → null (Req 23.5).</summary>
    Task<VisitPassDto?> GetVisitPassAsync(Guid visitPassId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated/filter/sort list of walk-in visitors (the pending-approval queue, Req 24.2),
    /// tenant/self-scoped. Allow-list: filter/sort by status, category, hostUnitId; search by name —
    /// so "pending walk-ins for my unit" rides the status filter. 
    /// </summary>
    Task<PagedData<VisitorDto>> ListVisitorsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>Fetch one walk-in visitor by id, tenant/self-scoped; unknown/foreign id → null (Req 24.2).</summary>
    Task<VisitorDto?> GetVisitorAsync(Guid visitorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated/filter/sort list of entry/exit events (the entry/exit log, Req 25.4, 25.5),
    /// tenant-scoped. Allow-list: sort by entryTsUtc (default, newest first), exitTsUtc; filter by
    /// category, open (exit null), destinationUnitId, visitorId; search by visitor name — so the
    /// chronological log and "currently on-site" rides the (CommunityId, EntryTsUtc) index.
    /// </summary>
    Task<PagedData<EntryExitEventDto>> ListEntryExitEventsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated/filter/sort list of recurring visitors (Req 25.2), tenant/self-scoped. Allow-list:
    /// filter/sort by status, category, hostUnitId; search by name.
    /// </summary>
    Task<PagedData<RecurringVisitorDto>> ListRecurringVisitorsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>Fetch one recurring visitor by id, tenant/self-scoped; unknown/foreign id → null (Req 25.2).</summary>
    Task<RecurringVisitorDto?> GetRecurringVisitorAsync(Guid recurringVisitorId, CancellationToken cancellationToken = default);
}
