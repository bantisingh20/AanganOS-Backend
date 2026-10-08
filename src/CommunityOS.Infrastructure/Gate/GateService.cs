using System.Security.Cryptography;
using CommunityOS.Application.Abstractions;
using CommunityOS.Application.Abstractions.Security;
using CommunityOS.Application.Audit;
using CommunityOS.Application.Auth;
using CommunityOS.Application.Config;
using CommunityOS.Application.Exceptions;
using CommunityOS.Application.Gate;
using CommunityOS.Application.Notifications;
using CommunityOS.Application.Querying;
using CommunityOS.Contracts.Envelope;
using CommunityOS.Domain.Gate;
using CommunityOS.Domain.Identity;
using CommunityOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommunityOS.Infrastructure.Gate;

/// <summary>
/// EF-backed <see cref="IGateService"/> (Task 16.1, Req 23.1–23.4, 24.1–24.5).
///
/// <para><b>Pre-approved pass (Req 23.1).</b> A resident creates a pass for their OWN unit:
/// <see cref="IAuthorizationGuard"/> re-checks <c>Create</c> on <c>VisitPass</c> + authorized
/// community (defense-in-depth), and the host unit is validated same-community. The QR token is an
/// opaque, high-entropy random string; the OTP is a short random code whose SALTED HASH is stored
/// (the plaintext is returned once, never persisted — <see cref="IPasswordHasher"/>). The validity
/// window and max-uses default from Config_Service (no hardcoding).</para>
///
/// <para><b>Verification (Req 23.3, 23.4).</b> A QR scan hits the <c>(CommunityId, QrToken)</c>
/// unique index directly (indexed, no scan). The service accepts a pass that is active, inside its
/// window and has uses remaining — incrementing <c>UseCount</c> on the TRACKED row so EF carries the
/// <c>RowVersion</c> into the UPDATE: two concurrent scans race on the row-version check and exactly
/// one wins, so a single-use pass can never be admitted twice (the loser's
/// <c>DbUpdateConcurrencyException</c> maps centrally to CONFLICT). An expired / exhausted / revoked /
/// invalid pass is rejected with the reason — never leaking which specific pass by surfacing a bare
/// "invalid".</para>
///
/// <para><b>Walk-in + approval (Req 24.1-24.5).</b> A guard registers a walk-in (<c>Create</c> on
/// <c>Visitor</c>); the category is validated against the <c>Visitor_Category</c> list; it starts
/// <c>pending</c> (entry withheld, Req 24.5) and the host unit's residents are notified via
/// Notification_Service — EnqueueAsync only Adds the outbox row, so the single
/// <c>SaveChangesAsync</c> commits the walk-in + the notifications atomically (Req 24.2). A resident
/// with <c>Approve</c> approves/rejects a pending walk-in for their own unit; the decision records
/// approver + timestamp exactly once under the visitor <c>RowVersion</c> (an invalid transition →
/// CONFLICT, Req 24.3, 24.4).</para>
/// </summary>
public sealed class GateService(
    AppDbContext db,
    IAuditService audit,
    IConfigService config,
    IClock clock,
    IPasswordHasher hasher,
    IAuthorizationGuard authorizationGuard,
    INotificationService notifications,
    ISecurityOpsService securityOps,
    IPageSizePolicy pageSizePolicy,
    IAsyncQueryExecutor executor)
    : IGateService
{
    // Allow-lists for the Task 16.4 list endpoints (Req 23.5, 24.2, 25.4). Only these fields reach the
    // query — no arbitrary client LINQ (steering: no hardcoding, injection-safe). Filters/sorts are
    // chosen to ride the entity indexes (status / category / host unit; EntryExitEvent by EntryTsUtc).
    private static readonly ListQueryConfiguration<VisitPass> VisitPassQueryConfig =
        new ListQueryConfiguration<VisitPass>()
            .WithDefaultSort(p => p.ValidUntilUtc, SortDirection.Descending)
            .Sortable("validUntilUtc", p => p.ValidUntilUtc)
            .Sortable("validFromUtc", p => p.ValidFromUtc)
            .Sortable("status", p => p.Status)
            .Sortable("visitorName", p => p.VisitorName)
            .Filterable("status", p => p.Status)
            .Filterable("hostUnitId", p => p.UnitId)
            .Searchable(p => p.VisitorName);

    private static readonly ListQueryConfiguration<Visitor> VisitorQueryConfig =
        new ListQueryConfiguration<Visitor>()
            .WithDefaultSort(v => v.Status)
            .Sortable("status", v => v.Status)
            .Sortable("category", v => v.Category)
            .Sortable("name", v => v.Name)
            .Filterable("status", v => v.Status)
            .Filterable("category", v => v.Category)
            .Filterable("hostUnitId", v => v.UnitId)
            .Searchable(v => v.Name);

    private static readonly ListQueryConfiguration<EntryExitEvent> EntryExitEventQueryConfig =
        new ListQueryConfiguration<EntryExitEvent>()
            // Newest-first chronological log by default, riding the (CommunityId, EntryTsUtc) index.
            .WithDefaultSort(e => e.EntryTsUtc, SortDirection.Descending)
            .Sortable("entryTsUtc", e => e.EntryTsUtc)
            .Sortable("exitTsUtc", e => e.ExitTsUtc)
            .Filterable("category", e => e.Category)
            .Filterable("destinationUnitId", e => e.DestinationUnitId)
            .Filterable("visitorId", e => e.VisitorId)
            // "open" filter (true = still on-site): map the client's bool to the exit-null predicate
            // via IsOpen so the entry/exit log can show currently-on-site visitors (Req 25.5).
            .Filterable("open", e => e.ExitTsUtc == null)
            .Searchable(e => e.VisitorName);

    private static readonly ListQueryConfiguration<RecurringVisitor> RecurringVisitorQueryConfig =
        new ListQueryConfiguration<RecurringVisitor>()
            .WithDefaultSort(r => r.Name)
            .Sortable("name", r => r.Name)
            .Sortable("status", r => r.Status)
            .Sortable("category", r => r.Category)
            .Filterable("status", r => r.Status)
            .Filterable("category", r => r.Category)
            .Filterable("hostUnitId", r => r.UnitId)
            .Searchable(r => r.Name);

    // Validity window / max-uses defaults are config-driven (no hardcoding, Req 23.1). These keys
    // fall back to the literal defaults below when unset in Config_Service.
    private const string DefaultValidityHoursKey = "gate.visitPass.defaultValidityHours";
    private const int DefaultValidityHours = 24;
    private const string DefaultMaxUsesKey = "gate.visitPass.defaultMaxUses";
    private const int DefaultMaxUses = 1;

    // How many QR-token generations to try before giving up on a (vanishingly unlikely) collision.
    private const int QrTokenAttempts = 5;

    public async Task<CreateVisitPassResult> CreateVisitPassAsync(CreateVisitPassRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var visitorName = RequireText(request.VisitorName, "visitorName", "A visitor name is required.");

        // Defense-in-depth (Req 23.1): a resident creates a pass for their OWN unit. The endpoint
        // gate (Task 16.4) declares [RequirePermission(Create, "VisitPass")]; the invariant must hold
        // regardless of caller — no permission → FORBIDDEN with nothing mutated.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "VisitPass", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to create a visit pass.");
        }

        // Resolve the host unit (tenant-scoped) to anchor the pass in the unit's community and run
        // the same-community/own-unit check. The resident self-scope filter (if the caller is a
        // resident) already restricts this read to their own unit, so a cross-unit host → not found.
        var communityId = await RequireHostUnitCommunityAsync(request.HostUnitId, cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(communityId);

        var now = clock.UtcNow;
        var validFrom = request.ValidFromUtc ?? now;
        var validityHours = await config.GetIntAsync(DefaultValidityHoursKey, DefaultValidityHours, communityId, cancellationToken);
        var validUntil = request.ValidUntilUtc ?? validFrom.AddHours(validityHours);
        if (validUntil <= validFrom)
        {
            throw new ValidationException("validUntilUtc", "The pass valid-until time must be after the valid-from time.");
        }

        var defaultMaxUses = await config.GetIntAsync(DefaultMaxUsesKey, DefaultMaxUses, communityId, cancellationToken);
        var maxUses = request.MaxUses is { } m && m >= 1 ? m : Math.Max(1, defaultMaxUses);

        if (request.NumberOfPersons is int persons && persons <= 0)
        {
            throw new ValidationException("numberOfPersons", "Number of persons must be greater than zero.");
        }

        // Optional vehicle type: validate against the editable Vehicle_Type list when supplied.
        if (!string.IsNullOrWhiteSpace(request.VehicleType))
        {
            var allowedVehicleTypes = await config.GetMasterDataAsync(MasterDataKeys.VehicleType, communityId, cancellationToken: cancellationToken);
            if (!allowedVehicleTypes.Any(e => string.Equals(e.Code, request.VehicleType.Trim(), StringComparison.Ordinal)))
            {
                throw new ValidationException("vehicleType", $"'{request.VehicleType}' is not a valid vehicle type.");
            }
        }

        // Short OTP shared with the visitor; only its salted hash is stored (steering: secrets).
        var otp = GenerateOtp();

        var pass = new VisitPass
        {
            CommunityId = communityId,
            HostUnitId = request.HostUnitId,
            HostResidentId = request.HostResidentId,
            VisitorName = visitorName,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            VehicleNumber = string.IsNullOrWhiteSpace(request.VehicleNumber) ? null : request.VehicleNumber.Trim(),
            VehicleType = string.IsNullOrWhiteSpace(request.VehicleType) ? null : request.VehicleType.Trim(),
            NumberOfPersons = request.NumberOfPersons,
            WhomToMeet = string.IsNullOrWhiteSpace(request.WhomToMeet) ? null : request.WhomToMeet.Trim(),
            VisitDateUtc = request.VisitDateUtc,
            PhotoFileId = request.PhotoFileId,
            OtpHash = hasher.Hash(otp),
            ValidFromUtc = validFrom,
            ValidUntilUtc = validUntil,
            MaxUses = maxUses,
            UseCount = 0,
            Status = VisitPassStatuses.Active,
        };

        // Generate an opaque, community-unique QR token. The DB unique index (CommunityId, QrToken)
        // is the source of truth: on the (astronomically unlikely) collision we regenerate and retry
        // rather than a read-then-write pre-check.
        await SaveWithUniqueQrTokenAsync(pass, cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Create", "VisitPass", pass.Id.ToString(), pass.CommunityId, Detail: null),
            cancellationToken);

        return new CreateVisitPassResult(ToDto(pass), otp);
    }

    public async Task<VerifyVisitPassResult> VerifyVisitPassAsync(VerifyVisitPassRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The guard-facing gate operation (Req 23.3): gated on Create on Visitor (the gate's capture
        // grant). Defense-in-depth on top of the Task 16.4 endpoint gate.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "Visitor", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to verify a visit pass.");
        }

        var byQr = !string.IsNullOrWhiteSpace(request.QrToken);
        var byOtp = !string.IsNullOrWhiteSpace(request.Otp);
        if (!byQr && !byOtp)
        {
            throw new ValidationException("qrToken", "A QR token or OTP is required to verify a visit pass.");
        }

        // TRACKED load (not AsNoTracking): on accept we increment UseCount and need EF to carry the
        // RowVersion concurrency token into the UPDATE (Req 23.3 — concurrency-safe use count). The
        // global tenant filter scopes the lookup to the caller's communities.
        VisitPass? pass;
        if (byQr)
        {
            var qr = request.QrToken!.Trim();
            // Rides the (CommunityId, QrToken) unique index directly.
            pass = await db.VisitPasses.FirstOrDefaultAsync(p => p.QrToken == qr, cancellationToken);
        }
        else
        {
            // OTP path: the OTP hash is salted (not equality-searchable), so load the community's
            // still-usable passes and verify in constant time. ponytail: this is an O(active-passes)
            // hash scan per OTP verify; the primary gate path is the indexed QR scan above, and OTP is
            // the manual fallback — if OTP volume ever grows, add a lookup token. Upgrade path: store a
            // separate indexed opaque OTP-lookup code alongside the hash.
            var otp = request.Otp!.Trim();
            var now0 = clock.UtcNow;
            var candidates = await db.VisitPasses
                .Where(p => p.Status == VisitPassStatuses.Active
                    && p.ValidFromUtc <= now0
                    && p.ValidUntilUtc >= now0
                    && p.UseCount < p.MaxUses)
                .ToListAsync(cancellationToken);
            pass = candidates.FirstOrDefault(p => hasher.Verify(p.OtpHash, otp));
        }

        if (pass is null)
        {
            // Unknown token/OTP (or outside the caller's tenant scope): reject without leaking which.
            return Reject("The visit pass is invalid.");
        }

        // Evaluate the pass state (Req 23.4). Order: revoked → window → uses. Expiry is derived from
        // the window (Req 23.2), not a stored flag, so a lapsed active pass still reads as expired.
        var now = clock.UtcNow;
        if (string.Equals(pass.Status, VisitPassStatuses.Revoked, StringComparison.Ordinal))
        {
            return Reject("The visit pass has been revoked.");
        }

        if (now < pass.ValidFromUtc)
        {
            return Reject("The visit pass is not yet valid.");
        }

        if (now > pass.ValidUntilUtc)
        {
            return Reject("The visit pass has expired.");
        }

        if (!string.Equals(pass.Status, VisitPassStatuses.Active, StringComparison.Ordinal) || pass.UseCount >= pass.MaxUses)
        {
            return Reject("The visit pass has already been used.");
        }

        // Accept (Req 23.3): increment the use count and mark the pass used when exhausted. The
        // RowVersion makes this concurrency-safe — a second concurrent scan loses the row-version
        // check and its save maps to CONFLICT (central mapping in AppDbContext), so a single-use pass
        // is admitted exactly once.
        pass.UseCount += 1;
        if (pass.UseCount >= pass.MaxUses)
        {
            pass.Status = VisitPassStatuses.Used;
        }

        await audit.AppendAsync(
            new AuditEntry("Verify", "VisitPass", pass.Id.ToString(), pass.CommunityId, Detail: null),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new VerifyVisitPassResult(Accepted: true, RejectionReason: null, Pass: ToDto(pass));
    }

    public async Task<VisitorDto> RegisterWalkInAsync(RegisterWalkInRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = RequireText(request.Name, "name", "A visitor name is required.");

        // Defense-in-depth (Req 24.1): a guard registers a walk-in with Create on Visitor.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "Visitor", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to register a walk-in visitor.");
        }

        var communityId = await RequireHostUnitCommunityAsync(request.HostUnitId, cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(communityId);
        await RequireValidCategoryAsync(request.Category, communityId, cancellationToken);
        await RequireValidIdProofTypeAsync(request.IdProofType, communityId, cancellationToken);

        if (request.ExpectedDurationMinutes is int duration && duration <= 0)
        {
            throw new ValidationException("expectedDurationMinutes", "Expected duration must be greater than zero.");
        }

        var visitor = new Visitor
        {
            CommunityId = communityId,
            HostUnitId = request.HostUnitId,
            Name = name,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Category = request.Category.Trim(),
            PhotoFileId = request.PhotoFileId,
            VehicleNumber = string.IsNullOrWhiteSpace(request.VehicleNumber) ? null : request.VehicleNumber.Trim(),
            GateLocation = string.IsNullOrWhiteSpace(request.GateLocation) ? null : request.GateLocation.Trim(),
            IdProofType = string.IsNullOrWhiteSpace(request.IdProofType) ? null : request.IdProofType.Trim(),
            IdProofNumber = string.IsNullOrWhiteSpace(request.IdProofNumber) ? null : request.IdProofNumber.Trim(),
            Purpose = string.IsNullOrWhiteSpace(request.Purpose) ? null : request.Purpose.Trim(),
            WhomToMeet = string.IsNullOrWhiteSpace(request.WhomToMeet) ? null : request.WhomToMeet.Trim(),
            ExpectedDurationMinutes = request.ExpectedDurationMinutes,
            // Starts pending: entry withheld until a resident approves (Req 24.3, 24.5).
            Status = VisitorStatuses.Pending,
        };

        db.Visitors.Add(visitor);

        // Watchlist flag (Req 26.5): check the walk-in's name / phone / vehicle number against the
        // community's ACTIVE watchlist. A match is FLAGGED (recorded in the audit trail so the guard
        // and the resident see it) — registration itself proceeds (the walk-in is pending anyway and
        // entry is withheld until approval). The hard override gate is enforced at RecordEntry, where
        // admitting a watchlisted subject requires the Approve permission (Req 26.5).
        var walkInMatch = await MatchWatchlistAnyAsync(
            visitor.CommunityId,
            cancellationToken,
            visitor.Name,
            visitor.Phone,
            visitor.VehicleNumber);
        if (walkInMatch)
        {
            await audit.AppendAsync(
                new AuditEntry("WatchlistFlag", "Visitor", visitor.Id.ToString(), visitor.CommunityId,
                    Detail: "Walk-in matched an active watchlist entry; entry requires an authorized override."),
                cancellationToken);
        }

        await audit.AppendAsync(
            new AuditEntry("Create", "Visitor", visitor.Id.ToString(), visitor.CommunityId, Detail: null),
            cancellationToken);

        // Notify the host unit's residents to approve/reject (Req 24.2). EnqueueAsync only Adds the
        // outbox rows to THIS DbContext (no save), so the single SaveChangesAsync below commits the
        // walk-in + the notifications atomically (transactional outbox) — a rollback queues nothing.
        await NotifyHostUnitResidentsAsync(visitor, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(visitor);
    }

    public Task<VisitorDto> ApproveWalkInAsync(Guid visitorId, Guid approvingResidentId, CancellationToken cancellationToken = default) =>
        DecideWalkInAsync(
            visitorId,
            approvingResidentId,
            toStatus: VisitorStatuses.Approved,
            auditAction: "Approve",
            detail: null,
            cancellationToken);

    public Task<VisitorDto> RejectWalkInAsync(Guid visitorId, Guid rejectingResidentId, RejectWalkInRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DecideWalkInAsync(
            visitorId,
            rejectingResidentId,
            toStatus: VisitorStatuses.Rejected,
            auditAction: "Reject",
            detail: string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            cancellationToken);
    }

    // =============================================================================================
    // Task 16.2 — recurring visitors (Req 25.2) + entry/exit capture (Req 25.3, 25.4)
    // =============================================================================================

    public async Task<RecurringVisitorDto> CreateRecurringVisitorAsync(CreateRecurringVisitorRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = RequireText(request.Name, "name", "A visitor name is required.");

        // Defense-in-depth (Req 25.2): a resident defines a recurring visitor for their OWN unit with
        // Create on RecurringVisitor. The endpoint gate (Task 16.4) declares the same requirement; the
        // invariant must hold regardless of caller — no permission → FORBIDDEN with nothing mutated.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "RecurringVisitor", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to create a recurring visitor.");
        }

        // Resolve the host unit (tenant + resident self-scope filtered) → its community anchors the
        // row and runs the own-unit/same-community check (a cross-unit host → not found).
        var communityId = await RequireHostUnitCommunityAsync(request.HostUnitId, cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(communityId);
        await RequireValidCategoryAsync(request.Category, communityId, cancellationToken);
        ValidateSchedule(request);

        var recurring = new RecurringVisitor
        {
            CommunityId = communityId,
            HostUnitId = request.HostUnitId,
            HostResidentId = request.HostResidentId,
            Name = name,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Category = request.Category.Trim(),
            DaysOfWeek = request.DaysOfWeek,
            StartDate = request.StartDate.Date,
            EndDate = request.EndDate.Date,
            DailyStartMinutes = request.DailyStartMinutes,
            DailyEndMinutes = request.DailyEndMinutes,
            Status = RecurringVisitorStatuses.Active,
        };

        db.RecurringVisitors.Add(recurring);

        await audit.AppendAsync(
            new AuditEntry("Create", "RecurringVisitor", recurring.Id.ToString(), recurring.CommunityId, Detail: null),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(recurring);
    }

    public async Task<RecurringVisitorDto> RevokeRecurringVisitorAsync(Guid recurringVisitorId, CancellationToken cancellationToken = default)
    {
        // Defense-in-depth (Req 25.2): a resident revokes their own recurring visitor with Create on
        // RecurringVisitor (the same grant that created it).
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "RecurringVisitor", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to revoke a recurring visitor.");
        }

        // TRACKED load: we mutate the row and need the RowVersion carried into the UPDATE. The global
        // tenant + resident self-scope filter restricts this to the caller's own unit, so a
        // cross-unit/foreign-tenant recurring visitor is a not-found.
        var recurring = await db.RecurringVisitors.FirstOrDefaultAsync(r => r.Id == recurringVisitorId, cancellationToken)
            ?? throw new NotFoundException("Recurring visitor not found.");

        authorizationGuard.EnsureCommunityAuthorized(recurring.CommunityId);

        // Only an active authorization can be revoked. A revoke of an already-revoked one conflicts
        // with the current state — the same thing a concurrency loser sees after the winner revoked.
        if (!recurring.IsActive)
        {
            throw new ConflictException(
                $"The recurring visitor is '{recurring.Status}', not 'active'; it cannot be revoked again.");
        }

        recurring.Status = RecurringVisitorStatuses.Revoked;

        await audit.AppendAsync(
            new AuditEntry("Revoke", "RecurringVisitor", recurring.Id.ToString(), recurring.CommunityId, Detail: null),
            cancellationToken);

        // RowVersion makes the revoke record exactly once under concurrency (loser → CONFLICT).
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(recurring);
    }

    public async Task<bool> IsRecurringAuthorizedAsync(Guid recurringVisitorId, DateTime whenUtc, CancellationToken cancellationToken = default)
    {
        // Read-only authorization check (Req 25.2): tenant-scoped AsNoTracking read; the domain rule
        // (active + in range + matching day + optional daily window) lives on the entity. A missing /
        // foreign-tenant id is simply not authorized (the global filter hides it → null → false).
        var recurring = await db.RecurringVisitors.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == recurringVisitorId, cancellationToken);

        return recurring?.IsAuthorizedOn(whenUtc) ?? false;
    }

    public async Task<EntryExitEventDto> RecordEntryAsync(RecordEntryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A guard records an entry with Create on EntryExitEvent (defense-in-depth over the Task 16.4
        // endpoint gate).
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "EntryExitEvent", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to record an entry.");
        }

        var kind = RequireExactlyOneAuthorization(request.VisitorId, request.VisitPassId, request.RecurringVisitorId);
        var now = clock.UtcNow;

        // Resolve + ENFORCE admittability for the named authorization (Req 24.4, 25.3): a non-
        // admittable authorization yields no entry event. The resolver returns the owning community
        // and the denormalized name/category captured onto the event.
        var admit = await ResolveAdmittableAuthorizationAsync(kind, request.VisitorId, request.VisitPassId, request.RecurringVisitorId, now, cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(admit.CommunityId);

        // Watchlist override gate (Req 26.5): if the admitted subject (name / vehicle number) matches
        // the community's ACTIVE watchlist, the entry is FLAGGED and may only proceed when the caller
        // holds the Approve permission on Visitor (the authorized override). Without it the entry is
        // refused — FORBIDDEN, no event created — surfacing the match to the guard. The check is a
        // single indexed watchlist lookup (no scan).
        var entryMatch = await MatchWatchlistAnyAsync(
            admit.CommunityId,
            cancellationToken,
            admit.VisitorName,
            admit.VehicleNumber);
        if (entryMatch)
        {
            await audit.AppendAsync(
                new AuditEntry("WatchlistFlag", "EntryExitEvent", null, admit.CommunityId,
                    Detail: $"Subject '{admit.VisitorName}' matched an active watchlist entry at entry."),
                cancellationToken);

            if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Approve, "Visitor", cancellationToken))
            {
                throw new ForbiddenException(
                    "The subject matches an active watchlist entry; an authorized override (Approve permission) is required to admit entry.");
            }
        }

        var evt = new EntryExitEvent
        {
            CommunityId = admit.CommunityId,
            GateId = request.GateId,
            GateLabel = string.IsNullOrWhiteSpace(request.GateLabel) ? null : request.GateLabel.Trim(),
            DestinationUnitId = admit.DestinationUnitId,
            VisitorId = request.VisitorId,
            VisitPassId = request.VisitPassId,
            RecurringVisitorId = request.RecurringVisitorId,
            VisitorName = admit.VisitorName,
            Category = admit.Category,
            VehicleNumber = string.IsNullOrWhiteSpace(request.VehicleNumber) ? admit.VehicleNumber : request.VehicleNumber.Trim(),
            EntryTsUtc = now,
            ExitTsUtc = null,
        };

        db.EntryExitEvents.Add(evt);

        await audit.AppendAsync(
            new AuditEntry("RecordEntry", "EntryExitEvent", evt.Id.ToString(), evt.CommunityId, Detail: null),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(evt);
    }

    public async Task<EntryExitEventDto> RecordExitAsync(RecordExitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "EntryExitEvent", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to record an exit.");
        }

        var kind = RequireExactlyOneAuthorization(request.VisitorId, request.VisitPassId, request.RecurringVisitorId);

        // Match the OPEN event for the named authorization (Req 25.3). TRACKED load so the RowVersion
        // rides the UPDATE — two concurrent exits race on the row-version check and exactly one wins,
        // so a double-exit can't both stamp a time (the loser → CONFLICT centrally). The lookup is a
        // single indexed query on (CommunityId, <authorization id>) filtered to the open event. If the
        // authorization has more than one open event (shouldn't happen), close the earliest entry.
        var query = db.EntryExitEvents.Where(e => e.ExitTsUtc == null);
        query = kind switch
        {
            AuthorizationKind.WalkIn => query.Where(e => e.VisitorId == request.VisitorId),
            AuthorizationKind.VisitPass => query.Where(e => e.VisitPassId == request.VisitPassId),
            _ => query.Where(e => e.RecurringVisitorId == request.RecurringVisitorId),
        };

        var open = await query.OrderBy(e => e.EntryTsUtc).FirstOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException(
                "openEvent",
                "No open entry event was found for this visitor; an exit must match an open entry.");

        authorizationGuard.EnsureCommunityAuthorized(open.CommunityId);

        open.ExitTsUtc = clock.UtcNow;
        if (request.GateId is { } gid)
        {
            open.GateId = gid;
        }

        if (!string.IsNullOrWhiteSpace(request.GateLabel))
        {
            open.GateLabel = request.GateLabel.Trim();
        }

        await audit.AppendAsync(
            new AuditEntry("RecordExit", "EntryExitEvent", open.Id.ToString(), open.CommunityId, Detail: null),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(open);
    }

    // =============================================================================================
    // Task 16.4 — paginated list/read surface (Req 23.5, 24.2, 25.4, 25.5)
    // =============================================================================================

    public Task<PagedData<VisitPassDto>> ListVisitPassesAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // Tenant + resident self-scope filtered by the global query filter (Req 23.5): a resident
        // principal only sees passes for their own unit. OTP is never on VisitPassDto (secrets).
        return db.VisitPasses.AsNoTracking().ToPagedResultAsync(
            parameters, VisitPassQueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
    }

    public async Task<VisitPassDto?> GetVisitPassAsync(Guid visitPassId, CancellationToken cancellationToken = default)
    {
        var pass = await db.VisitPasses.AsNoTracking().FirstOrDefaultAsync(p => p.Id == visitPassId, cancellationToken);
        return pass is null ? null : ToDto(pass);
    }

    public Task<PagedData<VisitorDto>> ListVisitorsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // The pending-approval queue (Req 24.2): tenant/self-scoped; a status=pending filter surfaces
        // the walk-ins a resident must approve/reject for their own unit.
        return db.Visitors.AsNoTracking().ToPagedResultAsync(
            parameters, VisitorQueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
    }

    public async Task<VisitorDto?> GetVisitorAsync(Guid visitorId, CancellationToken cancellationToken = default)
    {
        var visitor = await db.Visitors.AsNoTracking().FirstOrDefaultAsync(v => v.Id == visitorId, cancellationToken);
        return visitor is null ? null : ToDto(visitor);
    }

    public Task<PagedData<EntryExitEventDto>> ListEntryExitEventsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // The entry/exit log (Req 25.4, 25.5): tenant-scoped, newest-first by default riding the
        // (CommunityId, EntryTsUtc) index; the open filter shows currently-on-site visitors.
        return db.EntryExitEvents.AsNoTracking().ToPagedResultAsync(
            parameters, EntryExitEventQueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
    }

    public Task<PagedData<RecurringVisitorDto>> ListRecurringVisitorsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        return db.RecurringVisitors.AsNoTracking().ToPagedResultAsync(
            parameters, RecurringVisitorQueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
    }

    public async Task<RecurringVisitorDto?> GetRecurringVisitorAsync(Guid recurringVisitorId, CancellationToken cancellationToken = default)
    {
        var recurring = await db.RecurringVisitors.AsNoTracking().FirstOrDefaultAsync(r => r.Id == recurringVisitorId, cancellationToken);
        return recurring is null ? null : ToDto(recurring);
    }

    /// <summary>
    /// Shared pending → approved/rejected transition for a walk-in (Req 24.3, 24.4). Enforces the
    /// Approve permission + authorized community, that the walk-in is still pending (an invalid
    /// transition → CONFLICT — also what the concurrency loser observes), stamps the decision
    /// (resident + timestamp), audits it, and commits under the visitor RowVersion so the decision
    /// records exactly once.
    /// </summary>
    private async Task<VisitorDto> DecideWalkInAsync(
        Guid visitorId,
        Guid decidingResidentId,
        string toStatus,
        string auditAction,
        string? detail,
        CancellationToken cancellationToken)
    {
        // Defense-in-depth (Req 24.3): a resident approves/rejects with Approve on Visitor.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Approve, "Visitor", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to approve or reject a walk-in visitor.");
        }

        // TRACKED load: we mutate the row and need the RowVersion carried into the UPDATE. The global
        // tenant + resident self-scope filter restricts this to the caller's own unit, so a
        // cross-unit/foreign-tenant walk-in is a not-found.
        var visitor = await db.Visitors.FirstOrDefaultAsync(v => v.Id == visitorId, cancellationToken)
            ?? throw new NotFoundException("Visitor not found.");

        authorizationGuard.EnsureCommunityAuthorized(visitor.CommunityId);

        // Only a pending walk-in can be decided (Req 24.5). An approve/reject against a non-pending
        // one conflicts with the current state (CONFLICT) — the same thing the concurrency loser sees
        // after the winner moved the row.
        if (!string.Equals(visitor.Status, VisitorStatuses.Pending, StringComparison.Ordinal))
        {
            throw new ConflictException(
                $"The visitor is '{visitor.Status}', not 'pending'; the decision is not valid for its current status.");
        }

        visitor.Status = toStatus;
        visitor.ApprovedByResidentId = decidingResidentId;
        visitor.ApprovalDecisionAtUtc = clock.UtcNow;

        await audit.AppendAsync(
            new AuditEntry(auditAction, "Visitor", visitor.Id.ToString(), visitor.CommunityId, Detail: detail),
            cancellationToken);

        // RowVersion makes the decision record exactly once under concurrency: the loser's save trips
        // a DbUpdateConcurrencyException mapped centrally to CONFLICT (Req 24.3).
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(visitor);
    }

    /// <summary>
    /// Notify the residents of the walk-in's host unit (Req 24.2). Resolves the unit's current
    /// household residents that have a linked login user (a notification needs a recipient user) in a
    /// SINGLE tenant-scoped read (no N+1), and enqueues the visitor-request template for each. The
    /// enqueue only Adds a Pending outbox row to the current DbContext — it does not save — so the
    /// caller's SaveChangesAsync commits the walk-in + the notifications atomically.
    /// </summary>
    private async Task NotifyHostUnitResidentsAsync(Visitor visitor, CancellationToken cancellationToken)
    {
        // Current (open) household members of the host unit → their linked login user ids. One
        // projecting join, distinct, no N+1.
        var recipientUserIds = await db.HouseholdMembers.AsNoTracking()
            .Where(h => h.UnitId == visitor.HostUnitId && h.EndDate == null)
            .Join(db.Residents.AsNoTracking().Where(r => r.UserId != null),
                h => h.ResidentId,
                r => r.Id,
                (h, r) => r.UserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (recipientUserIds.Count == 0)
        {
            return;
        }

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["visitorName"] = visitor.Name,
            ["category"] = visitor.Category,
        };

        foreach (var userId in recipientUserIds)
        {
            await notifications.EnqueueAsync(
                new NotificationRequest(
                    NotificationTemplateKeys.VisitorRequest,
                    userId,
                    visitor.CommunityId,
                    Placeholders: placeholders),
                cancellationToken);
        }
    }

    /// <summary>
    /// Resolve the host unit (tenant-scoped) and return its community id, rejecting an unknown /
    /// foreign-tenant / (for a resident) non-own unit. The returned community anchors the pass/visitor
    /// so it is always owned by the same community as its host unit (same-community by construction,
    /// Req 23.1, 24.1).
    /// </summary>
    private async Task<Guid> RequireHostUnitCommunityAsync(Guid hostUnitId, CancellationToken cancellationToken)
    {
        var communityId = await db.Units.AsNoTracking()
            .Where(u => u.Id == hostUnitId)
            .Select(u => (Guid?)u.CommunityId)
            .FirstOrDefaultAsync(cancellationToken);

        return communityId ?? throw new ValidationException("hostUnitId", "The host unit was not found in your authorized communities.");
    }

    private async Task RequireValidCategoryAsync(string? category, Guid communityId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ValidationException("category", "A visitor category is required.");
        }

        // Validate against the configurable Visitor_Category list resolved for this community
        // (Req 24.1, 25.1) — not a frozen enum, so categories stay editable per community.
        var allowed = await config.GetMasterDataAsync(MasterDataKeys.VisitorCategory, communityId, cancellationToken: cancellationToken);
        if (!allowed.Any(e => string.Equals(e.Code, category.Trim(), StringComparison.Ordinal)))
        {
            throw new ValidationException("category", $"'{category}' is not a valid visitor category.");
        }
    }

    private async Task RequireValidIdProofTypeAsync(string? idProofType, Guid communityId, CancellationToken cancellationToken)
    {
        // Optional field: only validate when supplied. Captured for future reference against the
        // editable ID_Proof_Type list (steering: no hardcoding), not a frozen enum.
        if (string.IsNullOrWhiteSpace(idProofType))
        {
            return;
        }

        var allowed = await config.GetMasterDataAsync(MasterDataKeys.IdProofType, communityId, cancellationToken: cancellationToken);
        if (!allowed.Any(e => string.Equals(e.Code, idProofType.Trim(), StringComparison.Ordinal)))
        {
            throw new ValidationException("idProofType", $"'{idProofType}' is not a valid ID-proof type.");
        }
    }

    /// <summary>
    /// Add the pass with a freshly generated opaque QR token, letting the DB unique index
    /// (CommunityId, QrToken) decide the winner. On the vanishingly unlikely collision, regenerate
    /// and retry (bounded) rather than a read-then-write pre-check — the same "let the DB decide"
    /// stance as the Vehicle registration-number index.
    /// </summary>
    private async Task SaveWithUniqueQrTokenAsync(VisitPass pass, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            pass.QrToken = GenerateQrToken();
            db.VisitPasses.Add(pass);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException) when (attempt < QrTokenAttempts)
            {
                // Collision (or another unique violation on this insert): detach and retry with a new
                // token. Bounded so a persistent failure still surfaces.
                db.Entry(pass).State = EntityState.Detached;
            }
        }
    }

    private VerifyVisitPassResult Reject(string reason) =>
        new(Accepted: false, RejectionReason: reason, Pass: null);

    /// <summary>
    /// Whether ANY of the supplied subject identifiers (name / phone / vehicle number) matches the
    /// community's ACTIVE watchlist (Req 26.5). Delegates to <see cref="ISecurityOpsService"/>'s
    /// indexed match (normalized equality on <c>(CommunityId, SubjectIdentifier)</c>); null/blank
    /// identifiers are skipped. Short-circuits on the first match.
    /// </summary>
    private async Task<bool> MatchWatchlistAnyAsync(Guid communityId, CancellationToken cancellationToken, params string?[] identifiers)
    {
        foreach (var identifier in identifiers)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                continue;
            }

            var matches = await securityOps.MatchWatchlistAsync(communityId, identifier, cancellationToken);
            if (matches.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string GenerateQrToken() =>
        // 256 bits of entropy, URL-safe, opaque — reveals nothing about the pass.
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string GenerateOtp() =>
        // 6-digit numeric OTP (000000–999999), unbiased.
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static string RequireText(string? value, string field, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(field, message);
        }

        return value.Trim();
    }

    private static VisitPassDto ToDto(VisitPass p) =>
        new(p.Id, p.CommunityId, p.HostUnitId, p.HostResidentId, p.VisitorName, p.Phone,
            p.VehicleNumber, p.VehicleType, p.NumberOfPersons, p.WhomToMeet, p.VisitDateUtc, p.PhotoFileId,
            p.QrToken, p.ValidFromUtc, p.ValidUntilUtc, p.MaxUses, p.UseCount, p.Status);

    private static VisitorDto ToDto(Visitor v) =>
        new(v.Id, v.CommunityId, v.HostUnitId, v.Name, v.Phone, v.Category, v.PhotoFileId,
            v.VehicleNumber, v.GateLocation, v.IdProofType, v.IdProofNumber, v.Purpose,
            v.WhomToMeet, v.ExpectedDurationMinutes, v.Status, v.ApprovedByResidentId,
            v.ApprovalDecisionAtUtc, v.IsAdmittable);

    private static RecurringVisitorDto ToDto(RecurringVisitor r) =>
        new(r.Id, r.CommunityId, r.HostUnitId, r.HostResidentId, r.Name, r.Phone, r.Category,
            r.DaysOfWeek, r.StartDate, r.EndDate, r.DailyStartMinutes, r.DailyEndMinutes, r.Status);

    private static EntryExitEventDto ToDto(EntryExitEvent e) =>
        new(e.Id, e.CommunityId, e.GateId, e.GateLabel, e.DestinationUnitId, e.VisitorId,
            e.VisitPassId, e.RecurringVisitorId, e.VisitorName, e.Category, e.VehicleNumber,
            e.EntryTsUtc, e.ExitTsUtc);

    /// <summary>Which authorization an entry/exit request names (exactly one).</summary>
    private enum AuthorizationKind
    {
        WalkIn,
        VisitPass,
        Recurring,
    }

    /// <summary>
    /// The resolved, admittable authorization behind an entry (Req 25.3): the owning community and the
    /// denormalized details captured onto the event.
    /// </summary>
    private sealed record AdmittableAuthorization(
        Guid CommunityId,
        string VisitorName,
        string Category,
        string? VehicleNumber,
        Guid? DestinationUnitId);

    /// <summary>
    /// Require that an entry/exit request names EXACTLY ONE authorization (walk-in / pass / recurring)
    /// — zero or more than one is a validation error — and return which one.
    /// </summary>
    private static AuthorizationKind RequireExactlyOneAuthorization(Guid? visitorId, Guid? visitPassId, Guid? recurringVisitorId)
    {
        var count = (visitorId is not null ? 1 : 0) + (visitPassId is not null ? 1 : 0) + (recurringVisitorId is not null ? 1 : 0);
        if (count != 1)
        {
            throw new ValidationException(
                "authorization",
                "Exactly one of visitorId, visitPassId or recurringVisitorId must identify the admitting authorization.");
        }

        if (visitorId is not null)
        {
            return AuthorizationKind.WalkIn;
        }

        return visitPassId is not null ? AuthorizationKind.VisitPass : AuthorizationKind.Recurring;
    }

    /// <summary>
    /// Resolve the named authorization and ENFORCE admittability for an entry (Req 24.4, 25.3). A
    /// walk-in must be <c>approved</c> (<see cref="Visitor.IsAdmittable"/> — a pending/rejected one is
    /// rejected); a pass must be active, inside its validity window and have uses remaining; a
    /// recurring visitor must be authorized for <paramref name="whenUtc"/>
    /// (<see cref="RecurringVisitor.IsAuthorizedOn"/>). A missing/foreign-tenant authorization (hidden
    /// by the global filter) or a non-admittable one throws a ValidationException so NO entry event is
    /// created. Reads are tenant-scoped <c>AsNoTracking</c> single indexed lookups (no N+1).
    /// </summary>
    private async Task<AdmittableAuthorization> ResolveAdmittableAuthorizationAsync(
        AuthorizationKind kind,
        Guid? visitorId,
        Guid? visitPassId,
        Guid? recurringVisitorId,
        DateTime whenUtc,
        CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case AuthorizationKind.WalkIn:
            {
                var v = await db.Visitors.AsNoTracking().FirstOrDefaultAsync(x => x.Id == visitorId, cancellationToken)
                    ?? throw new ValidationException("visitorId", "The visitor was not found in your authorized communities.");
                if (!v.IsAdmittable)
                {
                    // Req 24.4 / 25.3: a pending or rejected walk-in is NOT admittable — no entry event.
                    throw new ValidationException("visitorId", $"The visitor is '{v.Status}' and cannot be admitted.");
                }

                return new AdmittableAuthorization(v.CommunityId, v.Name, v.Category, v.VehicleNumber, v.UnitId);
            }

            case AuthorizationKind.VisitPass:
            {
                var p = await db.VisitPasses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == visitPassId, cancellationToken)
                    ?? throw new ValidationException("visitPassId", "The visit pass was not found in your authorized communities.");
                if (!IsPassAdmittable(p, whenUtc))
                {
                    throw new ValidationException("visitPassId", "The visit pass is not valid for entry (expired, used or revoked).");
                }

                return new AdmittableAuthorization(p.CommunityId, p.VisitorName, Category: "guest", VehicleNumber: null, p.UnitId);
            }

            default:
            {
                var r = await db.RecurringVisitors.AsNoTracking().FirstOrDefaultAsync(x => x.Id == recurringVisitorId, cancellationToken)
                    ?? throw new ValidationException("recurringVisitorId", "The recurring visitor was not found in your authorized communities.");
                if (!r.IsAuthorizedOn(whenUtc))
                {
                    // Req 25.2 / 25.3: outside the schedule or revoked → not authorized for entry now.
                    throw new ValidationException("recurringVisitorId", "The recurring visitor is not authorized to enter at this time.");
                }

                return new AdmittableAuthorization(r.CommunityId, r.Name, r.Category, VehicleNumber: null, r.UnitId);
            }
        }
    }

    /// <summary>
    /// Whether a pass is valid for a gate entry right now (Req 23.4 rule reused for entry capture):
    /// active, within its validity window and with uses remaining. Mirrors the verify-time acceptance
    /// test; entry capture is a read-only admittability check (it does not itself consume a use —
    /// gate verification does that).
    /// </summary>
    private static bool IsPassAdmittable(VisitPass p, DateTime whenUtc) =>
        string.Equals(p.Status, VisitPassStatuses.Active, StringComparison.Ordinal)
        && whenUtc >= p.ValidFromUtc
        && whenUtc <= p.ValidUntilUtc
        && p.UseCount < p.MaxUses;

    /// <summary>
    /// Validate a recurring-visitor schedule (Req 25.2): at least one authorized day, a valid date
    /// range (end on/after start), and — when a daily window is supplied — well-formed, in-bounds
    /// minutes with start before end.
    /// </summary>
    private static void ValidateSchedule(CreateRecurringVisitorRequest request)
    {
        if (request.DaysOfWeek is <= 0 or > 0b1111111)
        {
            throw new ValidationException("daysOfWeek", "At least one valid day-of-week must be selected.");
        }

        if (request.EndDate.Date < request.StartDate.Date)
        {
            throw new ValidationException("endDate", "The end date must be on or after the start date.");
        }

        var hasStart = request.DailyStartMinutes is not null;
        var hasEnd = request.DailyEndMinutes is not null;
        if (hasStart != hasEnd)
        {
            throw new ValidationException("dailyWindow", "A daily time window needs both a start and an end, or neither.");
        }

        if (hasStart)
        {
            var start = request.DailyStartMinutes!.Value;
            var end = request.DailyEndMinutes!.Value;
            if (start is < 0 or > 1439 || end is < 1 or > 1440 || start >= end)
            {
                throw new ValidationException("dailyWindow", "The daily time window must be within a day and start before it ends.");
            }
        }
    }
}
