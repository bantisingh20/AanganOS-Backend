using UrbanNest.Application.Abstractions;
using UrbanNest.Application.Abstractions.Security;
using UrbanNest.Application.Audit;
using UrbanNest.Application.Config;
using UrbanNest.Application.Exceptions;
using UrbanNest.Application.Notifications;
using UrbanNest.Application.Parking;
using UrbanNest.Domain.Identity;
using UrbanNest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using DomainParkingAssignment = UrbanNest.Domain.Parking.ParkingAssignment;
using DomainParkingAssignmentTypes = UrbanNest.Domain.Parking.ParkingAssignmentTypes;
using DomainParkingViolation = UrbanNest.Domain.Parking.ParkingViolation;
using DomainParkingViolationStatuses = UrbanNest.Domain.Parking.ParkingViolationStatuses;
using DomainParkingZone = UrbanNest.Domain.Parking.ParkingZone;
using DomainParkingSlot = UrbanNest.Domain.Residents.ParkingSlot;
using DomainParkingSlotTypes = UrbanNest.Domain.Residents.ParkingSlotTypes;
using DomainEvReservation = UrbanNest.Domain.Parking.EvReservation;
using DomainEvReservationStatuses = UrbanNest.Domain.Parking.EvReservationStatuses;
using DomainVisitorStatuses = UrbanNest.Domain.Gate.VisitorStatuses;

namespace UrbanNest.Infrastructure.Parking;

/// <summary>
/// EF-backed <see cref="IParkingService"/> (Task 24.1, Req 40.1–40.5): the full parking module built
/// on the minimal Phase 2 <c>ParkingSlot</c> association (Task 12.5), which it evolves rather than
/// duplicates — the layout (zones + slots), slot assignment with the DB-level one-active-assignment-
/// per-slot guarantee, violations (File_Service evidence + resident notice) and the tenant-scoped
/// occupancy dashboard. Guest parking / EV (24.2) and the HTTP controllers (24.3) extend this seam.
///
/// <para><b>Pattern parity with <c>AmenityService</c> / <c>UnitHistoryService</c>.</b> Every mutation
/// checks the resource permission (defense-in-depth) + community authorization, validates configurable
/// codes against the editable master-data lists (no frozen enums), audits via <see cref="IAuditService"/>,
/// and lets DB unique indexes be the source of truth (zone/slot code uniqueness and — the headline —
/// one active assignment per slot → CONFLICT via <c>UniqueConstraintGuard</c>, never a read-then-write
/// gap). Reads are tenant-scoped by the global filter (entities are NOT <c>IUnitScoped</c> — community
/// infrastructure), <c>AsNoTracking</c>.</para>
///
/// <para><b>One active assignment per slot (Req 40.3).</b> The append-only
/// <c>ParkingAssignment</c> register carries an active row (EndedAtUtc == null) per current
/// assignment. <see cref="AssignSlotAsync"/> closes the prior active row and inserts the new one in a
/// single transaction, mirroring the one-open-owner <c>UnitHistoryService</c>. On SQL Server the
/// filtered unique index on <c>(ParkingSlotId) WHERE EndedAtUtc IS NULL</c> makes two concurrent
/// assignments of the SAME slot resolve to exactly one winner — the loser's unique violation is
/// translated to CONFLICT. On the SQLite test provider the invariant rests on the single transaction
/// (documented caveat on <c>ParkingAssignmentEntityConfiguration</c>).</para>
/// </summary>
public sealed class ParkingService(
    AppDbContext db,
    IAuditService audit,
    IConfigService config,
    IClock clock,
    IAuthorizationGuard authorizationGuard,
    INotificationService notifications)
    : IParkingService
{
    private const string DuplicateZoneCodeMessage =
        "A parking zone with the same code already exists in this community.";
    private const string DuplicateSlotCodeMessage =
        "A parking slot with the same code already exists in this community.";
    private const string ActiveAssignmentConflictMessage =
        "The parking slot was assigned by another request. Reload and try again.";
    private const string ActiveEvReservationConflictMessage =
        "The EV charging slot was reserved by another request. Reload and try again.";

    // Seeded fallbacks for the Task 24.2 guest-parking / EV configurable rules when the Config_Service
    // keys are unset (Req 41.1, 41.2) — safe defaults, not hardcoded decisions: a community overrides
    // each key to tune the window/cap without a code change (steering: no hardcoding).
    private const int DefaultGuestParkingMaxDurationHours = 24;
    private const int DefaultGuestParkingMaxConcurrentPerUnit = 2;
    private const int DefaultEvReservationMaxDurationHours = 6;
    private const int DefaultEvReservationMaxConcurrentPerUnit = 1;

    // =============================================================================================
    // Layout: zones (Req 40.1).
    // =============================================================================================

    public async Task<ParkingZoneDto> CreateZoneAsync(CreateParkingZoneRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "ParkingZone", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to create a parking zone.");
        }

        authorizationGuard.EnsureCommunityAuthorized(request.CommunityId);

        var code = RequireText(request.Code, "code", "A parking zone code is required.");
        var name = RequireText(request.Name, "name", "A parking zone name is required.");
        var zoneType = await RequireValidCodeAsync(
            request.ZoneType, MasterDataKeys.ParkingZoneType, request.CommunityId, "zoneType", "parking zone type", cancellationToken);

        var zone = new DomainParkingZone
        {
            CommunityId = request.CommunityId,
            Code = code,
            Name = name,
            ZoneType = zoneType,
            Level = Trimmed(request.Level),
        };

        db.ParkingZones.Add(zone);

        await audit.AppendAsync(
            new AuditEntry("Create", "ParkingZone", zone.Id.ToString(), zone.CommunityId, Detail: $"code={code}; type={zoneType}"),
            cancellationToken);

        // The DB Unique(CommunityId, Code) index is the source of truth: a duplicate → CONFLICT.
        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), DuplicateZoneCodeMessage);

        return ToDto(zone);
    }

    public async Task<IReadOnlyList<ParkingZoneDto>> ListZonesAsync(Guid communityId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "ParkingZone", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view parking zones.");
        }

        // Tenant-scoped AsNoTracking read: a foreign-tenant community's zones are hidden by the global
        // filter, so an unknown/foreign id yields an empty list. Ordered by code (Req 40.1).
        var zones = await db.ParkingZones.AsNoTracking()
            .Where(z => z.CommunityId == communityId)
            .OrderBy(z => z.Code)
            .ToListAsync(cancellationToken);

        return zones.Select(ToDto).ToList();
    }

    // =============================================================================================
    // Layout: slots (Req 40.1).
    // =============================================================================================

    public async Task<ParkingSlotDetailDto> CreateSlotAsync(CreateParkingSlotLayoutRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "ParkingSlot", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to create a parking slot.");
        }

        authorizationGuard.EnsureCommunityAuthorized(request.CommunityId);

        var code = RequireText(request.Code, "code", "A parking slot code is required.");
        var slotType = await RequireValidCodeAsync(
            request.SlotType, MasterDataKeys.ParkingSlotType, request.CommunityId, "slotType", "parking slot type", cancellationToken);

        if (request.ZoneId is { } zoneId)
        {
            await RequireZoneInCommunityAsync(zoneId, request.CommunityId, cancellationToken);
        }

        var slot = new DomainParkingSlot
        {
            CommunityId = request.CommunityId,
            Code = code,
            ZoneId = request.ZoneId,
            Level = Trimmed(request.Level),
            SlotNumber = Trimmed(request.SlotNumber),
            SlotType = slotType,
            IsActive = true,
        };

        db.ParkingSlots.Add(slot);

        await audit.AppendAsync(
            new AuditEntry("Create", "ParkingSlot", slot.Id.ToString(), slot.CommunityId, Detail: $"code={code}; type={slotType}"),
            cancellationToken);

        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), DuplicateSlotCodeMessage);

        // Freshly created → no active assignment yet.
        return ToDto(slot, activeUnitId: null, activeVisitorId: null, isOccupied: false);
    }

    public async Task<ParkingSlotDetailDto> UpdateSlotAsync(Guid parkingSlotId, UpdateParkingSlotLayoutRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "ParkingSlot", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to edit a parking slot.");
        }

        // TRACKED load so the RowVersion rides the UPDATE. The tenant filter hides foreign/archived
        // rows → not found.
        var slot = await db.ParkingSlots.FirstOrDefaultAsync(s => s.Id == parkingSlotId, cancellationToken)
            ?? throw new NotFoundException("Parking slot not found.");

        authorizationGuard.EnsureCommunityAuthorized(slot.CommunityId);

        if (request.ClearZone)
        {
            slot.ZoneId = null;
        }
        else if (request.ZoneId is { } zoneId)
        {
            await RequireZoneInCommunityAsync(zoneId, slot.CommunityId, cancellationToken);
            slot.ZoneId = zoneId;
        }

        if (request.Level is not null)
        {
            slot.Level = Trimmed(request.Level);
        }

        if (request.SlotNumber is not null)
        {
            slot.SlotNumber = Trimmed(request.SlotNumber);
        }

        if (request.SlotType is not null)
        {
            slot.SlotType = await RequireValidCodeAsync(
                request.SlotType, MasterDataKeys.ParkingSlotType, slot.CommunityId, "slotType", "parking slot type", cancellationToken);
        }

        if (request.IsActive is { } isActive)
        {
            slot.IsActive = isActive;
        }

        await audit.AppendAsync(
            new AuditEntry("Update", "ParkingSlot", slot.Id.ToString(), slot.CommunityId, Detail: null),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        var (unitId, visitorId) = await ResolveActiveAssignmentAsync(slot.Id, cancellationToken);
        return ToDto(slot, unitId, visitorId, isOccupied: unitId is not null || visitorId is not null);
    }

    public async Task<ParkingSlotDetailDto?> GetSlotAsync(Guid parkingSlotId, CancellationToken cancellationToken = default)
    {
        var slot = await db.ParkingSlots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == parkingSlotId, cancellationToken);
        if (slot is null)
        {
            return null;
        }

        var (unitId, visitorId) = await ResolveActiveAssignmentAsync(slot.Id, cancellationToken);
        return ToDto(slot, unitId, visitorId, isOccupied: unitId is not null || visitorId is not null);
    }

    public async Task<IReadOnlyList<ParkingSlotDetailDto>> ListSlotsAsync(Guid communityId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "ParkingSlot", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view parking slots.");
        }

        // Tenant-scoped read riding the (CommunityId, ZoneId, Level) layout index. The active
        // assignment is projected with a correlated subquery over the active-row set so the whole
        // list resolves in the DATABASE in one query — no N+1 per-slot assignment lookup (Req 40.2).
        var rows = await db.ParkingSlots.AsNoTracking()
            .Where(s => s.CommunityId == communityId)
            .OrderBy(s => s.ZoneId).ThenBy(s => s.Level).ThenBy(s => s.Code)
            .Select(s => new
            {
                Slot = s,
                Active = db.ParkingAssignments
                    .Where(a => a.ParkingSlotId == s.Id && a.EndedAtUtc == null)
                    .Select(a => new { a.UnitId, a.VisitorId })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => ToDto(r.Slot, r.Active?.UnitId, r.Active?.VisitorId, isOccupied: r.Active != null))
            .ToList();
    }

    // =============================================================================================
    // Assignment: one-active-per-slot (Req 40.2, 40.3).
    // =============================================================================================

    public async Task<ParkingAssignmentDto> AssignSlotAsync(AssignParkingSlotRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Assign, "ParkingAssignment", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to assign a parking slot.");
        }

        // Exactly one subject (Req 40.2): a resident unit (permanent/temporary) OR a visitor.
        if (request.UnitId is null == (request.VisitorId is null))
        {
            throw new ValidationException("unitId", "Provide exactly one of a unit or a visitor to assign the slot to.");
        }

        // Load the slot tenant-scoped (the global filter hides foreign/archived rows → not found).
        var slot = await db.ParkingSlots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.ParkingSlotId, cancellationToken)
            ?? throw new NotFoundException("Parking slot not found.");

        authorizationGuard.EnsureCommunityAuthorized(slot.CommunityId);

        if (!slot.IsActive)
        {
            throw new ValidationException("parkingSlotId", "The parking slot is not active and cannot be assigned.");
        }

        var assignmentType = await RequireValidCodeAsync(
            request.AssignmentType, MasterDataKeys.ParkingAssignmentType, slot.CommunityId, "assignmentType", "parking assignment type", cancellationToken);

        // Same-community subject validation (Req 40.2).
        if (request.UnitId is { } unitId)
        {
            await RequireUnitInCommunityAsync(unitId, slot.CommunityId, cancellationToken);
        }
        else if (request.VisitorId is { } visitorId)
        {
            await RequireVisitorInCommunityAsync(visitorId, slot.CommunityId, cancellationToken);
        }

        var now = clock.UtcNow;

        // Close the slot's prior ACTIVE assignment (if any) and append the new one in ONE transaction
        // (Req 40.3): a slot never has two active rows. TRACKED load of the open row so its RowVersion
        // rides the close UPDATE (a concurrent release/reassign loser → CONFLICT centrally).
        var openRow = await db.ParkingAssignments
            .FirstOrDefaultAsync(a => a.ParkingSlotId == slot.Id && a.EndedAtUtc == null, cancellationToken);
        if (openRow is not null)
        {
            openRow.EndedAtUtc = now;
        }

        var assignment = new DomainParkingAssignment
        {
            CommunityId = slot.CommunityId,
            ParkingSlotId = slot.Id,
            UnitId = request.UnitId,
            VisitorId = request.VisitorId,
            AssignmentType = assignmentType,
            StartedAtUtc = now,
            EndedAtUtc = null,
        };

        db.ParkingAssignments.Add(assignment);

        await audit.AppendAsync(
            new AuditEntry("Assign", "ParkingAssignment", assignment.Id.ToString(), assignment.CommunityId,
                Detail: $"slotId={slot.Id}; type={assignmentType}; unitId={request.UnitId}; visitorId={request.VisitorId}"),
            cancellationToken);

        // The DB filtered unique index on (ParkingSlotId) WHERE EndedAtUtc IS NULL is the source of
        // truth for one-active-per-slot (Req 40.3): two concurrent assignments of the SAME slot race
        // to it and exactly one wins; the loser's unique violation → CONFLICT (no read-then-write).
        // The close+insert commit atomically so the slot never has two active rows.
        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), ActiveAssignmentConflictMessage);

        return ToDto(assignment);
    }

    public async Task ReleaseSlotAsync(Guid parkingSlotId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Assign, "ParkingAssignment", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to release a parking slot.");
        }

        // TRACKED load of the active row so its RowVersion rides the close UPDATE (a concurrent
        // double-release loser → CONFLICT centrally). The tenant filter scopes it to the caller's
        // communities. No active row (unknown/foreign slot, or already free) → not found.
        var openRow = await db.ParkingAssignments
            .FirstOrDefaultAsync(a => a.ParkingSlotId == parkingSlotId && a.EndedAtUtc == null, cancellationToken)
            ?? throw new NotFoundException("No active assignment was found for the parking slot.");

        authorizationGuard.EnsureCommunityAuthorized(openRow.CommunityId);

        openRow.EndedAtUtc = clock.UtcNow;

        await audit.AppendAsync(
            new AuditEntry("Release", "ParkingAssignment", openRow.Id.ToString(), openRow.CommunityId,
                Detail: $"slotId={parkingSlotId}"),
            cancellationToken);

        // RowVersion makes the release record exactly once (a loser → CONFLICT centrally).
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ParkingAssignmentDto>> ListAssignmentsForSlotAsync(Guid parkingSlotId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "ParkingAssignment", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view parking assignments.");
        }

        // Tenant-scoped AsNoTracking read riding the (CommunityId, ParkingSlotId, StartedAtUtc) index,
        // newest first; a foreign/unknown slot's rows are hidden by the global filter → empty list.
        var rows = await db.ParkingAssignments.AsNoTracking()
            .Where(a => a.ParkingSlotId == parkingSlotId)
            .OrderByDescending(a => a.StartedAtUtc).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDto).ToList();
    }

    // =============================================================================================
    // Violations (Req 40.4).
    // =============================================================================================

    public async Task<ParkingViolationDto> RecordViolationAsync(RecordParkingViolationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "ParkingViolation", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to record a parking violation.");
        }

        var slot = await db.ParkingSlots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.ParkingSlotId, cancellationToken)
            ?? throw new NotFoundException("Parking slot not found.");

        authorizationGuard.EnsureCommunityAuthorized(slot.CommunityId);

        var violationType = await RequireValidCodeAsync(
            request.ViolationType, MasterDataKeys.ParkingViolationType, slot.CommunityId, "violationType", "parking violation type", cancellationToken);
        var description = RequireText(request.Description, "description", "A violation description is required.");

        // Validate the offending vehicle (when supplied) belongs to the slot's community (Req 40.4).
        if (request.VehicleId is { } vehicleId)
        {
            await RequireVehicleInCommunityAsync(vehicleId, slot.CommunityId, cancellationToken);
        }

        // Resolve the responsible resident unit from the slot's ACTIVE assignment (Req 40.4) so the
        // right residents are notified; null when the slot is unassigned or assigned to a visitor.
        var (activeUnitId, _) = await ResolveActiveAssignmentAsync(slot.Id, cancellationToken);

        var violation = new DomainParkingViolation
        {
            CommunityId = slot.CommunityId,
            ParkingSlotId = slot.Id,
            VehicleId = request.VehicleId,
            UnitId = activeUnitId,
            ViolationType = violationType,
            Description = description,
            EvidenceFileId = request.EvidenceFileId,
            Status = DomainParkingViolationStatuses.Open,
            RecordedAtUtc = clock.UtcNow,
        };

        db.ParkingViolations.Add(violation);

        await audit.AppendAsync(
            new AuditEntry("Create", "ParkingViolation", violation.Id.ToString(), violation.CommunityId,
                Detail: $"slotId={slot.Id}; type={violationType}"),
            cancellationToken);

        // Notify the responsible unit's residents (Req 40.4). EnqueueAsync only Adds Pending outbox
        // rows to THIS DbContext (no save), so the single SaveChanges below commits the violation +
        // audit + notifications atomically (transactional outbox).
        await EnqueueViolationNoticeAsync(violation, slot.Code, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(violation);
    }

    public async Task<ParkingViolationDto> UpdateViolationStatusAsync(Guid violationId, UpdateParkingViolationStatusRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "ParkingViolation", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to edit a parking violation.");
        }

        var violation = await db.ParkingViolations.FirstOrDefaultAsync(v => v.Id == violationId, cancellationToken)
            ?? throw new NotFoundException("Parking violation not found.");

        authorizationGuard.EnsureCommunityAuthorized(violation.CommunityId);

        // Validate the new status as a member of the editable list (no transition allow-list for the
        // violation lifecycle — the requirement needs a tracked state, Req 40.4).
        violation.Status = await RequireValidCodeAsync(
            request.Status, MasterDataKeys.ParkingViolationStatus, violation.CommunityId, "status", "parking violation status", cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Update", "ParkingViolation", violation.Id.ToString(), violation.CommunityId,
                Detail: $"status={violation.Status}"),
            cancellationToken);

        // RowVersion makes the status change record exactly once under concurrency (a loser → CONFLICT).
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(violation);
    }

    public async Task<IReadOnlyList<ParkingViolationDto>> ListViolationsForSlotAsync(Guid parkingSlotId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "ParkingViolation", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view parking violations.");
        }

        var rows = await db.ParkingViolations.AsNoTracking()
            .Where(v => v.ParkingSlotId == parkingSlotId)
            .OrderByDescending(v => v.RecordedAtUtc).ThenBy(v => v.Id)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDto).ToList();
    }

    // =============================================================================================
    // Occupancy dashboard (Req 40.5).
    // =============================================================================================

    public async Task<ParkingOccupancyDto> GetOccupancyAsync(Guid communityId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "ParkingSlot", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view parking occupancy.");
        }

        authorizationGuard.EnsureCommunityAuthorized(communityId);

        // Compute the per-(zone, level) free/occupied buckets in the DATABASE (Req 40.5): a grouped
        // projection over the community's ACTIVE slots, each tagged with whether it has an active
        // assignment via a correlated EXISTS subquery, then GroupBy(zone, level) with Count/Sum. No
        // in-memory table scan — the whole aggregate runs server-side (tenant-scoped by the global
        // filter so an unauthorized community returns nothing). Inactive slots are excluded.
        var buckets = await db.ParkingSlots.AsNoTracking()
            .Where(s => s.CommunityId == communityId && s.IsActive)
            .Select(s => new
            {
                s.ZoneId,
                s.Level,
                Occupied = db.ParkingAssignments.Any(a => a.ParkingSlotId == s.Id && a.EndedAtUtc == null),
            })
            .GroupBy(x => new { x.ZoneId, x.Level })
            .Select(g => new ParkingOccupancyBucketDto(
                g.Key.ZoneId,
                g.Key.Level,
                g.Count(),
                g.Count(x => x.Occupied),
                g.Count() - g.Count(x => x.Occupied)))
            .ToListAsync(cancellationToken);

        var totalSlots = buckets.Sum(b => b.TotalSlots);
        var occupiedSlots = buckets.Sum(b => b.OccupiedSlots);

        return new ParkingOccupancyDto(
            communityId,
            totalSlots,
            occupiedSlots,
            totalSlots - occupiedSlots,
            buckets);
    }

    // =============================================================================================
    // Guest parking (Task 24.2, Req 41.1).
    // =============================================================================================

    public async Task<ParkingAssignmentDto> RequestGuestParkingAsync(RequestGuestParkingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Guest parking IS a visitor-type ParkingAssignment, so it reuses the ParkingAssignment Assign
        // grant + the one-active-per-slot register — no second mechanism (Req 41.1).
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Assign, "ParkingAssignment", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to request guest parking.");
        }

        if (request.VisitorId == Guid.Empty)
        {
            throw new ValidationException("visitorId", "A visitor is required for guest parking.");
        }

        if (request.UnitId == Guid.Empty)
        {
            throw new ValidationException("unitId", "A requesting unit is required for guest parking.");
        }

        // Load the slot tenant-scoped (the global filter hides foreign/archived rows → not found).
        var slot = await db.ParkingSlots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.ParkingSlotId, cancellationToken)
            ?? throw new NotFoundException("Parking slot not found.");

        authorizationGuard.EnsureCommunityAuthorized(slot.CommunityId);

        if (!slot.IsActive)
        {
            throw new ValidationException("parkingSlotId", "The parking slot is not active and cannot be assigned.");
        }

        // Guest parking draws from the VISITOR pool (Req 41.1): only a visitor-type slot is eligible.
        if (!string.Equals(slot.SlotType, DomainParkingSlotTypes.Visitor, StringComparison.Ordinal))
        {
            throw new ValidationException("parkingSlotId", "Guest parking requires a visitor-type parking slot.");
        }

        // The visitor must be in the slot's community AND approved — an approved walk-in is admittable
        // (Req 41.1 "approved Visitor", honouring the Task 16.1 approval gate, Req 24.3).
        var visitor = await db.Visitors.AsNoTracking().FirstOrDefaultAsync(v => v.Id == request.VisitorId, cancellationToken);
        if (visitor is null || visitor.CommunityId != slot.CommunityId)
        {
            throw new ValidationException("visitorId", "The visitor must belong to the same community as the slot.");
        }

        if (!visitor.IsAdmittable)
        {
            throw new ValidationException("visitorId", "The visitor must be approved before guest parking can be assigned.");
        }

        // The requesting unit must be in the slot's community (Req 41.1).
        await RequireUnitInCommunityAsync(request.UnitId, slot.CommunityId, cancellationToken);

        // Per-unit concurrent-guest-slot cap (Req 41.1): count the unit's live (active) visitor
        // assignments in the DATABASE and reject when the configurable cap is reached — no in-memory
        // scan, no N+1. The cap is Config_Service-resolved (platform fallback), never hardcoded.
        var maxConcurrent = await config.GetIntAsync(
            ConfigKeys.GuestParkingMaxConcurrentPerUnit, DefaultGuestParkingMaxConcurrentPerUnit, slot.CommunityId, cancellationToken);
        if (maxConcurrent <= 0)
        {
            maxConcurrent = DefaultGuestParkingMaxConcurrentPerUnit;
        }

        var activeGuestSlots = await db.ParkingAssignments.AsNoTracking()
            .CountAsync(
                a => a.UnitId == request.UnitId
                    && a.VisitorId != null
                    && a.AssignmentType == DomainParkingAssignmentTypes.Visitor
                    && a.EndedAtUtc == null,
                cancellationToken);

        if (activeGuestSlots >= maxConcurrent)
        {
            throw new ValidationException("unitId", "The unit has reached its maximum number of concurrent guest parking slots.");
        }

        // Time-bound the guest assignment from the configurable max guest duration (Req 41.1).
        var maxDurationHours = await config.GetIntAsync(
            ConfigKeys.GuestParkingMaxDurationHours, DefaultGuestParkingMaxDurationHours, slot.CommunityId, cancellationToken);
        if (maxDurationHours <= 0)
        {
            maxDurationHours = DefaultGuestParkingMaxDurationHours;
        }

        var now = clock.UtcNow;
        var validUntil = now.AddHours(maxDurationHours);

        // Append the visitor assignment on the one-active-per-slot register (Req 41.1, reusing the
        // 24.1 mechanism): close any prior active row and insert the new one in ONE transaction. The
        // UnitId records the requesting unit (for the per-unit cap + notification), the VisitorId the
        // guest. Both unit + visitor on a guest row is intentional here (the 24.1 Assign path sets
        // exactly one; guest parking is the visitor assignment that also records the sponsoring unit).
        var openRow = await db.ParkingAssignments
            .FirstOrDefaultAsync(a => a.ParkingSlotId == slot.Id && a.EndedAtUtc == null, cancellationToken);
        if (openRow is not null)
        {
            openRow.EndedAtUtc = now;
        }

        var assignment = new DomainParkingAssignment
        {
            CommunityId = slot.CommunityId,
            ParkingSlotId = slot.Id,
            UnitId = request.UnitId,
            VisitorId = request.VisitorId,
            AssignmentType = DomainParkingAssignmentTypes.Visitor,
            StartedAtUtc = now,
            EndedAtUtc = null,
        };

        db.ParkingAssignments.Add(assignment);

        await audit.AppendAsync(
            new AuditEntry("Assign", "ParkingAssignment", assignment.Id.ToString(), assignment.CommunityId,
                Detail: $"guest; slotId={slot.Id}; visitorId={request.VisitorId}; unitId={request.UnitId}; validUntil={validUntil:O}"),
            cancellationToken);

        // Notify the requesting resident (Req 41.1). EnqueueAsync only Adds Pending outbox rows to
        // THIS DbContext (no save), so the single SaveChanges below commits assignment + audit +
        // notification atomically (transactional outbox).
        await EnqueueResidentNoticeAsync(
            request.RequestedByResidentId, NotificationTemplateKeys.GuestParkingAssigned, slot.CommunityId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["slotCode"] = slot.Code,
                ["visitorName"] = visitor.Name,
                ["validUntil"] = validUntil.ToString("yyyy-MM-dd HH:mm'Z'"),
            },
            cancellationToken);

        // The DB filtered unique index on (ParkingSlotId) WHERE EndedAtUtc IS NULL is the source of
        // truth for one-active-per-slot (Req 41.1 reuses Req 40.3): two concurrent grabs of the SAME
        // slot race to it and exactly one wins; the loser's unique violation → CONFLICT (no
        // read-then-write). The close+insert commit atomically so the slot never has two active rows.
        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), ActiveAssignmentConflictMessage);

        return ToDto(assignment);
    }

    // =============================================================================================
    // EV charging reservations (Task 24.2, Req 41.2, 41.3).
    // =============================================================================================

    public async Task<EvReservationDto> ReserveEvSlotAsync(ReserveEvSlotRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "EvReservation", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to reserve an EV charging slot.");
        }

        if (request.ResidentId == Guid.Empty)
        {
            throw new ValidationException("residentId", "A reserving resident is required.");
        }

        if (request.UnitId == Guid.Empty)
        {
            throw new ValidationException("unitId", "A reserving unit is required.");
        }

        if (request.ReservedUntilUtc <= request.ReservedFromUtc)
        {
            throw new ValidationException("reservedUntilUtc", "The reservation end must be after its start.");
        }

        // Load the slot tenant-scoped (the global filter hides foreign/archived rows → not found).
        var slot = await db.ParkingSlots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.ParkingSlotId, cancellationToken)
            ?? throw new NotFoundException("Parking slot not found.");

        authorizationGuard.EnsureCommunityAuthorized(slot.CommunityId);

        if (!slot.IsActive)
        {
            throw new ValidationException("parkingSlotId", "The parking slot is not active and cannot be reserved.");
        }

        // EV reservations only apply to an EV-charging slot (Req 41.2).
        if (!string.Equals(slot.SlotType, DomainParkingSlotTypes.Ev, StringComparison.Ordinal))
        {
            throw new ValidationException("parkingSlotId", "EV charging reservations require an EV-charging parking slot.");
        }

        // The reserving unit must be in the slot's community (Req 41.2).
        await RequireUnitInCommunityAsync(request.UnitId, slot.CommunityId, cancellationToken);

        // Max session duration (Req 41.2): the reserved window must not exceed the configurable cap.
        var maxDurationHours = await config.GetIntAsync(
            ConfigKeys.EvReservationMaxDurationHours, DefaultEvReservationMaxDurationHours, slot.CommunityId, cancellationToken);
        if (maxDurationHours <= 0)
        {
            maxDurationHours = DefaultEvReservationMaxDurationHours;
        }

        if (request.ReservedUntilUtc - request.ReservedFromUtc > TimeSpan.FromHours(maxDurationHours))
        {
            throw new ValidationException("reservedUntilUtc", $"The reservation window must not exceed {maxDurationHours} hours.");
        }

        // Per-unit concurrent-reservation cap (Req 41.2): count the unit's live (active) reservations
        // in the DATABASE and reject when the configurable cap is reached — no scan, no N+1.
        var maxConcurrent = await config.GetIntAsync(
            ConfigKeys.EvReservationMaxConcurrentPerUnit, DefaultEvReservationMaxConcurrentPerUnit, slot.CommunityId, cancellationToken);
        if (maxConcurrent <= 0)
        {
            maxConcurrent = DefaultEvReservationMaxConcurrentPerUnit;
        }

        var activeReservations = await db.EvReservations.AsNoTracking()
            .CountAsync(r => r.UnitId == request.UnitId && r.EndedAtUtc == null, cancellationToken);
        if (activeReservations >= maxConcurrent)
        {
            throw new ValidationException("unitId", "The unit has reached its maximum number of concurrent EV reservations.");
        }

        var reservation = new DomainEvReservation
        {
            CommunityId = slot.CommunityId,
            ParkingSlotId = slot.Id,
            ResidentId = request.ResidentId,
            UnitId = request.UnitId,
            ReservedFromUtc = request.ReservedFromUtc,
            ReservedUntilUtc = request.ReservedUntilUtc,
            EndedAtUtc = null,
            Status = DomainEvReservationStatuses.Reserved,
        };

        db.EvReservations.Add(reservation);

        await audit.AppendAsync(
            new AuditEntry("Create", "EvReservation", reservation.Id.ToString(), reservation.CommunityId,
                Detail: $"slotId={slot.Id}; unitId={request.UnitId}; from={request.ReservedFromUtc:O}; until={request.ReservedUntilUtc:O}"),
            cancellationToken);

        // Notify the reserving resident (Req 41.2). Only Adds the outbox row; the SaveChanges below
        // commits reservation + audit + notification atomically (transactional outbox).
        await EnqueueResidentNoticeAsync(
            request.ResidentId, NotificationTemplateKeys.EvReservationConfirmed, slot.CommunityId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["slotCode"] = slot.Code,
                ["reservedFrom"] = request.ReservedFromUtc.ToString("yyyy-MM-dd HH:mm'Z'"),
                ["reservedUntil"] = request.ReservedUntilUtc.ToString("yyyy-MM-dd HH:mm'Z'"),
            },
            cancellationToken);

        // The DB filtered unique index on (ParkingSlotId) WHERE EndedAtUtc IS NULL is the source of
        // truth for one-active-reservation-per-EV-slot (Req 41.3): two concurrent reservations of the
        // SAME slot race to it and exactly ONE wins; the loser's unique violation → CONFLICT (no
        // read-then-write). This is the identical DB-level guarantee ParkingAssignment uses.
        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), ActiveEvReservationConflictMessage);

        return ToDto(reservation);
    }

    public async Task<EvReservationDto> CloseEvReservationAsync(Guid reservationId, CloseEvReservationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "EvReservation", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to close an EV reservation.");
        }

        // TRACKED load of the ACTIVE reservation so its RowVersion rides the close UPDATE (a concurrent
        // double-close loser → CONFLICT centrally). An unknown/foreign/already-closed id → not found.
        var reservation = await db.EvReservations
            .FirstOrDefaultAsync(r => r.Id == reservationId && r.EndedAtUtc == null, cancellationToken)
            ?? throw new NotFoundException("No active EV reservation was found.");

        authorizationGuard.EnsureCommunityAuthorized(reservation.CommunityId);

        var status = await RequireValidCodeAsync(
            request.Status, MasterDataKeys.EvReservationStatus, reservation.CommunityId, "status", "EV reservation status", cancellationToken);

        // Only a terminal status closes a reservation (Req 41.2); reserved/charging keep it active.
        if (!string.Equals(status, DomainEvReservationStatuses.Completed, StringComparison.Ordinal)
            && !string.Equals(status, DomainEvReservationStatuses.Cancelled, StringComparison.Ordinal))
        {
            throw new ValidationException("status", "An EV reservation can only be closed as completed or cancelled.");
        }

        if (request.EnergyKwh is { } kwh && kwh < 0)
        {
            throw new ValidationException("energyKwh", "The energy consumption must not be negative.");
        }

        var now = clock.UtcNow;
        reservation.Status = status;
        reservation.EndedAtUtc = now;
        if (request.EnergyKwh is not null)
        {
            reservation.SessionEndedAtUtc = now;
            reservation.EnergyKwh = request.EnergyKwh;
        }

        await audit.AppendAsync(
            new AuditEntry("Update", "EvReservation", reservation.Id.ToString(), reservation.CommunityId,
                Detail: $"status={status}; energyKwh={request.EnergyKwh}"),
            cancellationToken);

        // RowVersion makes the close record exactly once (a loser → CONFLICT centrally); closing the
        // active row frees the EV slot for a new reservation.
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(reservation);
    }

    public async Task<IReadOnlyList<EvReservationDto>> ListEvReservationsForSlotAsync(Guid parkingSlotId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "EvReservation", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view EV reservations.");
        }

        // Tenant-scoped AsNoTracking read riding the (CommunityId, ParkingSlotId, ReservedFromUtc)
        // index, newest first; a foreign/unknown slot's rows are hidden by the global filter → empty.
        var rows = await db.EvReservations.AsNoTracking()
            .Where(r => r.ParkingSlotId == parkingSlotId)
            .OrderByDescending(r => r.ReservedFromUtc).ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDto).ToList();
    }

    // =============================================================================================
    // Helpers.
    // =============================================================================================

    /// <summary>
    /// Resolve the (unitId, visitorId) of a slot's current ACTIVE assignment (Req 40.2), or
    /// (null, null) when the slot is free. One tenant-scoped indexed read over the active-row set.
    /// </summary>
    private async Task<(Guid? UnitId, Guid? VisitorId)> ResolveActiveAssignmentAsync(Guid parkingSlotId, CancellationToken cancellationToken)
    {
        var active = await db.ParkingAssignments.AsNoTracking()
            .Where(a => a.ParkingSlotId == parkingSlotId && a.EndedAtUtc == null)
            .Select(a => new { a.UnitId, a.VisitorId })
            .FirstOrDefaultAsync(cancellationToken);

        return active is null ? (null, null) : (active.UnitId, active.VisitorId);
    }

    /// <summary>
    /// Enqueue the parking-violation notice to the responsible unit's residents (Req 40.4). Resolves
    /// the unit's residents' linked login users with one tenant-scoped grouped read (no N+1) and Adds
    /// an outbox row per recipient; the caller's SaveChanges commits them in the same transaction.
    /// A violation on an unassigned slot (no unit) has no resident to notify — nothing is enqueued.
    /// </summary>
    private async Task EnqueueViolationNoticeAsync(DomainParkingViolation violation, string slotCode, CancellationToken cancellationToken)
    {
        if (violation.UnitId is not { } unitId)
        {
            return;
        }

        var recipientUserIds = await db.Residents.AsNoTracking()
            .Where(r => r.UserId != null)
            .Join(
                db.HouseholdMembers.AsNoTracking().Where(h => h.UnitId == unitId && h.EndDate == null),
                r => r.Id,
                h => h.ResidentId,
                (r, h) => r.UserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (recipientUserIds.Count == 0)
        {
            return;
        }

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["slotCode"] = slotCode,
            ["violationType"] = violation.ViolationType,
            ["description"] = violation.Description,
        };

        foreach (var userId in recipientUserIds)
        {
            await notifications.EnqueueAsync(
                new NotificationRequest(NotificationTemplateKeys.ParkingViolationRecorded, userId, violation.CommunityId, Placeholders: placeholders),
                cancellationToken);
        }
    }

    /// <summary>Resolve the community id of a zone tenant-scoped, rejecting an unknown/foreign/other-community id (Req 40.1).</summary>
    private async Task RequireZoneInCommunityAsync(Guid zoneId, Guid communityId, CancellationToken cancellationToken)
    {
        var zoneCommunityId = await db.ParkingZones.AsNoTracking()
            .Where(z => z.Id == zoneId)
            .Select(z => (Guid?)z.CommunityId)
            .FirstOrDefaultAsync(cancellationToken);

        if (zoneCommunityId is null || zoneCommunityId.Value != communityId)
        {
            throw new ValidationException("zoneId", "The zone must belong to the same community as the slot.");
        }
    }

    /// <summary>Validate a unit is in the slot's community (Req 40.2), rejecting an unknown/foreign/other-community unit.</summary>
    private async Task RequireUnitInCommunityAsync(Guid unitId, Guid communityId, CancellationToken cancellationToken)
    {
        var unitCommunityId = await db.Units.AsNoTracking()
            .Where(u => u.Id == unitId)
            .Select(u => (Guid?)u.CommunityId)
            .FirstOrDefaultAsync(cancellationToken);

        if (unitCommunityId is null || unitCommunityId.Value != communityId)
        {
            throw new ValidationException("unitId", "The unit must belong to the same community as the slot.");
        }
    }

    /// <summary>Validate a visitor is in the slot's community (Req 40.2), rejecting an unknown/foreign/other-community visitor.</summary>
    private async Task RequireVisitorInCommunityAsync(Guid visitorId, Guid communityId, CancellationToken cancellationToken)
    {
        var visitorCommunityId = await db.Visitors.AsNoTracking()
            .Where(v => v.Id == visitorId)
            .Select(v => (Guid?)v.CommunityId)
            .FirstOrDefaultAsync(cancellationToken);

        if (visitorCommunityId is null || visitorCommunityId.Value != communityId)
        {
            throw new ValidationException("visitorId", "The visitor must belong to the same community as the slot.");
        }
    }

    /// <summary>Validate an offending vehicle is in the slot's community (Req 40.4), rejecting an unknown/foreign/other-community vehicle.</summary>
    private async Task RequireVehicleInCommunityAsync(Guid vehicleId, Guid communityId, CancellationToken cancellationToken)
    {
        var vehicleCommunityId = await db.Vehicles.AsNoTracking()
            .Where(v => v.Id == vehicleId)
            .Select(v => (Guid?)v.CommunityId)
            .FirstOrDefaultAsync(cancellationToken);

        if (vehicleCommunityId is null || vehicleCommunityId.Value != communityId)
        {
            throw new ValidationException("vehicleId", "The vehicle must belong to the same community as the slot.");
        }
    }

    /// <summary>
    /// Validate a code against a configurable master-data list resolved for the community (Req 40.1-40.4)
    /// — not a frozen enum. Returns the trimmed code; an unknown code → VALIDATION_ERROR. Mirrors
    /// AmenityService.RequireValidCodeAsync.
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

    private static string RequireText(string? value, string field, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(field, message);
        }

        return value.Trim();
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ParkingZoneDto ToDto(DomainParkingZone z) =>
        new(z.Id, z.CommunityId, z.Code, z.Name, z.ZoneType, z.Level);

    private static ParkingSlotDetailDto ToDto(DomainParkingSlot s, Guid? activeUnitId, Guid? activeVisitorId, bool isOccupied) =>
        new(s.Id, s.CommunityId, s.Code, s.ZoneId, s.Level, s.SlotNumber, s.SlotType, s.IsActive, isOccupied, activeUnitId, activeVisitorId);

    private static ParkingAssignmentDto ToDto(DomainParkingAssignment a) =>
        new(a.Id, a.CommunityId, a.ParkingSlotId, a.UnitId, a.VisitorId, a.AssignmentType, a.StartedAtUtc, a.EndedAtUtc);

    private static ParkingViolationDto ToDto(DomainParkingViolation v) =>
        new(v.Id, v.CommunityId, v.ParkingSlotId, v.VehicleId, v.UnitId, v.ViolationType, v.Description, v.EvidenceFileId, v.Status, v.RecordedAtUtc);

    private static EvReservationDto ToDto(DomainEvReservation r) =>
        new(r.Id, r.CommunityId, r.ParkingSlotId, r.ResidentId, r.UnitId, r.ReservedFromUtc, r.ReservedUntilUtc, r.EndedAtUtc, r.Status, r.EnergyKwh);

    /// <summary>
    /// Enqueue a single notice to a resident's linked login user (Req 41.1, 41.2). Resolves the
    /// resident's user id with one tenant-scoped indexed read; a resident with no platform account has
    /// no push target → nothing is enqueued. Only Adds the outbox row (no save) — the caller's single
    /// SaveChanges commits it in the same transaction (transactional outbox).
    /// </summary>
    private async Task EnqueueResidentNoticeAsync(
        Guid residentId, string templateKey, Guid communityId, Dictionary<string, string> placeholders, CancellationToken cancellationToken)
    {
        if (residentId == Guid.Empty)
        {
            return;
        }

        var userId = await db.Residents.AsNoTracking()
            .Where(r => r.Id == residentId && r.UserId != null)
            .Select(r => r.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (userId is not { } recipientId)
        {
            return;
        }

        await notifications.EnqueueAsync(
            new NotificationRequest(templateKey, recipientId, communityId, Placeholders: placeholders),
            cancellationToken);
    }
}
