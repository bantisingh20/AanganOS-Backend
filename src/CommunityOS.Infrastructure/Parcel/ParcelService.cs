using System.Security.Cryptography;
using CommunityOS.Application.Abstractions;
using CommunityOS.Application.Abstractions.Idempotency;
using CommunityOS.Application.Abstractions.Security;
using CommunityOS.Application.Audit;
using CommunityOS.Application.Auth;
using CommunityOS.Application.Config;
using CommunityOS.Application.Exceptions;
using CommunityOS.Application.Gate;
using CommunityOS.Application.Notifications;
using CommunityOS.Application.Parcel;
using CommunityOS.Application.Querying;
using CommunityOS.Contracts.Envelope;
using CommunityOS.Domain.Identity;
using CommunityOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using DomainCustodyLocation = CommunityOS.Domain.Parcel.CustodyLocation;
using DomainCustodyTimelineEntry = CommunityOS.Domain.Parcel.CustodyTimelineEntry;
using DomainCustodyTimelineEventTypes = CommunityOS.Domain.Parcel.CustodyTimelineEventTypes;
using DomainParcel = CommunityOS.Domain.Parcel.Parcel;
using DomainParcelAuthorization = CommunityOS.Domain.Parcel.ParcelAuthorization;
using DomainParcelHandover = CommunityOS.Domain.Parcel.ParcelHandover;
using DomainParcelStatuses = CommunityOS.Domain.Parcel.ParcelStatuses;
using DomainParcelVerificationMethods = CommunityOS.Domain.Parcel.ParcelVerificationMethods;

namespace CommunityOS.Infrastructure.Parcel;

/// <summary>
/// EF-backed <see cref="IParcelService"/> (Task 18.1, Req 28.1–28.4, 29.3): parcel registration and
/// the configurable status state machine. Custody-location assignment (18.2), handover (18.3), the
/// append-only timeline + SLA/lost/damaged/returned flows (18.4) and the HTTP controllers (18.5)
/// extend this same seam in later tasks.
///
/// <para><b>Registration (Req 28.1, 28.4, 29.3).</b> A guard with <c>Create</c> on <c>Parcel</c>
/// registers a received parcel for a recipient unit in their community: the recipient unit is
/// resolved tenant-scoped (so a cross-tenant/non-own unit is a validation error, same-community by
/// construction), the category + condition are validated against the editable <c>Parcel_Category</c>
/// / <c>Parcel_Condition</c> lists, the received timestamp is stamped from the clock, the status
/// starts <c>received</c>, the optional photo/condition-evidence File_Service references are recorded
/// (Req 28.4), and the parcel is flagged priority when its category is one of the configurable
/// priority categories (<c>parcel.priorityCategories</c>, Req 29.3). Audited.</para>
///
/// <para><b>Status state machine (Req 28.2, 28.3).</b> A status change is permitted only when the
/// <c>from-&gt;to</c> pair is present in the editable <c>Parcel_Status_Transition</c> allow-list for
/// the community — an illegal transition (including a no-op <c>from==to</c>) is rejected with a
/// VALIDATION_ERROR and nothing is mutated (Req 28.3), with no hardcoded transition table. The parcel
/// is loaded TRACKED so EF carries the <c>RowVersion</c> into the UPDATE: two concurrent transitions
/// race on the row-version check and exactly one wins (the loser → CONFLICT via the central mapping
/// in <see cref="AppDbContext.SaveChangesAsync"/>). Audited.</para>
///
/// <para><b>Tenant + self scope.</b> <c>Parcel</c> is <c>IUnitScoped</c>, so the DbContext resident
/// self-scope filter restricts a resident principal to parcels addressed to their own unit — no
/// bespoke predicate here. Reads use <c>AsNoTracking</c>; mutations are audited and committed under
/// the parcel RowVersion.</para>
/// </summary>
public sealed class ParcelService(
    AppDbContext db,
    IAuditService audit,
    IConfigService config,
    IClock clock,
    IAuthorizationGuard authorizationGuard,
    INotificationService notifications,
    IPasswordHasher hasher,
    IIdempotencyService idempotency,
    ISecurityOpsService securityOps,
    ITenantContext tenant,
    IPageSizePolicy pageSizePolicy,
    IAsyncQueryExecutor executor)
    : IParcelService
{
    // Allow-list for the Task 18.5 parcel list endpoint (Req 28.5). Only these fields reach the query
    // — no arbitrary client LINQ (steering: no hardcoding, injection-safe). Sorts/filters ride the
    // parcel's indexed columns (status / category / recipient unit / received timestamp); default is
    // newest-received-first. Mirrors the Gate/Resident list configs.
    private static readonly ListQueryConfiguration<DomainParcel> ParcelQueryConfig =
        new ListQueryConfiguration<DomainParcel>()
            .WithDefaultSort(p => p.ReceivedAtUtc, SortDirection.Descending)
            .Sortable("receivedAtUtc", p => p.ReceivedAtUtc)
            .Sortable("status", p => p.Status)
            .Sortable("category", p => p.Category)
            .Sortable("trackingNumber", p => p.TrackingNumber)
            .Filterable("status", p => p.Status)
            .Filterable("category", p => p.Category)
            .Filterable("condition", p => p.Condition)
            .Filterable("recipientUnitId", p => p.UnitId)
            .Filterable("custodyLocationId", p => p.CustodyLocationId)
            .Filterable("isPriority", p => p.IsPriority)
            .Searchable(p => p.TrackingNumber)
            .Searchable(p => p.Provider);

    private const string DuplicateCustodyLocationCodeMessage =
        "A custody location with the same code already exists in this community.";

    // A given active custody location holds at most one parcel (Req 29.4). The filtered unique index
    // UX_Parcel_CustodyLocationId_Active decides the winner under concurrency; the race loser's
    // unique-violation surfaces here as a CONFLICT.
    private const string CustodyLocationInUseMessage =
        "That custody location is already holding another parcel. Choose a different location.";
    // Default priority categories (Req 29.3) when parcel.priorityCategories is unset in Config_Service
    // — a safe fallback, not a hardcoded decision: a community can override the key to tune which
    // categories are urgent without a code change (steering: no hardcoding).
    private const string DefaultPriorityCategories = "medicine,perishable,urgent";

    // Handover verification config fallbacks (Req 30.1, 30.2). Safe defaults when the Config_Service
    // keys are unset — a community overrides them without a code change (steering: no hardcoding).
    private const string DefaultVerificationMethod = DomainParcelVerificationMethods.Otp;
    private const int DefaultOtpLength = 6;
    private const int DefaultAuthorizationValidityHours = 48;

    // How many QR-token generations to try before giving up on a (vanishingly unlikely) collision —
    // same bound GateService uses for the visit-pass QR token.
    private const int QrTokenAttempts = 5;

    // The logical endpoint the handover idempotency key scopes (Req 30.5). Task 18.5's
    // POST /parcels/{id}/handover is the HTTP face of this operation; keeping the key here means the
    // at-most-once guarantee holds even for a non-HTTP caller of the service.
    private const string HandoverEndpoint = "ParcelService.PerformHandover";

    // Custody-SLA default (hours) when parcel.custody.slaHours is unset in Config_Service (Req 31.2)
    // — a safe fallback, not a hardcoded decision: a community overrides the key to tune its custody
    // SLA without a code change (steering: no hardcoding). 72h = 3 days held before escalation.
    private const int DefaultCustodySlaHours = 72;

    // Linked-incident defaults for a lost/damaged parcel (Req 31.3) when the caller does not override
    // them. Both are valid seeded Incident_Category / Incident_Severity codes; a community can pass a
    // different (still validated) code, and the SecurityOps escalation threshold decides whether the
    // linked incident auto-escalates — the incident logic is NOT duplicated here (steering: reuse).
    private const string DefaultLostDamagedIncidentCategory = "other";
    private const string DefaultLostDamagedIncidentSeverity = "medium";

    // Hard upper bound on how many overdue parcels a single SLA sweep call will process, regardless
    // of the worker-supplied batch size — keeps the sweep's work bounded so it stays correct and the
    // app stays up under a large backlog (steering: bounded work / scalable). The worker passes its
    // configured batch size; this clamps it.
    private const int MaxSlaSweepBatch = 500;

    public async Task<ParcelDto> RegisterParcelAsync(RegisterParcelRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 28.1): a guard registers a parcel with Create on Parcel. The Task 18.5
        // endpoint gate declares the same requirement; the invariant must hold regardless of caller —
        // no permission → FORBIDDEN with nothing mutated.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "Parcel", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to register a parcel.");
        }

        var trackingNumber = RequireText(request.TrackingNumber, "trackingNumber", "A tracking number is required.");
        var provider = RequireText(request.Provider, "provider", "A delivery provider is required.");

        // Resolve the recipient unit tenant-scoped to anchor the parcel in the unit's community and run
        // the same-community/own-unit check. The resident self-scope filter (if the caller is a
        // resident) already restricts this read to their own unit, so a cross-unit recipient → not found.
        var communityId = await RequireRecipientUnitCommunityAsync(request.RecipientUnitId, cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(communityId);

        var category = await RequireValidCodeAsync(
            request.Category, MasterDataKeys.ParcelCategory, communityId, "category", "parcel category", cancellationToken);
        var condition = await RequireValidCodeAsync(
            request.Condition, MasterDataKeys.ParcelCondition, communityId, "condition", "parcel condition", cancellationToken);

        var isPriority = await IsPriorityCategoryAsync(category, communityId, cancellationToken);

        var parcel = new DomainParcel
        {
            CommunityId = communityId,
            RecipientUnitId = request.RecipientUnitId,
            TrackingNumber = trackingNumber,
            Provider = provider,
            Category = category,
            Condition = condition,
            Status = DomainParcelStatuses.Received,
            Receiver = Trimmed(request.Receiver),
            ReceivedLocation = Trimmed(request.ReceivedLocation),
            ReceivedAtUtc = clock.UtcNow,
            PhotoFileId = request.PhotoFileId,
            ConditionEvidenceFileId = request.ConditionEvidenceFileId,
            IsPriority = isPriority,
        };

        db.Parcels.Add(parcel);

        await audit.AppendAsync(
            new AuditEntry("Create", "Parcel", parcel.Id.ToString(), parcel.CommunityId, Detail: null),
            cancellationToken);

        // Append the first custody-timeline entry (Req 31.1): registration is a parcel event, so it
        // is recorded on the append-only timeline. Only Adds the row — the single SaveChanges below
        // commits the parcel + the audit + the timeline entry atomically.
        AppendTimeline(parcel, DomainCustodyTimelineEventTypes.Registered,
            fromStatus: null, toStatus: parcel.Status, detail: null);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(parcel);
    }

    public async Task<ParcelDto> ChangeParcelStatusAsync(Guid parcelId, ChangeParcelStatusRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 28.3): a status change is an Edit verb on Parcel.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "Parcel", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to change a parcel's status.");
        }

        var toStatus = RequireText(request.ToStatus, "toStatus", "A target status is required.");

        // TRACKED load: we mutate the row and need the RowVersion carried into the UPDATE so the
        // transition records exactly once under concurrency. The global tenant + resident self-scope
        // filter restricts this to the caller's authorized communities, so a foreign-tenant/non-own
        // parcel is a not-found.
        var parcel = await db.Parcels.FirstOrDefaultAsync(p => p.Id == parcelId, cancellationToken)
            ?? throw new NotFoundException("Parcel not found.");

        authorizationGuard.EnsureCommunityAuthorized(parcel.CommunityId);

        // The target status must be a valid, active Parcel_Status code (Req 28.2) …
        await RequireValidCodeAsync(
            toStatus, MasterDataKeys.ParcelStatus, parcel.CommunityId, "toStatus", "parcel status", cancellationToken);

        // … and the from->to transition must be in the editable allow-list (Req 28.3). An illegal
        // transition (including a no-op from==to that is not seeded) → VALIDATION_ERROR, nothing
        // mutated — no hardcoded transition table.
        await RequireAllowedTransitionAsync(parcel.Status, toStatus, parcel.CommunityId, cancellationToken);

        var fromStatus = parcel.Status;
        parcel.Status = toStatus;

        await audit.AppendAsync(
            new AuditEntry(
                "ChangeStatus",
                "Parcel",
                parcel.Id.ToString(),
                parcel.CommunityId,
                Detail: Trimmed(request.Reason)),
            cancellationToken);

        // Record the status change on the append-only timeline (Req 31.1). Only Adds the row; the
        // single SaveChanges below commits the transition + the audit + the timeline entry atomically.
        AppendTimeline(parcel, DomainCustodyTimelineEventTypes.StatusChanged,
            fromStatus: fromStatus, toStatus: toStatus, detail: Trimmed(request.Reason));

        // RowVersion makes the transition record exactly once under concurrency: the loser's save trips
        // a DbUpdateConcurrencyException mapped centrally to CONFLICT (Req 28.3, 22.4).
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(parcel);
    }

    public async Task<ParcelDto?> GetParcelAsync(Guid parcelId, CancellationToken cancellationToken = default)
    {
        // Tenant + resident self-scope filtered AsNoTracking read (Req 28.5, 5.4): a resident only
        // sees parcels addressed to their own unit; an unknown/foreign id is hidden by the filter → null.
        var parcel = await db.Parcels.AsNoTracking().FirstOrDefaultAsync(p => p.Id == parcelId, cancellationToken);
        return parcel is null ? null : ToDto(parcel);
    }

    public Task<PagedData<ParcelDto>> ListParcelsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // Tenant + resident self-scope filtered by the global query filter (Req 28.5): a resident
        // principal only sees parcels addressed to their own unit. The allow-listed query config fixes
        // the sort/filter/search surface; AsNoTracking projection, page size clamped by the shared
        // policy — mirrors ListVisitPassesAsync.
        return db.Parcels.AsNoTracking().ToPagedResultAsync(
            parameters, ParcelQueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
    }

    public async Task<CustodyLocationDto> CreateCustodyLocationAsync(CreateCustodyLocationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 29.1): managing the custody-location catalog is a Create on
        // CustodyLocation. The Task 18.5 endpoint gate declares the same; the invariant holds
        // regardless of caller — no permission → FORBIDDEN with nothing mutated.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "CustodyLocation", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to create a custody location.");
        }

        authorizationGuard.EnsureCommunityAuthorized(request.CommunityId);
        var code = RequireText(request.Code, "code", "A custody location code is required.");

        var location = new DomainCustodyLocation { CommunityId = request.CommunityId, Code = code };
        db.CustodyLocations.Add(location);

        // Concurrency-safe (Req 29.1): let the DB unique index decide the winner (no read-then-write)
        // — same pattern as ParkingSlot/StorageLocation code uniqueness.
        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), DuplicateCustodyLocationCodeMessage);

        await audit.AppendAsync(
            new AuditEntry("Create", "CustodyLocation", location.Id.ToString(), location.CommunityId, Detail: null),
            cancellationToken);

        return new CustodyLocationDto(location.Id, location.CommunityId, location.Code);
    }

    public async Task<ParcelDto> AssignCustodyLocationAsync(Guid parcelId, AssignCustodyLocationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 29.1): assigning custody is part of the parcel's lifecycle, an Edit
        // verb on Parcel (the custody assignment is recorded on the Parcel row). The Task 18.5
        // endpoint gate declares the same; the invariant holds regardless of caller.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "Parcel", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to assign a parcel to custody.");
        }

        // TRACKED load: we mutate the row (status + custody location) and need the RowVersion carried
        // into the UPDATE so a concurrent transition records exactly once. The global tenant +
        // resident self-scope filter restricts this to the caller's authorized communities, so a
        // foreign-tenant/non-own parcel is a not-found.
        var parcel = await db.Parcels.FirstOrDefaultAsync(p => p.Id == parcelId, cancellationToken)
            ?? throw new NotFoundException("Parcel not found.");

        authorizationGuard.EnsureCommunityAuthorized(parcel.CommunityId);

        // The custody location must exist in the parcel's community (tenant-scoped read; the global
        // filter already hides foreign-tenant rows). Cross-community/unknown → validation error.
        await RequireCustodyLocationInCommunityAsync(request.CustodyLocationId, parcel.CommunityId, cancellationToken);

        // Moving into custody is a status transition through the SAME configurable state machine as
        // ChangeParcelStatus (Req 28.3): current -> in_custody must be in the editable allow-list, so
        // a parcel can only be taken into custody from a status the community permits — no hardcoded
        // table. A no-op (already in_custody) is not a seeded self-transition, so it is rejected here.
        await RequireAllowedTransitionAsync(parcel.Status, DomainParcelStatuses.InCustody, parcel.CommunityId, cancellationToken);

        var fromStatus = parcel.Status;
        parcel.Status = DomainParcelStatuses.InCustody;
        parcel.CustodyLocationId = request.CustodyLocationId; // non-null => the active assignment (Req 29.1)

        await audit.AppendAsync(
            new AuditEntry("AssignCustody", "Parcel", parcel.Id.ToString(), parcel.CommunityId,
                Detail: request.CustodyLocationId.ToString()),
            cancellationToken);

        // Record the custody-location assignment on the append-only timeline (Req 31.1). Only Adds
        // the row; the single SaveChanges below commits the transition + the custody assignment + the
        // audit + the notifications + the timeline entry atomically (transactional outbox).
        AppendTimeline(parcel, DomainCustodyTimelineEventTypes.CustodyAssigned,
            fromStatus: fromStatus, toStatus: parcel.Status,
            detail: request.CustodyLocationId.ToString());

        // Notify the recipient unit's residents that a parcel was received/taken into custody (Req
        // 29.2), with priority handling for a priority-flagged parcel (Req 29.3). EnqueueAsync only
        // Adds Pending outbox rows to THIS DbContext (no save), so the single SaveChangesAsync below
        // commits the status change, the custody assignment, the audit row AND the notifications
        // atomically (transactional outbox) — a rollback (e.g. the custody-location race loser) queues
        // nothing. Confidentiality + preference rules live inside Notification_Service; it never throws
        // for an opted-out resident/unknown template, so a notification decision can't break the
        // assignment.
        await EnqueueParcelReceivedAsync(parcel, cancellationToken);

        // One save = one transaction. The filtered unique index UX_Parcel_CustodyLocationId_Active
        // makes "one active custody location → one parcel" a DB guarantee: a racing duplicate
        // assignment of the SAME location trips the unique violation, translated to CONFLICT here
        // (Req 29.4). A concurrent transition losing the RowVersion check → CONFLICT via the central
        // mapping in AppDbContext.SaveChangesAsync (Req 28.3).
        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), CustodyLocationInUseMessage);

        return ToDto(parcel);
    }

    public async Task<AuthorizeCollectorResult> AuthorizeCollectorAsync(AuthorizeCollectorRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 30.1): a resident authorizes a collector for a parcel addressed to
        // their OWN unit with Create on ParcelAuthorization. The Task 18.5 endpoint gate declares the
        // same; the invariant holds regardless of caller — no permission → FORBIDDEN, nothing mutated.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "ParcelAuthorization", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to authorize a parcel collector.");
        }

        var collectorName = RequireText(request.CollectorName, "collectorName", "A collector name is required.");

        // Load the parcel tenant + resident-self-scope filtered: a resident principal only resolves a
        // parcel addressed to their own unit, so a cross-unit/foreign-tenant parcel is a not-found —
        // the self-scope check that a resident authorizes only their own unit's parcels (Req 30.1).
        var parcel = await db.Parcels.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.ParcelId, cancellationToken)
            ?? throw new NotFoundException("Parcel not found.");

        authorizationGuard.EnsureCommunityAuthorized(parcel.CommunityId);

        // Resolve + validate the verification method against the configurable list (Req 30.2); when
        // the caller omits it, use the community default (also validated), never a hardcoded choice.
        var method = string.IsNullOrWhiteSpace(request.VerificationMethod)
            ? await config.GetStringAsync(ConfigKeys.ParcelHandoverDefaultVerificationMethod, DefaultVerificationMethod, parcel.CommunityId, cancellationToken)
            : request.VerificationMethod.Trim();
        await RequireValidCodeAsync(
            method, MasterDataKeys.ParcelVerificationMethod, parcel.CommunityId, "verificationMethod", "verification method", cancellationToken);

        // An ID-bearing method needs the expected ID reference up front, so the handover can match the
        // presented reference against it (Req 30.2).
        var expectedId = Trimmed(request.ExpectedIdReference);
        if (MethodRequiresId(method) && expectedId is null)
        {
            throw new ValidationException(
                "expectedIdReference", "An expected ID reference is required for an ID-based verification method.");
        }

        var validityHours = await config.GetIntAsync(
            ConfigKeys.ParcelHandoverAuthorizationValidityHours, DefaultAuthorizationValidityHours, parcel.CommunityId, cancellationToken);
        var otp = GenerateOtp(await config.GetIntAsync(ConfigKeys.ParcelHandoverOtpLength, DefaultOtpLength, parcel.CommunityId, cancellationToken));

        var authorization = new DomainParcelAuthorization
        {
            CommunityId = parcel.CommunityId,
            UnitId = parcel.UnitId, // the authorized parcel's recipient unit (IUnitScoped backing column)
            ParcelId = parcel.Id,
            // The authorizing resident is optional on the record; the acting user is already captured
            // on the audit trail by the SaveChanges interceptor. A resident-id link is wired by the
            // Task 18.5 endpoint when the caller is a resident principal.
            AuthorizingResidentId = null,
            CollectorName = collectorName,
            CollectorRelationship = Trimmed(request.CollectorRelationship),
            ExpectedIdReference = expectedId,
            OtpHash = hasher.Hash(otp),
            VerificationMethod = method,
            ExpiresAtUtc = clock.UtcNow.AddHours(validityHours),
            IsUsed = false,
        };

        // Opaque, community-unique QR token — the DB Unique(CommunityId, QrToken) index is the source
        // of truth; on the (astronomically unlikely) collision we regenerate and retry rather than a
        // read-then-write pre-check (same approach as the visit-pass QR token).
        var qrToken = await SaveWithUniqueQrTokenAsync(authorization, cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Create", "ParcelAuthorization", authorization.Id.ToString(), authorization.CommunityId, Detail: null),
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new AuthorizeCollectorResult(ToDto(authorization), otp, qrToken);
    }

    public async Task<ParcelHandoverDto> PerformHandoverAsync(PerformHandoverRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 30.2, 30.4): a guard performs the handover — an Edit on Parcel (the
        // handover transitions the Parcel row to handed_over). The Task 18.5 endpoint gate declares
        // the same; the invariant holds regardless of caller.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "Parcel", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to perform a parcel handover.");
        }

        var key = RequireText(request.IdempotencyKey, "idempotencyKey", "An idempotency key is required for a parcel handover.");

        // TRACKED load of the authorization: on success we flip IsUsed and need its RowVersion carried
        // into the UPDATE so a single authorization can't drive two handovers. Tenant +
        // resident-self-scope filtered → a foreign-tenant one is a not-found.
        var authorization = await db.ParcelAuthorizations.FirstOrDefaultAsync(a => a.Id == request.ParcelAuthorizationId, cancellationToken)
            ?? throw new NotFoundException("Parcel authorization not found.");

        authorizationGuard.EnsureCommunityAuthorized(authorization.CommunityId);

        // TRACKED load of the parcel: on success we transition it and need its RowVersion in the UPDATE.
        var parcel = await db.Parcels.FirstOrDefaultAsync(p => p.Id == authorization.ParcelId, cancellationToken)
            ?? throw new NotFoundException("Parcel not found.");

        authorizationGuard.EnsureCommunityAuthorized(parcel.CommunityId);

        // Verify the collector by the configured method (Req 30.2) BEFORE reserving the idempotency
        // key. A FAILED verification records the failed attempt, rejects with a VALIDATION_ERROR and
        // mutates NOTHING on the parcel/authorization — the parcel stays in its current custody status
        // (Req 30.3) — and, crucially, does NOT burn the idempotency key, so the guard can retry the
        // handover with corrected credentials under a fresh (or the same) key. At-most-once applies to
        // the successful handover, not to a rejected verification.
        var now = clock.UtcNow;
        var failure = EvaluateVerification(authorization, request, now);
        if (failure is not null)
        {
            await audit.AppendAsync(
                new AuditEntry("HandoverVerificationFailed", "Parcel", parcel.Id.ToString(), parcel.CommunityId, Detail: failure),
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            throw new ValidationException("verification", $"Collector verification failed: {failure}");
        }

        // Verification passed. AT-MOST-ONCE (Req 30.5): wrap only the state-mutating handover commit in
        // the reusable idempotency primitive. It reserves the key via a Unique(KeyValue, Endpoint)
        // insert BEFORE running the operation, so a retry (or a concurrent request) carrying the same
        // key performs the handover exactly once and replays the first result — a DB guarantee, not an
        // application read-then-write check. The request hash binds the key to this payload so the
        // same key reused for a DIFFERENT handover is a conflict, never a wrong replay.
        var requestHash = ComputeHandoverRequestHash(request);
        var outcome = await idempotency.ExecuteAsync(
            key,
            HandoverEndpoint,
            requestHash,
            ct => CommitHandoverAsync(parcel, authorization, request, now, ct),
            cancellationToken);

        return outcome.Result;
    }

    public async Task<ParcelDto> RecordLostDamagedAsync(Guid parcelId, RecordLostDamagedRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 31.3): recording lost/damaged transitions the Parcel row — an Edit on
        // Parcel. The linked-incident creation below additionally requires Create on Incident (the
        // guard-facing incident grant), enforced by SecurityOpsService itself — the incident logic is
        // reused, not duplicated.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "Parcel", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to record a parcel as lost or damaged.");
        }

        var circumstances = RequireText(request.Circumstances, "circumstances", "The circumstances are required.");

        // TRACKED load: we transition the row and need its RowVersion carried into the UPDATE so the
        // transition records exactly once. Tenant + resident self-scope filtered → a foreign/non-own
        // parcel is a not-found.
        var parcel = await db.Parcels.FirstOrDefaultAsync(p => p.Id == parcelId, cancellationToken)
            ?? throw new NotFoundException("Parcel not found.");

        authorizationGuard.EnsureCommunityAuthorized(parcel.CommunityId);

        // Transition to lost_damaged through the SAME configurable state machine (Req 28.3): the
        // current -> lost_damaged edge must be in the editable allow-list, so a parcel can only be
        // recorded lost/damaged from a status the community permits — no hardcoded table, and a
        // double record (lost_damaged is terminal, no outgoing/self edge) → VALIDATION_ERROR.
        await RequireAllowedTransitionAsync(parcel.Status, DomainParcelStatuses.LostDamaged, parcel.CommunityId, cancellationToken);

        var fromStatus = parcel.Status;
        parcel.Status = DomainParcelStatuses.LostDamaged;
        parcel.CustodyLocationId = null; // a lost/damaged parcel frees its custody location (Req 29.4)

        await audit.AppendAsync(
            new AuditEntry("RecordLostDamaged", "Parcel", parcel.Id.ToString(), parcel.CommunityId, Detail: circumstances),
            cancellationToken);

        // Create the LINKED incident by REUSING the SecurityOps incident path (Req 31.3) — configurable
        // category/severity, auto-escalation + responder notification all handled there, not duplicated
        // here. It shares this request-scoped DbContext, so its SaveChanges commits the parcel
        // transition + the parcel audit staged above AND the incident + its escalation notifications
        // together in one transaction (atomic). The acting user (unspoofable, from the tenant context)
        // is the reporting user; the circumstances carry into the incident description + evidence note.
        var category = Trimmed(request.Category) ?? DefaultLostDamagedIncidentCategory;
        var severity = Trimmed(request.Severity) ?? DefaultLostDamagedIncidentSeverity;
        var description = request.EvidenceFileId is { } ev
            ? $"Parcel {parcel.TrackingNumber} ({parcel.Provider}) recorded lost/damaged: {circumstances} [evidence: {ev:N}]"
            : $"Parcel {parcel.TrackingNumber} ({parcel.Provider}) recorded lost/damaged: {circumstances}";

        var incident = await securityOps.ReportIncidentAsync(
            new ReportIncidentRequest(
                parcel.CommunityId,
                ReportedByUserId: ParseActingUserId(),
                GateId: null,
                Location: parcel.ReceivedLocation,
                Category: category,
                Severity: severity,
                Description: description),
            cancellationToken);

        // Record the lost/damaged event on the append-only timeline, linking the incident id (Req
        // 31.1, 31.3). Appended AFTER the incident so its id is captured; the parcel transition is
        // already committed by the incident save above, so this is a separate append-only insert.
        AppendTimeline(parcel, DomainCustodyTimelineEventTypes.LostDamaged,
            fromStatus: fromStatus, toStatus: parcel.Status,
            detail: $"incidentId={incident.Id:N}; {circumstances}");

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(parcel);
    }

    public async Task<ParcelDto> RecordReturnedAsync(Guid parcelId, RecordReturnedRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 31.4): recording a return transitions the Parcel row — an Edit on Parcel.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "Parcel", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to record a parcel as returned.");
        }

        // TRACKED load for the RowVersion-guarded transition; tenant + self-scope filtered.
        var parcel = await db.Parcels.FirstOrDefaultAsync(p => p.Id == parcelId, cancellationToken)
            ?? throw new NotFoundException("Parcel not found.");

        authorizationGuard.EnsureCommunityAuthorized(parcel.CommunityId);

        // Transition to returned through the SAME configurable state machine (Req 28.3): the
        // current -> returned edge must be in the editable allow-list, so a double return (returned is
        // terminal) → VALIDATION_ERROR.
        await RequireAllowedTransitionAsync(parcel.Status, DomainParcelStatuses.Returned, parcel.CommunityId, cancellationToken);

        var fromStatus = parcel.Status;
        parcel.Status = DomainParcelStatuses.Returned;
        parcel.CustodyLocationId = null; // a returned parcel frees its custody location (Req 29.4)

        var reason = Trimmed(request.Reason);

        await audit.AppendAsync(
            new AuditEntry("RecordReturned", "Parcel", parcel.Id.ToString(), parcel.CommunityId, Detail: reason),
            cancellationToken);

        // Append the return event to the custody timeline (Req 31.4, 31.1). Only Adds the row; the
        // single SaveChanges below commits the transition + the audit + the timeline entry atomically.
        AppendTimeline(parcel, DomainCustodyTimelineEventTypes.Returned,
            fromStatus: fromStatus, toStatus: parcel.Status, detail: reason);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(parcel);
    }

    public async Task<IReadOnlyList<CustodyTimelineEntryDto>> GetCustodyTimelineAsync(Guid parcelId, CancellationToken cancellationToken = default)
    {
        // Defense-in-depth (Req 31.5): reading the timeline needs View on CustodyTimelineEntry.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "CustodyTimelineEntry", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view a parcel's custody timeline.");
        }

        // Tenant-scoped (global filter) AsNoTracking chronological read riding the
        // (CommunityId, ParcelId, OccurredAtUtc) index — a foreign-tenant parcel's entries are hidden
        // by the filter, so an unknown/foreign id yields an empty list (no leak, Req 31.5). Ordered by
        // OccurredAtUtc then Id as a stable tie-break for events sharing a timestamp.
        var entries = await db.CustodyTimelineEntries.AsNoTracking()
            .Where(e => e.ParcelId == parcelId)
            .OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

        return entries.Select(ToDto).ToList();
    }

    public async Task<int> EscalateOverdueCustodyParcelsAsync(int maxParcels, CancellationToken cancellationToken = default)
    {
        // Bound the work regardless of the caller-supplied size (steering: bounded work / scalable):
        // process at most MaxSlaSweepBatch overdue parcels per call so one pass can't run unbounded
        // under a large backlog. The worker loops to drain the rest across passes.
        var take = Math.Clamp(maxParcels, 1, MaxSlaSweepBatch);
        var now = clock.UtcNow;

        // Resolve the SLA per community in play, not globally. First find the candidate parcels: in
        // custody, not yet escalated. CustodyLocationId is non-null exactly while in custody and is
        // the sweep's natural "still held" predicate; CustodySlaEscalatedAtUtc == null is the
        // at-most-once dedupe guard so a parcel is escalated once, not every pass. TRACKED (we stamp
        // the escalation marker). The worker runs under a super-admin context, so the global tenant
        // filter does not hide rows; a page cap keeps memory bounded.
        var candidates = await db.Parcels
            .Where(p => p.Status == DomainParcelStatuses.InCustody
                && p.CustodyLocationId != null
                && p.CustodySlaEscalatedAtUtc == null)
            .OrderBy(p => p.ReceivedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return 0;
        }

        // Resolve each distinct community's SLA ONCE (no N+1 config reads). Config_Service caches, but
        // de-duping the community set keeps the sweep lean when a batch spans a few communities.
        var slaHoursByCommunity = new Dictionary<Guid, int>();
        foreach (var communityId in candidates.Select(p => p.CommunityId).Distinct())
        {
            slaHoursByCommunity[communityId] = await config.GetIntAsync(
                ConfigKeys.ParcelCustodySlaHours, DefaultCustodySlaHours, communityId, cancellationToken);
        }

        var escalated = 0;
        foreach (var parcel in candidates)
        {
            // Measure custody age from when the parcel was received into the system. (The parcel moved
            // to in_custody at or after ReceivedAtUtc; using ReceivedAtUtc is a safe, slightly
            // conservative lower bound on time-in-custody and avoids a separate per-parcel timeline
            // read in the sweep — ponytail: naive age basis, upgrade to the in_custody timestamp if a
            // tighter SLA is ever needed.)
            var slaHours = slaHoursByCommunity[parcel.CommunityId];
            var hoursHeld = (now - parcel.ReceivedAtUtc).TotalHours;
            if (hoursHeld < slaHours)
            {
                continue;
            }

            // Stamp the escalation marker so this parcel is never escalated twice. The parcel is
            // TRACKED, so its RowVersion rides the UPDATE: a concurrent sweep/mutation losing the
            // row-version check surfaces as a CONFLICT on SaveChanges — we treat that as "someone else
            // handled it" and skip rather than re-notify (handled by the per-parcel save below).
            parcel.CustodySlaEscalatedAtUtc = now;

            await audit.AppendAsync(
                new AuditEntry("CustodySlaEscalated", "Parcel", parcel.Id.ToString(), parcel.CommunityId,
                    Detail: $"heldHours={hoursHeld:F1}; slaHours={slaHours}"),
                cancellationToken);

            // Append the escalation to the append-only timeline (Req 31.1). ActingUserId is null — the
            // sweep is a system actor, not an interactive user.
            AppendTimeline(parcel, DomainCustodyTimelineEventTypes.SlaEscalated,
                fromStatus: null, toStatus: parcel.Status,
                detail: $"Held {hoursHeld:F1}h, exceeded SLA of {slaHours}h.");

            // Notify the recipient residents AND the community admin (push + email, Req 31.2, 64.2).
            // EnqueueAsync only Adds outbox rows; the per-parcel SaveChanges below commits the stamp +
            // the audit + the timeline entry + the notifications together (transactional outbox).
            await EnqueueSlaExceededAsync(parcel, hoursHeld, cancellationToken);

            try
            {
                // Save per parcel so one parcel's concurrency loss (another sweep/writer bumped its
                // RowVersion) is isolated — that parcel is skipped, the rest of the batch still
                // escalates. Keeps the sweep correct under concurrency without a batch-wide rollback.
                await db.SaveChangesAsync(cancellationToken);
                escalated++;
            }
            catch (ConflictException)
            {
                // Another writer updated this parcel first (e.g. a concurrent handover/return, or a
                // parallel sweep). Our staged changes for THIS parcel are discarded on the failed
                // save; detach so the next iteration's save is clean, and skip without re-notifying.
                db.Entry(parcel).State = EntityState.Detached;
            }
        }

        return escalated;
    }

    /// <summary>
    /// Append one append-only <see cref="DomainCustodyTimelineEntry"/> for a parcel event (Req 31.1).
    /// Only <i>Adds</i> the row to the tracked context — the caller's SaveChanges commits it with the
    /// rest of the operation. The acting user + timestamp come from the tenant context / clock, never
    /// a DTO (same anti-spoof stance as the audit trail). The entry is never updated/deleted; the
    /// interceptor enforces that at the data layer.
    /// </summary>
    private void AppendTimeline(DomainParcel parcel, string eventType, string? fromStatus, string? toStatus, string? detail)
    {
        db.CustodyTimelineEntries.Add(new DomainCustodyTimelineEntry
        {
            CommunityId = parcel.CommunityId,
            ParcelId = parcel.Id,
            EventType = eventType,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            ActingUserId = tenant.UserId,
            Detail = detail,
            OccurredAtUtc = clock.UtcNow,
        });
    }

    /// <summary>
    /// Enqueue the parcel custody-SLA-exceeded notification to the recipient unit's residents AND the
    /// community admin (Req 31.2, 64.2). The resident recipients are the recipient unit's current
    /// household members' linked login users (one projecting join, distinct, no N+1 — the same
    /// resolution the parcel-received notice uses); the admin recipients are the users whose roles
    /// grant View on Parcel in the community (RBAC data, no hardcoded recipients). The SLA template
    /// declares the push + email channels (Req 64.2). EnqueueAsync only Adds the outbox rows; the
    /// caller's SaveChanges commits them in the same transaction.
    /// </summary>
    private async Task EnqueueSlaExceededAsync(DomainParcel parcel, double hoursHeld, CancellationToken cancellationToken)
    {
        var custodyLocationCode = await db.CustodyLocations.AsNoTracking()
            .Where(c => c.Id == parcel.CustodyLocationId)
            .Select(c => c.Code)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["trackingNumber"] = parcel.TrackingNumber,
            ["provider"] = parcel.Provider,
            ["custodyLocation"] = custodyLocationCode,
            ["hoursInCustody"] = ((int)hoursHeld).ToString(),
        };

        // Recipient residents on the parcel's unit (current household members with a linked user).
        var recipientUserIds = await db.HouseholdMembers.AsNoTracking()
            .Where(h => h.UnitId == parcel.UnitId && h.EndDate == null)
            .Join(db.Residents.AsNoTracking().Where(r => r.UserId != null),
                h => h.ResidentId,
                r => r.Id,
                (h, r) => r.UserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Community admins = users whose (community-scoped or platform-wide) roles grant View on
        // Parcel. Permission/Role/UserRole are platform-scoped (no tenant filter), so the community
        // predicate on the UserRole scope is explicit — the same audience resolution SecurityOps uses.
        var adminUserIds = await (
            from ur in db.UserRoles.AsNoTracking()
            where ur.CommunityId == parcel.CommunityId || ur.CommunityId == null
            join rp in db.RolePermissions.AsNoTracking() on ur.RoleId equals rp.RoleId
            join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            where p.ResourceType == "Parcel" && p.Action == PermissionAction.View
            select ur.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Union the recipient-resident + admin audiences, de-duped so a resident who is also an admin
        // is notified once. Enqueue the SLA template for each (push + email per the template, Req 64.2).
        foreach (var userId in recipientUserIds.Concat(adminUserIds).Distinct())
        {
            await notifications.EnqueueAsync(
                new NotificationRequest(NotificationTemplateKeys.ParcelSlaExceeded, userId, parcel.CommunityId, Placeholders: placeholders),
                cancellationToken);
        }
    }

    /// <summary>
    /// Parse the acting user id (from the tenant context) into the Guid the incident report requires.
    /// A system/anonymous actor (no user id) → <see cref="Guid.Empty"/>, which SecurityOps rejects —
    /// recording lost/damaged is an interactive guard action, so an acting user is always present on
    /// the real path; this keeps the reuse honest rather than fabricating a reporter.
    /// </summary>
    private Guid ParseActingUserId() =>
        Guid.TryParse(tenant.UserId, out var id) ? id : Guid.Empty;

    /// <summary>
    /// Commit a verified handover (Req 30.4), run at-most-once inside the idempotency wrapper. Records
    /// the handover (collector name/relationship/method/proof/guard/timestamp), consumes the
    /// authorization and transitions the parcel to <c>handed_over</c> through the configurable state
    /// machine in ONE transaction. The parcel and authorization were already loaded TRACKED by the
    /// caller, so their RowVersions ride the UPDATEs here.
    /// </summary>
    private async Task<ParcelHandoverDto> CommitHandoverAsync(
        DomainParcel parcel,
        DomainParcelAuthorization authorization,
        PerformHandoverRequest request,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Transition to handed_over through the SAME configurable state machine as every other status
        // change (Req 28.3) — current -> handed_over must be in the editable allow-list, so a parcel
        // can only be handed over from a status the community permits, no hardcoded table.
        await RequireAllowedTransitionAsync(parcel.Status, DomainParcelStatuses.HandedOver, parcel.CommunityId, cancellationToken);

        var fromStatus = parcel.Status;
        parcel.Status = DomainParcelStatuses.HandedOver;
        parcel.CustodyLocationId = null; // the parcel leaves its custody location on handover (frees it, Req 29.4)
        authorization.IsUsed = true;

        var handover = new DomainParcelHandover
        {
            CommunityId = parcel.CommunityId,
            UnitId = parcel.UnitId,
            ParcelId = parcel.Id,
            ParcelAuthorizationId = authorization.Id,
            CollectorName = authorization.CollectorName,
            CollectorRelationship = authorization.CollectorRelationship,
            VerificationMethod = authorization.VerificationMethod,
            ProofFileId = request.ProofFileId,
            GuardUserId = tenant.UserId, // the handling guard, from the request context (unspoofable)
            HandedOverAtUtc = now,
        };
        db.ParcelHandovers.Add(handover);

        await audit.AppendAsync(
            new AuditEntry("Handover", "Parcel", parcel.Id.ToString(), parcel.CommunityId,
                Detail: $"method={authorization.VerificationMethod}"),
            cancellationToken);

        // Record the handover on the append-only timeline (Req 31.1). Only Adds the row; the single
        // SaveChanges below commits the transition + the authorization consume + the handover row +
        // the audit + the timeline entry atomically.
        AppendTimeline(parcel, DomainCustodyTimelineEventTypes.HandedOver,
            fromStatus: fromStatus, toStatus: parcel.Status,
            detail: $"method={authorization.VerificationMethod}");

        // One save = one transaction: the parcel transition, the authorization consume, the handover
        // row and the audit entry commit atomically. The parcel RowVersion makes the transition record
        // exactly once (a concurrency loser → CONFLICT centrally); the Unique(ParcelId) index on
        // ParcelHandover is a belt-and-braces DB guarantee of one handover per parcel on top of the
        // idempotency-key wrapper (Req 30.4, 30.5).
        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken),
            "This parcel has already been handed over.");

        return ToDto(handover);
    }

    /// <summary>
    /// Evaluate the collector verification for the authorization's configured method (Req 30.2).
    /// Returns null when verification PASSES, or a short reason when it FAILS (expired / wrong method
    /// inputs / mismatched OTP, QR or ID). The caller treats a non-null reason as a rejected handover
    /// that leaves the parcel unchanged (Req 30.3). OTP is compared by salted-hash (constant time,
    /// same as the visit-pass OTP); QR by opaque-token equality; ID by reference match; a
    /// <c>combination</c> requires OTP AND QR AND ID.
    /// </summary>
    private string? EvaluateVerification(DomainParcelAuthorization authorization, PerformHandoverRequest request, DateTime now)
    {
        if (authorization.IsUsed)
        {
            return "the authorization has already been used";
        }

        if (now > authorization.ExpiresAtUtc)
        {
            return "the authorization has expired";
        }

        var method = authorization.VerificationMethod;
        var needsOtp = method is DomainParcelVerificationMethods.Otp or DomainParcelVerificationMethods.Combination;
        var needsQr = method is DomainParcelVerificationMethods.Qr or DomainParcelVerificationMethods.Combination;
        var needsId = MethodRequiresId(method);

        if (needsOtp)
        {
            if (string.IsNullOrWhiteSpace(request.Otp) || !hasher.Verify(authorization.OtpHash, request.Otp.Trim()))
            {
                return "the OTP did not match";
            }
        }

        if (needsQr)
        {
            if (string.IsNullOrWhiteSpace(request.QrToken)
                || !FixedTimeEquals(authorization.QrToken, request.QrToken.Trim()))
            {
                return "the QR token did not match";
            }
        }

        if (needsId)
        {
            if (string.IsNullOrWhiteSpace(request.PresentedIdReference)
                || authorization.ExpectedIdReference is null
                || !FixedTimeEquals(authorization.ExpectedIdReference, request.PresentedIdReference.Trim()))
            {
                return "the ID reference did not match";
            }
        }

        return null;
    }

    private static bool MethodRequiresId(string method) =>
        method is DomainParcelVerificationMethods.Id or DomainParcelVerificationMethods.Combination;

    /// <summary>
    /// Save the authorization, (re)generating the opaque QR token on the (vanishingly unlikely)
    /// Unique(CommunityId, QrToken) collision rather than a read-then-write pre-check — the same
    /// approach GateService uses for the visit-pass QR token. Returns the token that was persisted.
    /// </summary>
    private async Task<string> SaveWithUniqueQrTokenAsync(DomainParcelAuthorization authorization, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < QrTokenAttempts; attempt++)
        {
            authorization.QrToken = GenerateOpaqueToken();
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return authorization.QrToken;
            }
            catch (DbUpdateException) when (attempt < QrTokenAttempts - 1)
            {
                // QR-token collision on the unique index: detach the attempted insert's token and retry
                // with a fresh token. (A different unique violation would also land here, but the only
                // unique constraint on insert is the QR token.)
            }
        }

        throw new ConflictException("Could not allocate a unique collector QR token; please retry.");
    }

    /// <summary>
    /// A stable hash of the handover payload so the idempotency store detects the same key reused for
    /// a DIFFERENT handover (Req 30.5) — it binds the key to this authorization + presented
    /// credentials + proof. The OTP/QR/ID are hashed, never stored in cleartext by the idempotency
    /// store (steering: secrets): only this digest is persisted as the request hash.
    /// </summary>
    private static string ComputeHandoverRequestHash(PerformHandoverRequest request)
    {
        var material = string.Join('|',
            request.ParcelAuthorizationId.ToString("N"),
            request.Otp ?? string.Empty,
            request.QrToken ?? string.Empty,
            request.PresentedIdReference ?? string.Empty,
            request.ProofFileId?.ToString("N") ?? string.Empty);
        var digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(digest);
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));

    /// <summary>Generate a short numeric OTP of the configured length (Req 30.2), min 4 digits.</summary>
    private static string GenerateOtp(int length)
    {
        var digits = Math.Clamp(length, 4, 10);
        var max = (int)Math.Pow(10, digits);
        var value = RandomNumberGenerator.GetInt32(0, max);
        return value.ToString().PadLeft(digits, '0');
    }

    /// <summary>Opaque, high-entropy, URL-safe token for the QR code (Req 30.2), not derived from data.</summary>
    private static string GenerateOpaqueToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>
    /// Resolve the recipient unit (tenant-scoped) and return its community id, rejecting an unknown /
    /// foreign-tenant / (for a resident) non-own unit. The returned community anchors the parcel so it
    /// is always owned by the same community as its recipient unit (same-community by construction,
    /// Req 28.1).
    /// </summary>
    private async Task<Guid> RequireRecipientUnitCommunityAsync(Guid recipientUnitId, CancellationToken cancellationToken)
    {
        var communityId = await db.Units.AsNoTracking()
            .Where(u => u.Id == recipientUnitId)
            .Select(u => (Guid?)u.CommunityId)
            .FirstOrDefaultAsync(cancellationToken);

        return communityId ?? throw new ValidationException(
            "recipientUnitId", "The recipient unit was not found in your authorized communities.");
    }

    /// <summary>
    /// Require that the custody location exists in the parcel's community (Req 29.1). The global
    /// tenant filter already hides foreign-tenant rows, so a cross-community/unknown id is a
    /// validation error. AsNoTracking — we only check existence/community, never mutate it.
    /// </summary>
    private async Task RequireCustodyLocationInCommunityAsync(Guid custodyLocationId, Guid communityId, CancellationToken cancellationToken)
    {
        var found = await db.CustodyLocations.AsNoTracking()
            .AnyAsync(c => c.Id == custodyLocationId && c.CommunityId == communityId, cancellationToken);

        if (!found)
        {
            throw new ValidationException(
                "custodyLocationId", "The custody location was not found in the parcel's community.");
        }
    }

    /// <summary>
    /// Enqueue the parcel-received notification to the recipient unit's residents (Req 29.2) with
    /// priority handling (Req 29.3). The recipient users are the unit's current (open) household
    /// members' linked login users — one projecting join, distinct, no N+1 (the same resolution
    /// <c>GateService</c> uses). A priority-flagged parcel uses the priority template (full push +
    /// SMS + email channel set, Req 64.2); a standard parcel uses the standard template. The custody
    /// location code fills the {custodyLocation} placeholder. EnqueueAsync only Adds the outbox rows;
    /// the caller's SaveChanges commits them in the same transaction.
    /// </summary>
    private async Task EnqueueParcelReceivedAsync(DomainParcel parcel, CancellationToken cancellationToken)
    {
        var recipientUserIds = await db.HouseholdMembers.AsNoTracking()
            .Where(h => h.UnitId == parcel.UnitId && h.EndDate == null)
            .Join(db.Residents.AsNoTracking().Where(r => r.UserId != null),
                h => h.ResidentId,
                r => r.Id,
                (h, r) => r.UserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (recipientUserIds.Count == 0)
        {
            // No linked login users on the recipient unit → no push recipient; nothing to enqueue.
            // The custody assignment + audit still commit (Req 29.1).
            return;
        }

        // Priority handling (Req 29.3): a priority-flagged parcel gets the priority template (which
        // declares the full push + SMS + email channel set, Req 64.2) so a time-sensitive delivery is
        // surfaced more prominently; a standard parcel gets the standard parcel-received template.
        var templateKey = parcel.IsPriority
            ? NotificationTemplateKeys.ParcelReceivedPriority
            : NotificationTemplateKeys.ParcelReceived;

        var custodyLocationCode = await db.CustodyLocations.AsNoTracking()
            .Where(c => c.Id == parcel.CustodyLocationId)
            .Select(c => c.Code)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["trackingNumber"] = parcel.TrackingNumber,
            ["provider"] = parcel.Provider,
            ["custodyLocation"] = custodyLocationCode,
        };

        foreach (var userId in recipientUserIds)
        {
            await notifications.EnqueueAsync(
                new NotificationRequest(templateKey, userId, parcel.CommunityId, Placeholders: placeholders),
                cancellationToken);
        }
    }

    /// <summary>
    /// Validate a code against a configurable master-data list resolved for the community (Req 28.1,
    /// 28.2) — not a frozen enum, so the vocabulary stays editable per community. Returns the trimmed
    /// code on success; an unknown/retired code → VALIDATION_ERROR.
    /// </summary>
    private async Task<string> RequireValidCodeAsync(
        string? value, string listKey, Guid communityId, string field, string what, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(field, $"A {what} is required.");
        }

        var code = value.Trim();
        var allowed = await config.GetMasterDataAsync(listKey, communityId, cancellationToken: cancellationToken);
        if (!allowed.Any(e => string.Equals(e.Code, code, StringComparison.Ordinal)))
        {
            throw new ValidationException(field, $"'{code}' is not a valid {what}.");
        }

        return code;
    }

    /// <summary>
    /// Require that the <paramref name="from"/>-&gt;<paramref name="to"/> transition is present (and
    /// active) in the editable <c>Parcel_Status_Transition</c> allow-list for the community (Req 28.3).
    /// Each entry's code is a <c>from-&gt;to</c> pair of Parcel_Status codes; a transition not in the
    /// list is rejected with a validation error — no hardcoded transition table.
    /// </summary>
    private async Task RequireAllowedTransitionAsync(string from, string to, Guid communityId, CancellationToken cancellationToken)
    {
        var pair = $"{from}->{to}";
        var allowed = await config.GetMasterDataAsync(MasterDataKeys.ParcelStatusTransition, communityId, cancellationToken: cancellationToken);
        if (!allowed.Any(e => string.Equals(e.Code, pair, StringComparison.Ordinal)))
        {
            throw new ValidationException(
                "toStatus",
                $"A parcel cannot move from '{from}' to '{to}'; the transition is not allowed.");
        }
    }

    /// <summary>
    /// Whether <paramref name="category"/> is one of the configurable priority categories (Req 29.3).
    /// The comma-separated <c>parcel.priorityCategories</c> value is resolved per community via
    /// Config_Service (falling back to the seeded default set), so which categories are urgent is
    /// editable without a code change.
    /// </summary>
    private async Task<bool> IsPriorityCategoryAsync(string category, Guid communityId, CancellationToken cancellationToken)
    {
        var configured = await config.GetStringAsync(
            ConfigKeys.ParcelPriorityCategories, DefaultPriorityCategories, communityId, cancellationToken);

        return configured
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(c => string.Equals(c, category, StringComparison.Ordinal));
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string RequireText(string? value, string field, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(field, message);
        }

        return value.Trim();
    }

    private static ParcelDto ToDto(DomainParcel p) =>
        new(p.Id, p.CommunityId, p.RecipientUnitId, p.TrackingNumber, p.Provider, p.Category,
            p.Status, p.Condition, p.Receiver, p.ReceivedLocation, p.ReceivedAtUtc,
            p.PhotoFileId, p.ConditionEvidenceFileId, p.IsPriority, p.CustodyLocationId);

    private static ParcelAuthorizationDto ToDto(DomainParcelAuthorization a) =>
        new(a.Id, a.CommunityId, a.ParcelId, a.UnitId ?? Guid.Empty, a.CollectorName,
            a.CollectorRelationship, a.VerificationMethod, a.ExpiresAtUtc, a.IsUsed);

    private static ParcelHandoverDto ToDto(DomainParcelHandover h) =>
        new(h.Id, h.CommunityId, h.ParcelId, h.ParcelAuthorizationId, h.CollectorName,
            h.CollectorRelationship, h.VerificationMethod, h.ProofFileId, h.GuardUserId, h.HandedOverAtUtc);

    private static CustodyTimelineEntryDto ToDto(DomainCustodyTimelineEntry e) =>
        new(e.Id, e.CommunityId, e.ParcelId, e.EventType, e.FromStatus, e.ToStatus,
            e.ActingUserId, e.Detail, e.OccurredAtUtc);
}
