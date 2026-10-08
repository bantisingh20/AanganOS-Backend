namespace CommunityOS.Application.Parcel;

/// <summary>Read shape for a <c>Parcel</c> returned to callers (Req 28.1).</summary>
/// <param name="Id">The parcel id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="RecipientUnitId">The recipient unit the parcel is addressed to.</param>
/// <param name="TrackingNumber">The courier tracking/reference number.</param>
/// <param name="Provider">The delivery provider / courier.</param>
/// <param name="Category">Configurable parcel-category code (Req 28.1).</param>
/// <param name="Status">Configurable parcel-status code (Req 28.2).</param>
/// <param name="Condition">Configurable recorded package-condition code (Req 28.1).</param>
/// <param name="Receiver">Who received the parcel at the gate/reception, or null.</param>
/// <param name="ReceivedLocation">The gate/reception location, or null.</param>
/// <param name="ReceivedAtUtc">When the parcel was received (Req 28.1).</param>
/// <param name="PhotoFileId">Optional File_Service reference to the captured parcel photo (Req 28.4).</param>
/// <param name="ConditionEvidenceFileId">Optional File_Service reference to captured condition evidence (Req 28.4).</param>
/// <param name="IsPriority">Whether the parcel is flagged priority (medicine/perishable/urgent, Req 29.3).</param>
/// <param name="CustodyLocationId">The custody location the parcel is currently held in while in custody, or null (Req 29.1).</param>
public sealed record ParcelDto(
    Guid Id,
    Guid CommunityId,
    Guid RecipientUnitId,
    string TrackingNumber,
    string Provider,
    string Category,
    string Status,
    string Condition,
    string? Receiver,
    string? ReceivedLocation,
    DateTime ReceivedAtUtc,
    Guid? PhotoFileId,
    Guid? ConditionEvidenceFileId,
    bool IsPriority,
    Guid? CustodyLocationId);

/// <summary>Read shape for a <c>CustodyLocation</c> — a secure bin/location parcels are held in (Req 29.1).</summary>
/// <param name="Id">The custody location id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="Code">Human-readable location code/label, unique within the community.</param>
public sealed record CustodyLocationDto(
    Guid Id,
    Guid CommunityId,
    string Code);

/// <summary>
/// Create a custody location — a secure bin/shelf parcels are held in (Req 29.1). The code must be
/// unique within the community; a duplicate is rejected as a CONFLICT via the DB unique index.
/// </summary>
/// <param name="CommunityId">The owning community (must be authorized for the caller).</param>
/// <param name="Code">Human-readable location code/label. Required.</param>
public sealed record CreateCustodyLocationRequest(
    Guid CommunityId,
    string Code);

/// <summary>
/// Move a parcel into custody and assign it a custody location (Req 29.1, 29.4). The service runs the
/// configurable <c>-&gt;in_custody</c> transition, records the assignment on the parcel and notifies
/// the recipient unit's residents (Req 29.2) — with priority handling when the parcel is flagged
/// priority (Req 29.3). Only one parcel may actively hold a given custody location: a racing
/// duplicate assignment of the same location is rejected as a CONFLICT (Req 29.4).
/// </summary>
/// <param name="CustodyLocationId">The custody location to hold the parcel in. Required; must be in the parcel's community.</param>
public sealed record AssignCustodyLocationRequest(
    Guid CustodyLocationId);

/// <summary>
/// Register a received parcel at the gate/reception (Req 28.1). The guard supplies the tracking
/// number, provider, recipient unit, category, recorded condition and optional receiver/location and
/// photo/condition-evidence File_Service references. The service anchors the parcel in the recipient
/// unit's community, validates the category/condition against the configurable lists, stamps the
/// received timestamp, sets the status to <c>received</c> and flags priority when the category is a
/// configurable priority category (Req 29.3).
/// </summary>
/// <param name="RecipientUnitId">The recipient unit (must be in the caller's community). Required.</param>
/// <param name="TrackingNumber">The courier tracking/reference number. Required.</param>
/// <param name="Provider">The delivery provider / courier. Required.</param>
/// <param name="Category">Parcel-category code from the Parcel_Category list. Required.</param>
/// <param name="Condition">Recorded package-condition code from the Parcel_Condition list. Required.</param>
/// <param name="Receiver">Optional who received the parcel at the gate/reception.</param>
/// <param name="ReceivedLocation">Optional gate/reception location.</param>
/// <param name="PhotoFileId">Optional File_Service reference to a captured parcel photo (Req 28.4).</param>
/// <param name="ConditionEvidenceFileId">Optional File_Service reference to captured condition evidence (Req 28.4).</param>
public sealed record RegisterParcelRequest(
    Guid RecipientUnitId,
    string TrackingNumber,
    string Provider,
    string Category,
    string Condition,
    string? Receiver,
    string? ReceivedLocation,
    Guid? PhotoFileId,
    Guid? ConditionEvidenceFileId);

/// <summary>
/// Change a parcel's status (Req 28.2, 28.3). The target status must be a valid <c>Parcel_Status</c>
/// code and the transition from the parcel's current status to <paramref name="ToStatus"/> must be
/// present in the configurable <c>Parcel_Status_Transition</c> allow-list for the community;
/// otherwise the transition is rejected with a validation error (Req 28.3). The optional
/// <paramref name="Reason"/> is recorded in the audit trail.
/// </summary>
/// <param name="ToStatus">The target parcel-status code. Required.</param>
/// <param name="Reason">Optional free-text reason recorded in the audit trail.</param>
public sealed record ChangeParcelStatusRequest(
    string ToStatus,
    string? Reason);

/// <summary>
/// Read shape for a <c>ParcelAuthorization</c> — a resident's authorization naming a collector
/// permitted to pick up a parcel (Req 30.1). The OTP/QR secrets are NOT on this read shape; the
/// one-time OTP + QR token are returned only on <see cref="AuthorizeCollectorResult"/> at create.
/// </summary>
/// <param name="Id">The authorization id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="ParcelId">The parcel this authorization permits a collector to pick up.</param>
/// <param name="RecipientUnitId">The recipient unit the authorized parcel is addressed to.</param>
/// <param name="CollectorName">The authorized collector's name (Req 30.1, 30.4).</param>
/// <param name="CollectorRelationship">The collector's relationship to the resident, or null (Req 30.4).</param>
/// <param name="VerificationMethod">Configurable verification-method code the handover enforces (otp/qr/id/combination, Req 30.2).</param>
/// <param name="ExpiresAtUtc">When the authorization (and its OTP/QR) stops being valid (Req 30.1).</param>
/// <param name="IsUsed">Whether a completed handover has already consumed this authorization (Req 30.4).</param>
public sealed record ParcelAuthorizationDto(
    Guid Id,
    Guid CommunityId,
    Guid ParcelId,
    Guid RecipientUnitId,
    string CollectorName,
    string? CollectorRelationship,
    string VerificationMethod,
    DateTime ExpiresAtUtc,
    bool IsUsed);

/// <summary>
/// Read shape for a completed <c>ParcelHandover</c> (Req 30.4). Written only on a successful
/// verification; its existence proves the parcel reached <c>handed_over</c> through a verified
/// handover. This is also the result the idempotency wrapper snapshots and replays on a retry
/// carrying the same key (Req 30.5), so it is a plain serializable record.
/// </summary>
/// <param name="Id">The handover id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="ParcelId">The parcel that was handed over.</param>
/// <param name="ParcelAuthorizationId">The authorization the handover was verified against, or null.</param>
/// <param name="CollectorName">The collector's name as recorded at handover (Req 30.4).</param>
/// <param name="CollectorRelationship">The collector's relationship, or null (Req 30.4).</param>
/// <param name="VerificationMethod">The verification method actually used (Req 30.4).</param>
/// <param name="ProofFileId">File_Service reference to the captured handover proof, or null (Req 30.4).</param>
/// <param name="GuardUserId">The handling guard — the acting user who performed the handover (Req 30.4).</param>
/// <param name="HandedOverAtUtc">When the handover completed (Req 30.4).</param>
public sealed record ParcelHandoverDto(
    Guid Id,
    Guid CommunityId,
    Guid ParcelId,
    Guid? ParcelAuthorizationId,
    string CollectorName,
    string? CollectorRelationship,
    string VerificationMethod,
    Guid? ProofFileId,
    string? GuardUserId,
    DateTime HandedOverAtUtc);

/// <summary>Create result for <see cref="ParcelAuthorizationDto"/> plus the one-time collector secrets.</summary>
/// <param name="Authorization">The created authorization.</param>
/// <param name="Otp">
/// The plaintext OTP to share with the collector (Req 30.2). Returned ONCE here only — only its
/// salted hash is persisted, never the plaintext (steering: secrets).
/// </param>
/// <param name="QrToken">The opaque QR token to share with the collector (Req 30.2).</param>
public sealed record AuthorizeCollectorResult(
    ParcelAuthorizationDto Authorization,
    string Otp,
    string QrToken);

/// <summary>
/// A resident authorizes a collector to pick up a parcel addressed to the resident's OWN unit
/// (Req 30.1). The service generates a one-time OTP + an opaque QR token for the collector, resolves
/// the verification method against the configurable <c>Parcel_Verification_Method</c> list (falling
/// back to the community default) and stamps the authorization's validity window from config. An
/// <c>id</c> / <c>combination</c> method requires <paramref name="ExpectedIdReference"/>.
/// </summary>
/// <param name="ParcelId">The parcel to authorize collection of (must be addressed to the resident's own unit). Required.</param>
/// <param name="CollectorName">The authorized collector's name. Required.</param>
/// <param name="CollectorRelationship">Optional relationship of the collector to the resident (Req 30.4).</param>
/// <param name="VerificationMethod">
/// Optional verification-method code (otp/qr/id/combination). When omitted the configured community
/// default is used (Req 30.2).
/// </param>
/// <param name="ExpectedIdReference">
/// Optional expected government/photo-ID reference the <c>id</c>/<c>combination</c> methods check the
/// presented reference against. Required when the chosen method includes ID verification (Req 30.2).
/// </param>
public sealed record AuthorizeCollectorRequest(
    Guid ParcelId,
    string CollectorName,
    string? CollectorRelationship,
    string? VerificationMethod,
    string? ExpectedIdReference);

/// <summary>
/// A guard performs a verified handover of a parcel to the collector (Req 30.2, 30.4). The service
/// verifies the collector by the authorization's configured method using the presented
/// <paramref name="Otp"/> / <paramref name="QrToken"/> / <paramref name="PresentedIdReference"/>
/// (OTP by hash like the visit-pass OTP, QR by opaque token, ID by reference match; a
/// <c>combination</c> requires all). A FAILED verification is rejected with a validation error and
/// leaves the parcel in its current custody status — no handover row, no state change (Req 30.3). On
/// success it records the collector/relationship/method/proof/guard/timestamp and transitions the
/// parcel to <c>handed_over</c> through the configurable state machine (Req 30.4).
///
/// <para>The whole operation is made AT-MOST-ONCE by the <paramref name="IdempotencyKey"/>: a retry
/// carrying the same key does not perform a second handover and replays the first result (Req 30.5).
/// Task 18.5's <c>POST /parcels/{id}/handover</c> endpoint supplies this from the
/// <c>Idempotency-Key</c> header.</para>
/// </summary>
/// <param name="ParcelAuthorizationId">The authorization to verify the collector against. Required.</param>
/// <param name="Otp">The OTP presented by the collector (required for otp/combination methods).</param>
/// <param name="QrToken">The QR token presented by the collector (required for qr/combination methods).</param>
/// <param name="PresentedIdReference">The ID reference presented by the collector (required for id/combination methods).</param>
/// <param name="ProofFileId">Optional File_Service reference to the captured handover proof (Req 30.4).</param>
/// <param name="IdempotencyKey">
/// The client-supplied <c>Idempotency-Key</c> making the handover at-most-once (Req 30.5). Required.
/// </param>
public sealed record PerformHandoverRequest(
    Guid ParcelAuthorizationId,
    string? Otp,
    string? QrToken,
    string? PresentedIdReference,
    Guid? ProofFileId,
    string IdempotencyKey);

/// <summary>
/// Read shape for one append-only <c>CustodyTimelineEntry</c> — a single recorded parcel custody
/// event (Req 31.1, 31.5). Returned in chronological order by the timeline retrieval. Non-editable:
/// there is no corresponding update/create request shape (entries are system-written inside the
/// parcel operations).
/// </summary>
/// <param name="Id">The timeline entry id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="ParcelId">The parcel this entry belongs to.</param>
/// <param name="EventType">The custody event type code (registered / status_changed / custody_assigned / handed_over / lost_damaged / returned / sla_escalated).</param>
/// <param name="FromStatus">The parcel status before the event, when it is a status transition; otherwise null.</param>
/// <param name="ToStatus">The parcel status after the event, when it is a status transition; otherwise null.</param>
/// <param name="ActingUserId">The user who performed the event, or null for a system actor (the SLA sweep).</param>
/// <param name="Detail">Optional secret-free detail (custody location / method / linked incident id / SLA note).</param>
/// <param name="OccurredAtUtc">When the event occurred (Req 31.1).</param>
public sealed record CustodyTimelineEntryDto(
    Guid Id,
    Guid CommunityId,
    Guid ParcelId,
    string EventType,
    string? FromStatus,
    string? ToStatus,
    string? ActingUserId,
    string? Detail,
    DateTime OccurredAtUtc);

/// <summary>
/// Record a parcel as lost or damaged (Req 31.3). The service transitions the parcel to the
/// <c>lost_damaged</c> status through the configurable state machine, appends a custody-timeline
/// entry, and creates a LINKED <c>Incident</c> (reusing SecurityOpsService) recording the
/// circumstances and any evidence. The optional <paramref name="Category"/>/<paramref name="Severity"/>
/// override the configurable incident defaults when supplied.
/// </summary>
/// <param name="Circumstances">What happened — recorded on the incident and the timeline. Required.</param>
/// <param name="EvidenceFileId">Optional File_Service reference to captured loss/damage evidence (Req 31.3).</param>
/// <param name="Category">Optional Incident_Category code for the linked incident; defaults to the configurable fallback when omitted.</param>
/// <param name="Severity">Optional Incident_Severity code for the linked incident; defaults to the configurable fallback when omitted.</param>
public sealed record RecordLostDamagedRequest(
    string Circumstances,
    Guid? EvidenceFileId,
    string? Category,
    string? Severity);

/// <summary>
/// Record a parcel as returned to the delivery provider (Req 31.4). The service transitions the
/// parcel to the <c>returned</c> status through the configurable state machine and appends a
/// custody-timeline return entry. The optional <paramref name="Reason"/> is recorded on the timeline
/// entry and the audit trail.
/// </summary>
/// <param name="Reason">Optional free-text reason recorded on the timeline entry + audit trail.</param>
public sealed record RecordReturnedRequest(
    string? Reason);
