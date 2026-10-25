namespace UrbanNest.Application.Gate;

/// <summary>Read shape for a <c>VisitPass</c> returned to callers (Req 23.1). The OTP is NEVER on this
/// shape — only the one-time plaintext from <see cref="CreateVisitPassResult"/> carries it.</summary>
/// <param name="Id">The pass id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="HostUnitId">The host unit the pass admits a visitor to.</param>
/// <param name="HostResidentId">Optional inviting resident.</param>
/// <param name="VisitorName">The invited visitor's name.</param>
/// <param name="QrToken">The opaque QR code the gate scans.</param>
/// <param name="ValidFromUtc">Start of the validity window.</param>
/// <param name="ValidUntilUtc">End of the validity window (expiry, Req 23.2).</param>
/// <param name="MaxUses">Maximum accepted verifications.</param>
/// <param name="UseCount">Accepted verifications so far.</param>
/// <param name="Status">Configurable status code (active/used/expired/revoked).</param>
public sealed record VisitPassDto(
    Guid Id,
    Guid CommunityId,
    Guid HostUnitId,
    Guid? HostResidentId,
    string VisitorName,
    string? Phone,
    string? VehicleNumber,
    string? VehicleType,
    int? NumberOfPersons,
    string? WhomToMeet,
    DateTime? VisitDateUtc,
    Guid? PhotoFileId,
    string QrToken,
    DateTime ValidFromUtc,
    DateTime ValidUntilUtc,
    int MaxUses,
    int UseCount,
    string Status);

/// <summary>
/// Result of creating a visit pass (Req 23.1). Carries the created pass plus the one-time plaintext
/// <paramref name="Otp"/> — returned to the resident ONCE so they can share it with the visitor; it
/// is never persisted (only its hash is) or returned again (steering: secrets).
/// </summary>
/// <param name="Pass">The created pass.</param>
/// <param name="Otp">The plaintext OTP, returned exactly once.</param>
public sealed record CreateVisitPassResult(VisitPassDto Pass, string Otp);

/// <summary>Read shape for a <c>Visitor</c> (walk-in) returned to callers (Req 24.1).</summary>
/// <param name="Id">The visitor id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="HostUnitId">The host unit the visitor is requesting.</param>
/// <param name="Name">The visitor's name.</param>
/// <param name="Phone">Optional visitor phone.</param>
/// <param name="Category">Configurable visitor-category code (Req 24.1).</param>
/// <param name="PhotoFileId">Optional File_Service reference to the captured gate photo.</param>
/// <param name="VehicleNumber">Optional captured vehicle number.</param>
/// <param name="GateLocation">Optional captured gate location.</param>
/// <param name="IdProofType">Optional ID-proof type code (ID_Proof_Type list), captured for reference.</param>
/// <param name="IdProofNumber">Optional captured ID-proof number.</param>
/// <param name="Purpose">Optional free-text purpose of the visit.</param>
/// <param name="WhomToMeet">Optional free-text person the visitor is here to meet.</param>
/// <param name="ExpectedDurationMinutes">Optional expected visit duration in minutes.</param>
/// <param name="Status">Configurable status code (pending/approved/rejected/...).</param>
/// <param name="ApprovedByResidentId">The approving/rejecting resident, or null while pending.</param>
/// <param name="ApprovalDecisionAtUtc">When the decision was recorded, or null while pending.</param>
/// <param name="IsAdmittable">Whether the walk-in may be admitted (only when approved, Req 24.4, 24.5).</param>
public sealed record VisitorDto(
    Guid Id,
    Guid CommunityId,
    Guid HostUnitId,
    string Name,
    string? Phone,
    string Category,
    Guid? PhotoFileId,
    string? VehicleNumber,
    string? GateLocation,
    string? IdProofType,
    string? IdProofNumber,
    string? Purpose,
    string? WhomToMeet,
    int? ExpectedDurationMinutes,
    string Status,
    Guid? ApprovedByResidentId,
    DateTime? ApprovalDecisionAtUtc,
    bool IsAdmittable);

/// <summary>Outcome of a gate verification of a visit pass by QR/OTP (Req 23.3, 23.4).</summary>
/// <param name="Accepted">True when the pass is valid and within its window with uses remaining (Req 23.3).</param>
/// <param name="RejectionReason">Why the pass was rejected (expired/used/revoked/invalid), or null when accepted (Req 23.4).</param>
/// <param name="Pass">The verified pass (with the incremented use count) when accepted; null when rejected.</param>
public sealed record VerifyVisitPassResult(
    bool Accepted,
    string? RejectionReason,
    VisitPassDto? Pass);

/// <summary>
/// Create a pre-approved visit pass for the resident's OWN unit (Req 23.1). The caller supplies the
/// visitor and host unit; the service generates the opaque QR token + OTP and sets the status to
/// active. When the validity window is omitted, service defaults (configurable) apply.
/// </summary>
/// <param name="HostUnitId">The host unit (must be the resident's own unit). Required.</param>
/// <param name="HostResidentId">Optional inviting resident id to record on the pass.</param>
/// <param name="VisitorName">The invited visitor's name. Required.</param>
/// <param name="ValidFromUtc">Optional window start; defaults to now (UTC) when null.</param>
/// <param name="ValidUntilUtc">Optional window end; defaults to now + the configured validity hours when null.</param>
/// <param name="MaxUses">Optional max accepted verifications; defaults to the configured value (1) when null or &lt; 1.</param>
public sealed record CreateVisitPassRequest(
    Guid HostUnitId,
    Guid? HostResidentId,
    string VisitorName,
    DateTime? ValidFromUtc,
    DateTime? ValidUntilUtc,
    int? MaxUses,
    string? Phone = null,
    string? VehicleNumber = null,
    string? VehicleType = null,
    int? NumberOfPersons = null,
    string? WhomToMeet = null,
    DateTime? VisitDateUtc = null,
    Guid? PhotoFileId = null);

/// <summary>
/// Verify a visit pass at the gate by QR token or OTP (Req 23.3, 23.4). Exactly one of
/// <paramref name="QrToken"/> / <paramref name="Otp"/> identifies the pass; the OTP is matched
/// against the stored hash. The caller is a guard with the gate verification grant.
/// </summary>
/// <param name="QrToken">The scanned opaque QR token, or null when verifying by OTP.</param>
/// <param name="Otp">The entered OTP, or null when verifying by QR. When both are supplied the QR is used.</param>
public sealed record VerifyVisitPassRequest(
    string? QrToken,
    string? Otp);

/// <summary>
/// Register a walk-in visitor at the gate (Req 24.1). The guard captures the category/host-unit/
/// vehicle/photo/gate; the visitor starts pending and entry is withheld until a resident approves
/// (Req 24.3, 24.5). The host unit's residents are notified (Req 24.2).
/// </summary>
/// <param name="HostUnitId">The host unit the visitor is requesting. Required.</param>
/// <param name="Name">The visitor's name. Required.</param>
/// <param name="Phone">Optional visitor phone.</param>
/// <param name="Category">Visitor-category code from the Visitor_Category list. Required.</param>
/// <param name="PhotoFileId">Optional File_Service reference to a captured gate photo.</param>
/// <param name="VehicleNumber">Optional captured vehicle number.</param>
/// <param name="GateLocation">Optional captured gate location.</param>
/// <param name="IdProofType">Optional ID-proof type code validated against the ID_Proof_Type list.</param>
/// <param name="IdProofNumber">Optional captured ID-proof number.</param>
/// <param name="Purpose">Optional free-text purpose of the visit.</param>
/// <param name="WhomToMeet">Optional free-text person the visitor is here to meet.</param>
/// <param name="ExpectedDurationMinutes">Optional expected visit duration in minutes (must be &gt; 0 when supplied).</param>
public sealed record RegisterWalkInRequest(
    Guid HostUnitId,
    string Name,
    string? Phone,
    string Category,
    Guid? PhotoFileId,
    string? VehicleNumber,
    string? GateLocation,
    string? IdProofType = null,
    string? IdProofNumber = null,
    string? Purpose = null,
    string? WhomToMeet = null,
    int? ExpectedDurationMinutes = null);

/// <summary>Reject a pending walk-in visitor (Req 24.4). The reason is optional free text recorded in the audit trail.</summary>
/// <param name="Reason">Optional reason for the rejection.</param>
public sealed record RejectWalkInRequest(string? Reason);

// ---------------------------------------------------------------------------------------------
// Task 16.2 — recurring visitors (Req 25.2) and entry/exit capture (Req 25.3, 25.4)
// ---------------------------------------------------------------------------------------------

/// <summary>Read shape for a <c>RecurringVisitor</c> returned to callers (Req 25.2).</summary>
/// <param name="Id">The recurring-visitor id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="HostUnitId">The host unit the authorization is for.</param>
/// <param name="HostResidentId">The resident who defined (and may revoke) it.</param>
/// <param name="Name">The recurring visitor's name.</param>
/// <param name="Phone">Optional visitor phone.</param>
/// <param name="Category">Configurable visitor-category code (Req 25.1).</param>
/// <param name="DaysOfWeek">7-bit mask of authorized days (Sun=1 … Sat=64).</param>
/// <param name="StartDate">First valid date (inclusive).</param>
/// <param name="EndDate">Last valid date (inclusive).</param>
/// <param name="DailyStartMinutes">Optional daily-window start (minutes from midnight), or null.</param>
/// <param name="DailyEndMinutes">Optional daily-window end (minutes from midnight), or null.</param>
/// <param name="Status">Configurable status code (active/revoked).</param>
public sealed record RecurringVisitorDto(
    Guid Id,
    Guid CommunityId,
    Guid HostUnitId,
    Guid HostResidentId,
    string Name,
    string? Phone,
    string Category,
    int DaysOfWeek,
    DateTime StartDate,
    DateTime EndDate,
    int? DailyStartMinutes,
    int? DailyEndMinutes,
    string Status);

/// <summary>
/// Define a recurring visitor for the resident's OWN unit (Req 25.2). The schedule is stored as data
/// (days-of-week bitmask + valid date range + optional daily time window); a matching date authorizes
/// entry without a new approval.
/// </summary>
/// <param name="HostUnitId">The host unit (must be the resident's own unit). Required.</param>
/// <param name="HostResidentId">The defining resident id to record. Required.</param>
/// <param name="Name">The recurring visitor's name. Required.</param>
/// <param name="Phone">Optional visitor phone.</param>
/// <param name="Category">Visitor-category code from the Visitor_Category list. Required.</param>
/// <param name="DaysOfWeek">7-bit mask of authorized days (Sun=1 … Sat=64). Must be &gt; 0.</param>
/// <param name="StartDate">First valid date (inclusive). Required.</param>
/// <param name="EndDate">Last valid date (inclusive). Must be on/after <paramref name="StartDate"/>.</param>
/// <param name="DailyStartMinutes">Optional daily-window start (0–1439 minutes from midnight).</param>
/// <param name="DailyEndMinutes">Optional daily-window end (1–1440 minutes from midnight).</param>
public sealed record CreateRecurringVisitorRequest(
    Guid HostUnitId,
    Guid HostResidentId,
    string Name,
    string? Phone,
    string Category,
    int DaysOfWeek,
    DateTime StartDate,
    DateTime EndDate,
    int? DailyStartMinutes,
    int? DailyEndMinutes);

/// <summary>Read shape for an <c>EntryExitEvent</c> returned to callers (Req 25.3).</summary>
/// <param name="Id">The event id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="GateId">Optional gate id (Gate master is Task 16.3), or null.</param>
/// <param name="GateLabel">Optional free-text gate label captured at the gate.</param>
/// <param name="DestinationUnitId">Optional destination unit.</param>
/// <param name="VisitorId">The approved walk-in that admitted this entry, or null.</param>
/// <param name="VisitPassId">The verified pass that admitted this entry, or null.</param>
/// <param name="RecurringVisitorId">The authorized recurring visitor that admitted this entry, or null.</param>
/// <param name="VisitorName">Denormalized visitor name captured on the event.</param>
/// <param name="Category">Denormalized configurable visitor-category code (Req 25.1).</param>
/// <param name="VehicleNumber">Optional captured vehicle number.</param>
/// <param name="EntryTsUtc">Entry timestamp (Req 25.3).</param>
/// <param name="ExitTsUtc">Exit timestamp, or null while the visitor is on-site (the open event).</param>
public sealed record EntryExitEventDto(
    Guid Id,
    Guid CommunityId,
    Guid? GateId,
    string? GateLabel,
    Guid? DestinationUnitId,
    Guid? VisitorId,
    Guid? VisitPassId,
    Guid? RecurringVisitorId,
    string VisitorName,
    string Category,
    string? VehicleNumber,
    DateTime EntryTsUtc,
    DateTime? ExitTsUtc);

/// <summary>
/// Record a visitor's ENTRY at a gate (Req 25.3). The guard identifies WHICH authorization admitted
/// the visitor by supplying exactly one of <paramref name="VisitorId"/> (an approved walk-in),
/// <paramref name="VisitPassId"/> (a verified pass) or <paramref name="RecurringVisitorId"/> (a
/// recurring visitor authorized for today). The service re-checks admittability and rejects a
/// non-admittable authorization (Req 24.4, 25.3). The entry timestamp is the service clock's now.
/// </summary>
/// <param name="VisitorId">The approved walk-in admitting this entry, or null.</param>
/// <param name="VisitPassId">The verified pass admitting this entry, or null.</param>
/// <param name="RecurringVisitorId">The authorized recurring visitor admitting this entry, or null.</param>
/// <param name="GateId">Optional gate id (Gate master is Task 16.3).</param>
/// <param name="GateLabel">Optional free-text gate label.</param>
/// <param name="VehicleNumber">Optional captured vehicle number.</param>
public sealed record RecordEntryRequest(
    Guid? VisitorId,
    Guid? VisitPassId,
    Guid? RecurringVisitorId,
    Guid? GateId,
    string? GateLabel,
    string? VehicleNumber);

/// <summary>
/// Record a visitor's EXIT at a gate (Req 25.3). The guard identifies the SAME authorization used on
/// entry; the service stamps the exit timestamp against the matching OPEN event (ExitTsUtc == null)
/// for that authorization. An exit with no open entry is a validation error.
/// </summary>
/// <param name="VisitorId">The walk-in whose open entry to close, or null.</param>
/// <param name="VisitPassId">The pass whose open entry to close, or null.</param>
/// <param name="RecurringVisitorId">The recurring visitor whose open entry to close, or null.</param>
/// <param name="GateId">Optional gate id (Gate master is Task 16.3).</param>
/// <param name="GateLabel">Optional free-text gate label.</param>
public sealed record RecordExitRequest(
    Guid? VisitorId,
    Guid? VisitPassId,
    Guid? RecurringVisitorId,
    Guid? GateId,
    string? GateLabel);
