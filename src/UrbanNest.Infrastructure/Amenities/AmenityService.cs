using UrbanNest.Application.Abstractions;
using UrbanNest.Application.Abstractions.Security;
using UrbanNest.Application.Amenities;
using UrbanNest.Application.Audit;
using UrbanNest.Application.Auth;
using UrbanNest.Application.Config;
using UrbanNest.Application.Exceptions;
using UrbanNest.Application.Notifications;
using UrbanNest.Application.Querying;
using UrbanNest.Contracts.Envelope;
using UrbanNest.Domain.Identity;
using UrbanNest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using DomainAmenity = UrbanNest.Domain.Amenities.Amenity;
using DomainAmenityStatuses = UrbanNest.Domain.Amenities.AmenityStatuses;
using DomainFacilitySlot = UrbanNest.Domain.Amenities.FacilitySlot;
using DomainBookingRule = UrbanNest.Domain.Amenities.BookingRule;
using DomainBlackoutDate = UrbanNest.Domain.Amenities.BlackoutDate;
using DomainBooking = UrbanNest.Domain.Amenities.Booking;
using DomainBookingStatuses = UrbanNest.Domain.Amenities.BookingStatuses;
using DomainSlotBookingCount = UrbanNest.Domain.Amenities.SlotBookingCount;
using DomainResidentEvent = UrbanNest.Domain.Amenities.ResidentEvent;
using DomainResidentEventStatuses = UrbanNest.Domain.Amenities.ResidentEventStatuses;
using DomainResidentEventRegistration = UrbanNest.Domain.Amenities.ResidentEventRegistration;

namespace UrbanNest.Infrastructure.Amenities;

/// <summary>
/// EF-backed <see cref="IAmenityService"/> (Task 23.1, Req 38.1–38.4): the CONFIGURATION side of the
/// Amenities module — the bookable <c>Amenity</c> master with QR-free community-unique code and a
/// configurable lifecycle status, its <c>FacilitySlot</c> windows, its per-amenity singleton
/// <c>BookingRule</c> set and its <c>BlackoutDate</c> ranges — plus the tenant-scoped read surface.
/// This is the single cohesive Amenities seam — the booking lifecycle with capacity concurrency
/// (Task 23.2) and the HTTP controllers (Task 24.3) extend it in later tasks (mirrors how
/// <c>MaintenanceService</c> grows across its tasks). 23.1 provisions configuration only; it never
/// creates a booking or enforces capacity.
///
/// <para><b>Pattern parity with <c>MaintenanceService</c>.</b> Every mutation checks the resource
/// permission (defense-in-depth) + community authorization, validates configurable codes against the
/// editable master-data lists (no frozen enums, no hardcoded values — numeric rule defaults resolve
/// via Config_Service), loads TRACKED so the inherited <c>RowVersion</c> rides edits/deletes (a
/// concurrency loser → CONFLICT centrally), audits via <see cref="IAuditService"/>, and lets DB
/// unique indexes be the source of truth (code uniqueness + one-rule-set-per-amenity → CONFLICT via
/// <c>UniqueConstraintGuard</c>, never a read-then-write gap). Reads are tenant-scoped by the global
/// filter (entities are NOT <c>IUnitScoped</c> — community infrastructure), <c>AsNoTracking</c>, and
/// the amenity list rides the shared allow-listed <c>ListQueryConfiguration</c> + <c>ToPagedResultAsync</c>.</para>
/// </summary>
public sealed class AmenityService(
    AppDbContext db,
    IAuditService audit,
    IConfigService config,
    IClock clock,
    IAuthorizationGuard authorizationGuard,
    INotificationService notifications,
    ITenantContext tenant,
    IPageSizePolicy pageSizePolicy,
    IAsyncQueryExecutor executor)
    : IAmenityService
{
    // Allow-list for the Task 24.3 amenity list endpoint (Req 38.1). Only these fields reach the
    // query — no arbitrary client LINQ (steering: no hardcoding, injection-safe). Sorts/filters ride
    // the amenity's indexed columns; default is newest-first. Mirrors AssetQueryConfig.
    private static readonly ListQueryConfiguration<DomainAmenity> AmenityQueryConfig =
        new ListQueryConfiguration<DomainAmenity>()
            .WithDefaultSort(a => a.CreatedAtUtc, SortDirection.Descending)
            .Sortable("createdAtUtc", a => a.CreatedAtUtc)
            .Sortable("name", a => a.Name)
            .Sortable("code", a => a.Code)
            .Sortable("status", a => a.Status)
            .Sortable("amenityType", a => a.AmenityType)
            .Filterable("status", a => a.Status)
            .Filterable("amenityType", a => a.AmenityType)
            .Searchable(a => a.Name)
            .Searchable(a => a.Code)
            .Searchable(a => a.Location);

    // Seeded fallback for the default advance-booking window (days) when
    // amenity.booking.defaultAdvanceWindowDays is unset in Config_Service (Req 38.3) — a safe
    // default, not a hardcoded decision: a community overrides the key, and a per-amenity rule value
    // overrides it once configured (steering: no hardcoding).
    private const int DefaultAdvanceBookingWindowDays = 30;

    // Seeded fallback for the default max-bookings-per-resident when
    // amenity.booking.defaultMaxBookingsPerResident is unset in Config_Service (Req 38.3) — a safe
    // default, same configurability as above.
    private const int DefaultMaxBookingsPerResident = 2;

    private const int MinutesPerDay = 24 * 60;

    private const string DuplicateCodeMessage =
        "An amenity with the same code already exists in this community.";

    // =============================================================================================
    // Amenity master (Req 38.1).
    // =============================================================================================

    public async Task<AmenityDto> CreateAmenityAsync(CreateAmenityRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 38.1): a Facility Manager defines an amenity with Create on Amenity.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "Amenity", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to create an amenity.");
        }

        authorizationGuard.EnsureCommunityAuthorized(request.CommunityId);

        var name = RequireText(request.Name, "name", "An amenity name is required.");
        var code = RequireText(request.Code, "code", "An amenity code is required.");
        var amenityType = await RequireValidCodeAsync(
            request.AmenityType, MasterDataKeys.AmenityType, request.CommunityId, "amenityType", "amenity type", cancellationToken);
        var capacity = RequirePositive(request.DefaultCapacity, "defaultCapacity", "The default capacity must be positive.");

        var amenity = new DomainAmenity
        {
            CommunityId = request.CommunityId,
            Name = name,
            Code = code,
            AmenityType = amenityType,
            Status = DomainAmenityStatuses.Active,
            Location = Trimmed(request.Location),
            Description = Trimmed(request.Description),
            DefaultCapacity = capacity,
        };

        db.Amenities.Add(amenity);

        await audit.AppendAsync(
            new AuditEntry("Create", "Amenity", amenity.Id.ToString(), amenity.CommunityId,
                Detail: $"code={code}; type={amenityType}"),
            cancellationToken);

        // The DB Unique(CommunityId, Code) index is the source of truth for code uniqueness: a
        // duplicate maps to CONFLICT rather than a read-then-write pre-check (steering: concurrency
        // safety via DB-level guarantees).
        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), DuplicateCodeMessage);

        return ToDto(amenity);
    }

    public async Task<AmenityDto> UpdateAmenityAsync(Guid amenityId, UpdateAmenityRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "Amenity", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to edit an amenity.");
        }

        // TRACKED load so EF carries the RowVersion into the UPDATE (the edit records exactly once
        // under concurrency). The global tenant filter hides foreign/archived rows → not found.
        var amenity = await db.Amenities.FirstOrDefaultAsync(a => a.Id == amenityId, cancellationToken)
            ?? throw new NotFoundException("Amenity not found.");

        authorizationGuard.EnsureCommunityAuthorized(amenity.CommunityId);

        if (request.Name is not null)
        {
            amenity.Name = RequireText(request.Name, "name", "An amenity name is required.");
        }

        if (request.AmenityType is not null)
        {
            amenity.AmenityType = await RequireValidCodeAsync(
                request.AmenityType, MasterDataKeys.AmenityType, amenity.CommunityId, "amenityType", "amenity type", cancellationToken);
        }

        if (request.Status is not null)
        {
            // 23.1 validates the status as an active Amenity_Status code (no transition allow-list for
            // the amenity lifecycle — the requirement needs a tracked state, not transition validation).
            amenity.Status = await RequireValidCodeAsync(
                request.Status, MasterDataKeys.AmenityStatus, amenity.CommunityId, "status", "amenity status", cancellationToken);
        }

        if (request.Location is not null)
        {
            amenity.Location = Trimmed(request.Location);
        }

        if (request.Description is not null)
        {
            amenity.Description = Trimmed(request.Description);
        }

        if (request.DefaultCapacity is { } capacity)
        {
            amenity.DefaultCapacity = RequirePositive(capacity, "defaultCapacity", "The default capacity must be positive.");
        }

        await audit.AppendAsync(
            new AuditEntry("Update", "Amenity", amenity.Id.ToString(), amenity.CommunityId, Detail: null),
            cancellationToken);

        // RowVersion makes the edit record exactly once under concurrency (a loser → CONFLICT).
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(amenity);
    }

    public async Task<AmenityDto?> GetAmenityAsync(Guid amenityId, CancellationToken cancellationToken = default)
    {
        // Tenant-scoped AsNoTracking read (Req 38.1): an unknown/foreign/soft-deleted id is hidden by
        // the global filter → null.
        var amenity = await db.Amenities.AsNoTracking().FirstOrDefaultAsync(a => a.Id == amenityId, cancellationToken);
        return amenity is null ? null : ToDto(amenity);
    }

    public async Task<PagedData<AmenityDto>> ListAmenitiesAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "Amenity", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view amenities.");
        }

        // Tenant-scoped by the global query filter (soft-deleted rows hidden); the allow-listed query
        // config fixes the sort/filter/search surface; AsNoTracking projection, page size clamped by
        // the shared policy — mirrors ListAssetsAsync.
        return await db.Amenities.AsNoTracking().ToPagedResultAsync(
            parameters, AmenityQueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
    }

    public async Task DeleteAmenityAsync(Guid amenityId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Delete, "Amenity", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to delete an amenity.");
        }

        var amenity = await db.Amenities.FirstOrDefaultAsync(a => a.Id == amenityId, cancellationToken)
            ?? throw new NotFoundException("Amenity not found.");

        authorizationGuard.EnsureCommunityAuthorized(amenity.CommunityId);

        // Remove is converted into a Soft_Delete by AuditSaveChangesInterceptor, so the row stays
        // traceable but is hidden from ordinary reads.
        db.Amenities.Remove(amenity);

        await audit.AppendAsync(
            new AuditEntry("Delete", "Amenity", amenity.Id.ToString(), amenity.CommunityId, Detail: null),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    // =============================================================================================
    // FacilitySlot (Req 38.2).
    // =============================================================================================

    public async Task<FacilitySlotDto> CreateFacilitySlotAsync(CreateFacilitySlotRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "FacilitySlot", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to create a facility slot.");
        }

        // The amenity anchors the slot's community (same-community by construction). The global tenant
        // filter already hides foreign-tenant amenities, so an unknown/foreign id is a validation error.
        var communityId = await RequireAmenityCommunityAsync(request.AmenityId, cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(communityId);

        ValidateWindow(request.StartMinutesOfDay, request.EndMinutesOfDay);
        var mask = RequireDaysOfWeekMask(request.DaysOfWeekMask);
        var capacity = RequirePositive(request.Capacity, "capacity", "The slot capacity must be positive.");

        var slot = new DomainFacilitySlot
        {
            CommunityId = communityId,
            AmenityId = request.AmenityId,
            Name = Trimmed(request.Name),
            StartMinutesOfDay = request.StartMinutesOfDay,
            EndMinutesOfDay = request.EndMinutesOfDay,
            DaysOfWeekMask = mask,
            Capacity = capacity,
            IsActive = true,
        };

        db.FacilitySlots.Add(slot);

        await audit.AppendAsync(
            new AuditEntry("Create", "FacilitySlot", slot.Id.ToString(), slot.CommunityId,
                Detail: $"amenityId={slot.AmenityId}; window={slot.StartMinutesOfDay}-{slot.EndMinutesOfDay}; capacity={capacity}"),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(slot);
    }

    public async Task<FacilitySlotDto> UpdateFacilitySlotAsync(Guid slotId, UpdateFacilitySlotRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "FacilitySlot", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to edit a facility slot.");
        }

        var slot = await db.FacilitySlots.FirstOrDefaultAsync(s => s.Id == slotId, cancellationToken)
            ?? throw new NotFoundException("Facility slot not found.");

        authorizationGuard.EnsureCommunityAuthorized(slot.CommunityId);

        if (request.Name is not null)
        {
            slot.Name = Trimmed(request.Name);
        }

        // Resolve the effective window (supplied value or the current one) so a partial update still
        // validates start < end against the final state.
        var start = request.StartMinutesOfDay ?? slot.StartMinutesOfDay;
        var end = request.EndMinutesOfDay ?? slot.EndMinutesOfDay;
        if (request.StartMinutesOfDay is not null || request.EndMinutesOfDay is not null)
        {
            ValidateWindow(start, end);
            slot.StartMinutesOfDay = start;
            slot.EndMinutesOfDay = end;
        }

        if (request.DaysOfWeekMask is { } mask)
        {
            slot.DaysOfWeekMask = RequireDaysOfWeekMask(mask);
        }

        if (request.Capacity is { } capacity)
        {
            slot.Capacity = RequirePositive(capacity, "capacity", "The slot capacity must be positive.");
        }

        if (request.IsActive is { } isActive)
        {
            slot.IsActive = isActive;
        }

        await audit.AppendAsync(
            new AuditEntry("Update", "FacilitySlot", slot.Id.ToString(), slot.CommunityId, Detail: null),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(slot);
    }

    public async Task<IReadOnlyList<FacilitySlotDto>> GetAmenitySlotsAsync(Guid amenityId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "FacilitySlot", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view facility slots.");
        }

        // Tenant-scoped AsNoTracking read riding the (CommunityId, AmenityId, StartMinutesOfDay) index
        // — a foreign-tenant amenity's slots are hidden by the global filter, so an unknown/foreign id
        // yields an empty list. Ordered by window start (Req 38.2).
        var slots = await db.FacilitySlots.AsNoTracking()
            .Where(s => s.AmenityId == amenityId)
            .OrderBy(s => s.StartMinutesOfDay).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

        return slots.Select(ToDto).ToList();
    }

    public async Task DeleteFacilitySlotAsync(Guid slotId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Delete, "FacilitySlot", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to delete a facility slot.");
        }

        var slot = await db.FacilitySlots.FirstOrDefaultAsync(s => s.Id == slotId, cancellationToken)
            ?? throw new NotFoundException("Facility slot not found.");

        authorizationGuard.EnsureCommunityAuthorized(slot.CommunityId);

        db.FacilitySlots.Remove(slot);

        await audit.AppendAsync(
            new AuditEntry("Delete", "FacilitySlot", slot.Id.ToString(), slot.CommunityId, Detail: null),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    // =============================================================================================
    // BookingRule (Req 38.3) — per-amenity singleton upsert.
    // =============================================================================================

    public async Task<BookingRuleDto> SetBookingRuleAsync(SetBookingRuleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Rules are managed via the amenity's Edit grant (Req 38.3) — the booking policy belongs to
        // the amenity.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "Amenity", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to configure an amenity's booking rules.");
        }

        var communityId = await RequireAmenityCommunityAsync(request.AmenityId, cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(communityId);

        // Validate every eligible resident-type code against the editable Resident_Type list (an
        // unknown code → VALIDATION_ERROR, nothing stored) — never a hardcoded eligibility rule. A
        // null/empty list means "all resident types eligible".
        var allowedResidentTypes = await ValidateResidentTypesCsvAsync(request.AllowedResidentTypes, communityId, cancellationToken);

        var advanceWindow = request.AdvanceBookingWindowDays
            ?? await config.GetIntAsync(ConfigKeys.AmenityDefaultAdvanceBookingWindowDays, DefaultAdvanceBookingWindowDays, communityId, cancellationToken);
        if (advanceWindow < 0)
        {
            throw new ValidationException("advanceBookingWindowDays", "The advance-booking window must not be negative.");
        }

        var minDuration = ValidateOptionalPositive(request.MinDurationMinutes, "minDurationMinutes", "The minimum duration must be positive.");
        var maxDuration = ValidateOptionalPositive(request.MaxDurationMinutes, "maxDurationMinutes", "The maximum duration must be positive.");
        if (minDuration is { } min && maxDuration is { } max && max < min)
        {
            throw new ValidationException("maxDurationMinutes", "The maximum duration must not be less than the minimum duration.");
        }

        var maxBookings = request.MaxBookingsPerResident
            ?? await config.GetIntAsync(ConfigKeys.AmenityDefaultMaxBookingsPerResident, DefaultMaxBookingsPerResident, communityId, cancellationToken);
        if (maxBookings <= 0)
        {
            throw new ValidationException("maxBookingsPerResident", "The max bookings per resident must be positive.");
        }

        var periodDays = ValidateOptionalPositive(request.MaxBookingsPeriodDays, "maxBookingsPeriodDays", "The booking period must be positive.");
        var noticeHours = ValidateOptionalNonNegative(request.CancellationNoticeHours, "cancellationNoticeHours", "The cancellation notice must not be negative.");

        if (request.BookingCharge < 0)
        {
            throw new ValidationException("bookingCharge", "The booking charge must not be negative.");
        }

        if (request.SecurityDeposit < 0)
        {
            throw new ValidationException("securityDeposit", "The security deposit must not be negative.");
        }

        if (request.RefundPercentOnCancellation is { } refundPct && (refundPct < 0 || refundPct > 100))
        {
            throw new ValidationException("refundPercentOnCancellation", "The refund percent must be between 0 and 100.");
        }

        // Upsert: an amenity has ONE rule-set (Unique(AmenityId)). Load TRACKED so an existing row's
        // RowVersion rides the UPDATE (edit records exactly once under concurrency); otherwise insert
        // a new row (the DB unique index makes a concurrent duplicate insert → CONFLICT).
        var rule = await db.BookingRules.FirstOrDefaultAsync(r => r.AmenityId == request.AmenityId, cancellationToken);
        var isNew = rule is null;
        rule ??= new DomainBookingRule { CommunityId = communityId, AmenityId = request.AmenityId };

        rule.AllowedResidentTypes = allowedResidentTypes;
        rule.AdvanceBookingWindowDays = advanceWindow;
        rule.MinDurationMinutes = minDuration;
        rule.MaxDurationMinutes = maxDuration;
        rule.MaxBookingsPerResident = maxBookings;
        rule.MaxBookingsPeriodDays = periodDays;
        rule.RequiresApproval = request.RequiresApproval;
        rule.BookingCharge = request.BookingCharge;
        rule.SecurityDeposit = request.SecurityDeposit;
        rule.CancellationNoticeHours = noticeHours;
        rule.RefundPercentOnCancellation = request.RefundPercentOnCancellation;

        if (isNew)
        {
            db.BookingRules.Add(rule);
        }

        await audit.AppendAsync(
            new AuditEntry(isNew ? "Create" : "Update", "BookingRule", rule.Id.ToString(), rule.CommunityId,
                Detail: $"amenityId={rule.AmenityId}; requiresApproval={rule.RequiresApproval}"),
            cancellationToken);

        // The DB Unique(AmenityId) index is the source of truth: a concurrent duplicate insert maps to
        // CONFLICT rather than a read-then-write gap.
        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken),
            "A booking-rule set already exists for this amenity.");

        return ToDto(rule);
    }

    public async Task<BookingRuleDto?> GetBookingRuleAsync(Guid amenityId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "Amenity", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view an amenity's booking rules.");
        }

        // Tenant-scoped AsNoTracking read (Req 38.3): an amenity with no configured rule-set (or an
        // unknown/foreign id) → null.
        var rule = await db.BookingRules.AsNoTracking().FirstOrDefaultAsync(r => r.AmenityId == amenityId, cancellationToken);
        return rule is null ? null : ToDto(rule);
    }

    // =============================================================================================
    // BlackoutDate (Req 38.4).
    // =============================================================================================

    public async Task<BlackoutDateDto> CreateBlackoutDateAsync(CreateBlackoutDateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "BlackoutDate", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to create a blackout date.");
        }

        var communityId = await RequireAmenityCommunityAsync(request.AmenityId, cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(communityId);

        var reason = RequireText(request.Reason, "reason", "A blackout reason is required.");
        if (request.EndUtc <= request.StartUtc)
        {
            throw new ValidationException("endUtc", "The blackout end must be after the start.");
        }

        var blackout = new DomainBlackoutDate
        {
            CommunityId = communityId,
            AmenityId = request.AmenityId,
            StartUtc = request.StartUtc,
            EndUtc = request.EndUtc,
            Reason = reason,
        };

        db.BlackoutDates.Add(blackout);

        await audit.AppendAsync(
            new AuditEntry("Create", "BlackoutDate", blackout.Id.ToString(), blackout.CommunityId,
                Detail: $"amenityId={blackout.AmenityId}; start={blackout.StartUtc:O}; end={blackout.EndUtc:O}"),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(blackout);
    }

    public async Task<IReadOnlyList<BlackoutDateDto>> GetAmenityBlackoutDatesAsync(Guid amenityId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "BlackoutDate", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view blackout dates.");
        }

        // Tenant-scoped AsNoTracking read riding the (CommunityId, AmenityId, StartUtc) index — a
        // foreign-tenant amenity's blackouts are hidden by the global filter, so an unknown/foreign id
        // yields an empty list. Ordered by start instant (Req 38.4).
        var blackouts = await db.BlackoutDates.AsNoTracking()
            .Where(b => b.AmenityId == amenityId)
            .OrderBy(b => b.StartUtc).ThenBy(b => b.Id)
            .ToListAsync(cancellationToken);

        return blackouts.Select(ToDto).ToList();
    }

    public async Task DeleteBlackoutDateAsync(Guid blackoutId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Delete, "BlackoutDate", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to delete a blackout date.");
        }

        var blackout = await db.BlackoutDates.FirstOrDefaultAsync(b => b.Id == blackoutId, cancellationToken)
            ?? throw new NotFoundException("Blackout date not found.");

        authorizationGuard.EnsureCommunityAuthorized(blackout.CommunityId);

        db.BlackoutDates.Remove(blackout);

        await audit.AppendAsync(
            new AuditEntry("Delete", "BlackoutDate", blackout.Id.ToString(), blackout.CommunityId, Detail: null),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    // =============================================================================================
    // Booking lifecycle + capacity concurrency (Task 23.2, Req 39.1-39.5).
    // =============================================================================================

    public async Task<BookingDto> CreateBookingAsync(CreateBookingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 39.1): a resident books a slot with Create on Booking. The 24.3
        // endpoint declares the same; the invariant holds regardless of caller.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "Booking", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to create a booking.");
        }

        if (request.ResidentId == Guid.Empty)
        {
            throw new ValidationException("residentId", "A booking resident is required.");
        }

        if (request.UnitId == Guid.Empty)
        {
            throw new ValidationException("unitId", "A booking unit is required.");
        }

        // Normalize to a date-only (UTC midnight) value — the per-date capacity key and the weekday/
        // window checks are date-grained (Req 39.1).
        var bookingDate = request.BookingDate.Date;

        // Load the slot tenant-scoped (the global filter hides foreign-tenant/retired rows → an
        // unknown/foreign id is a validation error). AsNoTracking: the booking row + the counter are
        // the only things this flow writes; the slot is read-only input.
        var slot = await db.FacilitySlots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.FacilitySlotId, cancellationToken)
            ?? throw new ValidationException("facilitySlotId", "The facility slot was not found in your authorized communities.");

        authorizationGuard.EnsureCommunityAuthorized(slot.CommunityId);

        if (!slot.IsActive)
        {
            throw new ValidationException("facilitySlotId", "The facility slot is not available for booking.");
        }

        // The amenity must exist (tenant-scoped) and be bookable (active) — an inactive /
        // under-maintenance amenity is not bookable (Req 38.1, the lifecycle reads this).
        var amenity = await db.Amenities.AsNoTracking().FirstOrDefaultAsync(a => a.Id == slot.AmenityId, cancellationToken)
            ?? throw new ValidationException("facilitySlotId", "The amenity was not found in your authorized communities.");

        if (!string.Equals(amenity.Status, DomainAmenityStatuses.Active, StringComparison.Ordinal))
        {
            throw new ValidationException("facilitySlotId", "The amenity is not currently available for booking.");
        }

        // The slot must recur on the booking date's weekday (Req 38.2) and the date must not be in the
        // past (a booking is a future reservation).
        if (!slot.RecursOn(bookingDate.DayOfWeek))
        {
            throw new ValidationException("bookingDate", "The slot is not offered on the selected date.");
        }

        if (bookingDate < clock.UtcNow.Date)
        {
            throw new ValidationException("bookingDate", "The booking date must not be in the past.");
        }

        // Resolve the resident tenant-scoped (for eligibility + the notification recipient). An
        // unknown/foreign resident id → validation error (same-community by construction).
        var resident = await db.Residents.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.ResidentId, cancellationToken)
            ?? throw new ValidationException("residentId", "The resident was not found in your authorized communities.");

        // Apply the amenity's configurable booking rule (eligibility / advance window / per-resident
        // cap / approval). A rule-set is optional: an amenity with no configured rule-set is open
        // (all resident types eligible, no advance limit, no cap, auto-confirm) — the documented open
        // default mirroring BookingRule.AllowedResidentTypes == null.
        var rule = await db.BookingRules.AsNoTracking().FirstOrDefaultAsync(r => r.AmenityId == amenity.Id, cancellationToken);

        var requiresApproval = rule?.RequiresApproval ?? false;
        var bookingCharge = rule?.BookingCharge ?? 0m;
        var securityDeposit = rule?.SecurityDeposit ?? 0m;

        if (rule is not null)
        {
            // Eligibility (Req 39.1): the resident's type must be in the rule's allow-list when one is
            // configured (null/empty = all eligible).
            if (!string.IsNullOrWhiteSpace(rule.AllowedResidentTypes))
            {
                var eligible = rule.AllowedResidentTypes
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (!eligible.Contains(resident.ResidentType, StringComparer.Ordinal))
                {
                    throw new ValidationException("residentId", "The resident type is not eligible to book this amenity.");
                }
            }

            // Advance-booking window (Req 38.3): the date must be within the configured number of days
            // ahead. Null = no advance-window limit.
            if (rule.AdvanceBookingWindowDays is { } windowDays)
            {
                var latest = clock.UtcNow.Date.AddDays(windowDays);
                if (bookingDate > latest)
                {
                    throw new ValidationException("bookingDate", "The booking date is beyond the advance-booking window.");
                }
            }
        }

        // Blackout exclusion (Req 38.4): reject a booking whose date falls within ANY active blackout
        // range for the amenity. The half-open range covers the booking day when it overlaps [day,
        // day+1). A single indexed read over the amenity's blackouts (no scan). The non-overlap
        // predicate (start < day+1 AND end > day) runs in the database.
        var dayEnd = bookingDate.AddDays(1);
        var blackedOut = await db.BlackoutDates.AsNoTracking()
            .AnyAsync(b => b.AmenityId == amenity.Id && b.StartUtc < dayEnd && b.EndUtc > bookingDate, cancellationToken);
        if (blackedOut)
        {
            throw new ValidationException("bookingDate", "The amenity is unavailable (blacked out) on the selected date.");
        }

        // Per-resident cap (Req 38.3): count the resident's live (non-cancelled) bookings for the
        // amenity within the rolling period and reject when the cap is reached. Counted in the
        // database over the (CommunityId, ResidentId, BookingDate) index (no N+1, no in-memory scan).
        if (rule?.MaxBookingsPerResident is { } cap)
        {
            var periodStart = rule.MaxBookingsPeriodDays is { } periodDays
                ? bookingDate.AddDays(-periodDays)
                : DateTime.MinValue;

            var existing = await db.Bookings.AsNoTracking()
                .CountAsync(
                    b => b.ResidentId == resident.Id
                        && b.AmenityId == amenity.Id
                        && b.Status != DomainBookingStatuses.Cancelled
                        && b.BookingDate >= periodStart,
                    cancellationToken);

            if (existing >= cap)
            {
                throw new ValidationException("residentId", "The resident has reached the maximum number of bookings for this amenity.");
            }
        }

        // --- Capacity concurrency: claim a per-(slot, date) seat with a DB-level guarded UPDATE ----

        // Ensure the per-(slot, date) counter row exists (lazy create-on-first-booking). The
        // Unique(FacilitySlotId, BookingDate) index makes this race-safe: two concurrent first
        // bookings both attempt the insert, the database lets exactly one win and the loser's
        // duplicate insert → CONFLICT, which we swallow and then re-resolve the winner's row. No
        // read-then-write existence gap. The capacity is snapshotted from the slot.
        var counterId = await EnsureSlotBookingCountAsync(slot, bookingDate, cancellationToken);

        // Atomic guarded increment (Req 39.2, 39.5): the database evaluates BookedCount < Capacity and
        // applies +1 in ONE statement. affected == 1 → a seat was claimed; affected == 0 → the slot is
        // full. Two requests racing the last seat serialize at the row; exactly one wins — no oversell,
        // no lost update, no read-then-write (steering: DB-level concurrency guarantee).
        var seatClaimed = await db.SlotBookingCounts
            .Where(c => c.Id == counterId && c.BookedCount < c.Capacity)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.BookedCount, c => c.BookedCount + 1), cancellationToken);

        if (seatClaimed == 0)
        {
            // At capacity (Req 39.2): a clean validation error, no booking row written, the seat ledger
            // untouched.
            throw new ValidationException("facilitySlotId", "The slot is fully booked for the selected date.");
        }

        // Seat is held. Create the booking row; if persisting it fails for any reason, release the
        // seat so a claimed-but-unrecorded seat never leaks capacity.
        var status = requiresApproval ? DomainBookingStatuses.Requested : DomainBookingStatuses.Confirmed;
        var booking = new DomainBooking
        {
            CommunityId = slot.CommunityId,
            UnitId = request.UnitId,
            AmenityId = amenity.Id,
            FacilitySlotId = slot.Id,
            ResidentId = resident.Id,
            BookingDate = bookingDate,
            Status = status,
            RequiresApproval = requiresApproval,
            BookingCharge = bookingCharge,
            SecurityDeposit = securityDeposit,
            ConfirmedAtUtc = requiresApproval ? null : clock.UtcNow,
        };

        db.Bookings.Add(booking);

        await audit.AppendAsync(
            new AuditEntry("Create", "Booking", booking.Id.ToString(), booking.CommunityId,
                Detail: $"slotId={slot.Id}; date={bookingDate:yyyy-MM-dd}; status={status}"),
            cancellationToken);

        // Notify the booking resident of the create/confirmation; when approval is required, also the
        // amenity's approvers (Req 39.4). EnqueueAsync only Adds Pending outbox rows to THIS DbContext
        // (no save), so the single SaveChanges below commits the booking + the audit + the
        // notifications atomically (transactional outbox).
        await EnqueueBookingCreatedAsync(booking, amenity, cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // The booking did not persist → return the held seat with the symmetric guarded decrement
            // so capacity is never consumed by a phantom booking. This runs on its own statement
            // (ExecuteUpdateAsync is independent of the failed change-tracker save), so it is not undone
            // by the rollback of the tracked SaveChanges above.
            await db.SlotBookingCounts
                .Where(c => c.Id == counterId && c.BookedCount > 0)
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.BookedCount, c => c.BookedCount - 1), CancellationToken.None);
            throw;
        }

        return ToDto(booking);
    }

    public async Task<BookingDto> ApproveBookingAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Approve, "Booking", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to approve a booking.");
        }

        // TRACKED load so the RowVersion rides the UPDATE (a double-approve loser → CONFLICT). The
        // tenant filter hides foreign rows → not found. (Approve is a staff action, not resident
        // self-scoped — a Facility Manager approves any booking in their communities.)
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking not found.");

        authorizationGuard.EnsureCommunityAuthorized(booking.CommunityId);

        // Move requested → confirmed through the editable allow-list (an illegal move, e.g. a booking
        // that is not pending, → VALIDATION_ERROR, nothing mutated — no hardcoded table).
        await RequireAllowedTransitionAsync(booking.Status, DomainBookingStatuses.Confirmed, booking.CommunityId, cancellationToken);

        booking.Status = DomainBookingStatuses.Confirmed;
        booking.ApprovedByUserId = ParseActingUserId();
        booking.ConfirmedAtUtc = clock.UtcNow;

        await audit.AppendAsync(
            new AuditEntry("Approve", "Booking", booking.Id.ToString(), booking.CommunityId, Detail: null),
            cancellationToken);

        // Notify the booking resident of the confirmation (Req 39.4). Only Adds the outbox row; the
        // SaveChanges below commits the transition + audit + notification atomically.
        await EnqueueBookingResidentNoticeAsync(booking, NotificationTemplateKeys.BookingConfirmed, cancellationToken);

        // RowVersion makes the approval record exactly once (a loser → CONFLICT centrally).
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(booking);
    }

    public async Task<BookingDto> CancelBookingAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "Booking", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to cancel a booking.");
        }

        // TRACKED load through the tenant + resident self-scope filter: a resident principal resolves
        // ONLY their own unit's booking, so a resident cancels only their own booking (Req 39.4) — a
        // foreign/other-unit booking is a not-found. The RowVersion rides the UPDATE (a double-cancel
        // loser → CONFLICT), so the seat is released exactly once.
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking not found.");

        authorizationGuard.EnsureCommunityAuthorized(booking.CommunityId);

        // Enforce the cancellation-notice window (Req 38.3) when the amenity's rule configures one:
        // the resident must cancel at least N hours before the slot's start on the booking date.
        var rule = await db.BookingRules.AsNoTracking().FirstOrDefaultAsync(r => r.AmenityId == booking.AmenityId, cancellationToken);
        if (rule?.CancellationNoticeHours is { } noticeHours)
        {
            var slotStart = await db.FacilitySlots.AsNoTracking()
                .Where(s => s.Id == booking.FacilitySlotId)
                .Select(s => (int?)s.StartMinutesOfDay)
                .FirstOrDefaultAsync(cancellationToken) ?? 0;

            var slotStartUtc = booking.BookingDate.AddMinutes(slotStart);
            if (clock.UtcNow > slotStartUtc.AddHours(-noticeHours))
            {
                throw new ValidationException("bookingId", "The cancellation-notice window for this booking has passed.");
            }
        }

        // Move the current status → cancelled through the editable allow-list (an already-cancelled /
        // completed booking has no edge to cancelled, so a re-cancel → VALIDATION_ERROR — no hardcoded
        // table).
        await RequireAllowedTransitionAsync(booking.Status, DomainBookingStatuses.Cancelled, booking.CommunityId, cancellationToken);

        booking.Status = DomainBookingStatuses.Cancelled;
        booking.CancelledAtUtc = clock.UtcNow;

        // Release the held per-(slot, date) seat back to the counter EXACTLY ONCE. The guarded
        // decrement (BookedCount > 0) runs immediately; the SeatReleased flag + the booking RowVersion
        // make the whole cancel record once, so a concurrent double-cancel loses the RowVersion check
        // (CONFLICT) and never decrements twice. We flip the flag before the save so the committed row
        // reflects the released seat.
        if (!booking.SeatReleased)
        {
            var counterId = await db.SlotBookingCounts.AsNoTracking()
                .Where(c => c.FacilitySlotId == booking.FacilitySlotId && c.BookingDate == booking.BookingDate)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (counterId is { } id)
            {
                await db.SlotBookingCounts
                    .Where(c => c.Id == id && c.BookedCount > 0)
                    .ExecuteUpdateAsync(set => set.SetProperty(c => c.BookedCount, c => c.BookedCount - 1), cancellationToken);
            }

            booking.SeatReleased = true;
        }

        await audit.AppendAsync(
            new AuditEntry("Cancel", "Booking", booking.Id.ToString(), booking.CommunityId, Detail: null),
            cancellationToken);

        // Notify the booking resident of the cancellation (Req 39.4). Only Adds the outbox row.
        await EnqueueBookingResidentNoticeAsync(booking, NotificationTemplateKeys.BookingCancelled, cancellationToken);

        // RowVersion makes the cancel (and the single seat release above) record exactly once.
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(booking);
    }

    public async Task<BookingDto?> GetBookingAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        // Tenant + resident-self-scoped AsNoTracking read (Req 39.1): an unknown/foreign/out-of-scope/
        // soft-deleted id is hidden by the global + self-scope filters → null.
        var booking = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken);
        return booking is null ? null : ToDto(booking);
    }

    // =============================================================================================
    // Resident events + capacity-bounded registration (Task 24.2, Req 41.4, 41.5).
    // =============================================================================================

    public async Task<ResidentEventDto> PublishResidentEventAsync(PublishResidentEventRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 41.4): a resident publishes an event with Create on ResidentEvent.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "ResidentEvent", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to publish a resident event.");
        }

        authorizationGuard.EnsureCommunityAuthorized(request.CommunityId);

        if (request.OrganizerResidentId == Guid.Empty)
        {
            throw new ValidationException("organizerResidentId", "An organiser resident is required.");
        }

        if (request.UnitId == Guid.Empty)
        {
            throw new ValidationException("unitId", "An organiser unit is required.");
        }

        var title = RequireText(request.Title, "title", "An event title is required.");
        if (request.EndsAtUtc <= request.StartsAtUtc)
        {
            throw new ValidationException("endsAtUtc", "The event end must be after its start.");
        }

        var capacity = RequirePositive(request.Capacity, "capacity", "The event capacity must be positive.");

        var residentEvent = new DomainResidentEvent
        {
            CommunityId = request.CommunityId,
            UnitId = request.UnitId,
            OrganizerResidentId = request.OrganizerResidentId,
            Title = title,
            Description = Trimmed(request.Description),
            Location = Trimmed(request.Location),
            StartsAtUtc = request.StartsAtUtc,
            EndsAtUtc = request.EndsAtUtc,
            Capacity = capacity,
            RegisteredCount = 0,
            Status = DomainResidentEventStatuses.Published,
        };

        db.ResidentEvents.Add(residentEvent);

        await audit.AppendAsync(
            new AuditEntry("Create", "ResidentEvent", residentEvent.Id.ToString(), residentEvent.CommunityId,
                Detail: $"title={title}; capacity={capacity}; startsAt={request.StartsAtUtc:O}"),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(residentEvent);
    }

    public async Task<ResidentEventRegistrationDto> RegisterForResidentEventAsync(RegisterForResidentEventRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 41.5): a resident registers with Create on ResidentEvent.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "ResidentEvent", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to register for a resident event.");
        }

        if (request.ResidentId == Guid.Empty)
        {
            throw new ValidationException("residentId", "A registering resident is required.");
        }

        if (request.UnitId == Guid.Empty)
        {
            throw new ValidationException("unitId", "A registering unit is required.");
        }

        // Load the event tenant-scoped (the global filter hides foreign/archived rows → not found).
        // AsNoTracking: the registration row + the event counter are the only things this flow writes;
        // the event is read-only input (its counter is mutated via the atomic guarded UPDATE below,
        // not the change tracker).
        var residentEvent = await db.ResidentEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == request.ResidentEventId, cancellationToken)
            ?? throw new NotFoundException("Resident event not found.");

        authorizationGuard.EnsureCommunityAuthorized(residentEvent.CommunityId);

        if (!string.Equals(residentEvent.Status, DomainResidentEventStatuses.Published, StringComparison.Ordinal))
        {
            throw new ValidationException("residentEventId", "The event is not open for registration.");
        }

        // Atomic guarded increment (Req 41.5): the database evaluates RegisteredCount < Capacity and
        // applies +1 in ONE statement. affected == 1 → a seat was claimed; affected == 0 → the event is
        // full. Two residents racing the last seat serialize at the row; exactly one wins — no oversell,
        // no lost update, no read-then-write (steering: DB-level concurrency guarantee). This is the
        // SAME mechanism as the booking lifecycle's SlotBookingCount.
        var seatClaimed = await db.ResidentEvents
            .Where(e => e.Id == residentEvent.Id && e.RegisteredCount < e.Capacity)
            .ExecuteUpdateAsync(set => set.SetProperty(e => e.RegisteredCount, e => e.RegisteredCount + 1), cancellationToken);

        if (seatClaimed == 0)
        {
            // At capacity (Req 41.5): a clean validation error, no registration row written, the
            // counter untouched.
            throw new ValidationException("residentEventId", "The event has reached its capacity.");
        }

        // Seat is held. Record the registration; if persisting it fails (including a duplicate
        // registration losing the Unique(ResidentEventId, ResidentId) race → CONFLICT), release the
        // seat so a claimed-but-unrecorded seat never leaks capacity.
        var registration = new DomainResidentEventRegistration
        {
            CommunityId = residentEvent.CommunityId,
            UnitId = request.UnitId,
            ResidentEventId = residentEvent.Id,
            ResidentId = request.ResidentId,
            RegisteredAtUtc = clock.UtcNow,
            IsCancelled = false,
        };

        db.ResidentEventRegistrations.Add(registration);

        await audit.AppendAsync(
            new AuditEntry("Create", "ResidentEventRegistration", registration.Id.ToString(), registration.CommunityId,
                Detail: $"eventId={residentEvent.Id}; residentId={request.ResidentId}"),
            cancellationToken);

        // Notify the registering resident (Req 41.5). EnqueueAsync only Adds Pending outbox rows to
        // THIS DbContext (no save), so the single SaveChanges below commits registration + audit +
        // notification atomically (transactional outbox).
        await EnqueueResidentEventNoticeAsync(registration, residentEvent, cancellationToken);

        try
        {
            // The Unique(ResidentEventId, ResidentId) index is the source of truth for one registration
            // per resident per event: a concurrent duplicate → CONFLICT (no read-then-write existence
            // check).
            await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
                () => db.SaveChangesAsync(cancellationToken),
                "You are already registered for this event.");
        }
        catch
        {
            // The registration did not persist (duplicate or other failure) → return the held seat with
            // the symmetric guarded decrement so capacity is never consumed by a phantom registration.
            // ExecuteUpdateAsync is independent of the failed change-tracker save, so it is not undone
            // by the rollback above.
            await db.ResidentEvents
                .Where(e => e.Id == residentEvent.Id && e.RegisteredCount > 0)
                .ExecuteUpdateAsync(set => set.SetProperty(e => e.RegisteredCount, e => e.RegisteredCount - 1), CancellationToken.None);
            throw;
        }

        return ToDto(registration);
    }

    public async Task CancelResidentEventRegistrationAsync(Guid registrationId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "ResidentEvent", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to cancel an event registration.");
        }

        // TRACKED load through the tenant + resident self-scope filter: a resident principal resolves
        // ONLY their own registration, so a resident cancels only their own (Req 41.5) — a
        // foreign/other-unit registration is not-found. The RowVersion rides the UPDATE (a double-cancel
        // loser → CONFLICT), so the seat is released exactly once.
        var registration = await db.ResidentEventRegistrations.FirstOrDefaultAsync(r => r.Id == registrationId, cancellationToken)
            ?? throw new NotFoundException("Event registration not found.");

        authorizationGuard.EnsureCommunityAuthorized(registration.CommunityId);

        if (registration.IsCancelled)
        {
            // Already cancelled → nothing to release; idempotent no-op (the seat was returned once).
            return;
        }

        // Release the held seat back to the event counter EXACTLY ONCE. The guarded decrement
        // (RegisteredCount > 0) runs immediately; the IsCancelled flag + the registration RowVersion
        // make the whole cancel record once, so a concurrent double-cancel loses the RowVersion check
        // (CONFLICT) and never decrements twice.
        await db.ResidentEvents
            .Where(e => e.Id == registration.ResidentEventId && e.RegisteredCount > 0)
            .ExecuteUpdateAsync(set => set.SetProperty(e => e.RegisteredCount, e => e.RegisteredCount - 1), cancellationToken);

        registration.IsCancelled = true;

        await audit.AppendAsync(
            new AuditEntry("Cancel", "ResidentEventRegistration", registration.Id.ToString(), registration.CommunityId,
                Detail: $"eventId={registration.ResidentEventId}"),
            cancellationToken);

        // RowVersion makes the cancel (and the single seat release above) record exactly once.
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ResidentEventDto?> GetResidentEventAsync(Guid residentEventId, CancellationToken cancellationToken = default)
    {
        // Tenant-scoped AsNoTracking read (Req 41.4): an unknown/foreign/soft-deleted id → null. Note
        // ResidentEvent is IUnitScoped, so a resident principal reads only their own unit's events here
        // — the community-wide browse surface is ListResidentEventsAsync (View on ResidentEvent).
        var residentEvent = await db.ResidentEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == residentEventId, cancellationToken);
        return residentEvent is null ? null : ToDto(residentEvent);
    }

    public async Task<IReadOnlyList<ResidentEventDto>> ListResidentEventsAsync(Guid communityId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "ResidentEvent", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view resident events.");
        }

        // Tenant-scoped AsNoTracking read riding the (CommunityId, StartsAtUtc) index, ordered by start
        // (Req 41.4). A foreign/unknown community's events are hidden by the global filter → empty list.
        var events = await db.ResidentEvents.AsNoTracking()
            .Where(e => e.CommunityId == communityId)
            .OrderBy(e => e.StartsAtUtc).ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

        return events.Select(ToDto).ToList();
    }

    /// <summary>
    /// Enqueue the event-registration confirmation to the registering resident (Req 41.5). Resolves
    /// the resident's linked login user with one tenant-scoped indexed read; a resident with no
    /// platform account has no push target → nothing is enqueued. Only Adds the outbox row; the
    /// caller's SaveChanges commits it in the same transaction.
    /// </summary>
    private async Task EnqueueResidentEventNoticeAsync(DomainResidentEventRegistration registration, DomainResidentEvent residentEvent, CancellationToken cancellationToken)
    {
        var residentUserId = await ResolveResidentUserIdAsync(registration.ResidentId, cancellationToken);
        if (residentUserId is not { } userId)
        {
            return;
        }

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["eventTitle"] = residentEvent.Title,
            ["startsAt"] = residentEvent.StartsAtUtc.ToString("yyyy-MM-dd HH:mm'Z'"),
        };

        await notifications.EnqueueAsync(
            new NotificationRequest(NotificationTemplateKeys.ResidentEventRegistered, userId, residentEvent.CommunityId, Placeholders: placeholders),
            cancellationToken);
    }

    /// <summary>
    /// Resolve (creating on first use) the per-(<paramref name="slot"/>, <paramref name="bookingDate"/>)
    /// <see cref="DomainSlotBookingCount"/> row and return its id (Req 39.2, 39.5). The
    /// <c>Unique(FacilitySlotId, BookingDate)</c> index makes the lazy create race-safe: a concurrent
    /// duplicate insert → CONFLICT (swallowed), after which the winner's row is re-read. No
    /// read-then-write existence gap. The capacity is snapshotted from the slot so the guarded
    /// increment compares against a stable per-date ceiling.
    /// </summary>
    private async Task<Guid> EnsureSlotBookingCountAsync(DomainFacilitySlot slot, DateTime bookingDate, CancellationToken cancellationToken)
    {
        var existingId = await db.SlotBookingCounts.AsNoTracking()
            .Where(c => c.FacilitySlotId == slot.Id && c.BookingDate == bookingDate)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingId is { } id)
        {
            return id;
        }

        var counter = new DomainSlotBookingCount
        {
            CommunityId = slot.CommunityId,
            AmenityId = slot.AmenityId,
            FacilitySlotId = slot.Id,
            BookingDate = bookingDate,
            BookedCount = 0,
            Capacity = slot.Capacity,
        };

        db.SlotBookingCounts.Add(counter);

        try
        {
            // Save ONLY the new counter row on its own so a duplicate-insert race is isolated and
            // translated to CONFLICT without touching the booking (which is added after this returns).
            await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
                () => db.SaveChangesAsync(cancellationToken),
                "The slot capacity counter already exists.");
            return counter.Id;
        }
        catch (ConflictException)
        {
            // A concurrent request won the create race: detach our losing row and re-read the winner's.
            db.Entry(counter).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            var winnerId = await db.SlotBookingCounts.AsNoTracking()
                .Where(c => c.FacilitySlotId == slot.Id && c.BookingDate == bookingDate)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(cancellationToken);

            return winnerId ?? throw new ConflictException("The slot capacity counter could not be resolved.");
        }
    }

    /// <summary>
    /// Enqueue the booking-created notice to the booking resident, and — when the booking requires
    /// approval — the approval-needed notice to the amenity's approvers (Req 39.3, 39.4). Only Adds
    /// outbox rows; the caller's SaveChanges commits them in the same transaction.
    /// </summary>
    private async Task EnqueueBookingCreatedAsync(DomainBooking booking, DomainAmenity amenity, CancellationToken cancellationToken)
    {
        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["amenityName"] = amenity.Name,
            ["bookingDate"] = booking.BookingDate.ToString("yyyy-MM-dd"),
            ["status"] = booking.Status,
        };

        var residentUserId = await ResolveResidentUserIdAsync(booking.ResidentId, cancellationToken);
        if (residentUserId is { } userId)
        {
            await notifications.EnqueueAsync(
                new NotificationRequest(NotificationTemplateKeys.BookingCreated, userId, booking.CommunityId, Placeholders: placeholders),
                cancellationToken);
        }

        if (!booking.RequiresApproval)
        {
            return;
        }

        // Approval-needed to the amenity's approvers = users whose (community-scoped or platform-wide)
        // roles grant Approve on Booking. Permission/Role/UserRole are platform-scoped (no tenant
        // filter), so the community predicate on the UserRole scope is explicit — the same audience
        // resolution Parcel/SecurityOps use for admin notices.
        var approverUserIds = await (
            from ur in db.UserRoles.AsNoTracking()
            where ur.CommunityId == booking.CommunityId || ur.CommunityId == null
            join rp in db.RolePermissions.AsNoTracking() on ur.RoleId equals rp.RoleId
            join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            where p.ResourceType == "Booking" && p.Action == PermissionAction.Approve
            select ur.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var approvalPlaceholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["amenityName"] = amenity.Name,
            ["bookingDate"] = booking.BookingDate.ToString("yyyy-MM-dd"),
        };

        foreach (var approverId in approverUserIds)
        {
            await notifications.EnqueueAsync(
                new NotificationRequest(NotificationTemplateKeys.BookingApprovalNeeded, approverId, booking.CommunityId, Placeholders: approvalPlaceholders),
                cancellationToken);
        }
    }

    /// <summary>
    /// Enqueue a single booking notice (confirmed / cancelled) to the booking resident (Req 39.4).
    /// Resolves the amenity name for the placeholder with one tenant-scoped indexed read. Only Adds
    /// the outbox row; the caller's SaveChanges commits it in the same transaction.
    /// </summary>
    private async Task EnqueueBookingResidentNoticeAsync(DomainBooking booking, string templateKey, CancellationToken cancellationToken)
    {
        var residentUserId = await ResolveResidentUserIdAsync(booking.ResidentId, cancellationToken);
        if (residentUserId is not { } userId)
        {
            return;
        }

        var amenityName = await db.Amenities.AsNoTracking()
            .Where(a => a.Id == booking.AmenityId)
            .Select(a => a.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["amenityName"] = amenityName,
            ["bookingDate"] = booking.BookingDate.ToString("yyyy-MM-dd"),
        };

        await notifications.EnqueueAsync(
            new NotificationRequest(templateKey, userId, booking.CommunityId, Placeholders: placeholders),
            cancellationToken);
    }

    /// <summary>
    /// Resolve the booking resident's linked login user id for a notification recipient (Req 39.4),
    /// or null when the resident has no platform account (no push target → nothing to enqueue). One
    /// tenant-scoped indexed read.
    /// </summary>
    private async Task<Guid?> ResolveResidentUserIdAsync(Guid residentId, CancellationToken cancellationToken) =>
        await db.Residents.AsNoTracking()
            .Where(r => r.Id == residentId && r.UserId != null)
            .Select(r => r.UserId)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Require that the <paramref name="from"/>-&gt;<paramref name="to"/> transition is present (and
    /// active) in the editable <c>Booking_Status_Transition</c> allow-list for the community (Req
    /// 39.3). Each entry's code is a <c>from-&gt;to</c> pair of Booking_Status codes; a transition not
    /// in the list is rejected — no hardcoded transition table. Mirrors the Ticket/Parcel/WorkOrder
    /// state machines.
    /// </summary>
    private async Task RequireAllowedTransitionAsync(string from, string to, Guid communityId, CancellationToken cancellationToken)
    {
        var pair = $"{from}->{to}";
        var allowed = await config.GetMasterDataAsync(MasterDataKeys.BookingStatusTransition, communityId, cancellationToken: cancellationToken);
        if (!allowed.Any(e => string.Equals(e.Code, pair, StringComparison.Ordinal)))
        {
            throw new ValidationException(
                "status",
                $"A booking cannot move from '{from}' to '{to}'; the transition is not allowed.");
        }
    }

    /// <summary>Parse the acting user id (from the tenant context) into a nullable Guid; a system/anonymous actor → null.</summary>
    private Guid? ParseActingUserId() =>
        Guid.TryParse(tenant.UserId, out var id) ? id : null;

    // =============================================================================================
    // Helpers.
    // =============================================================================================

    /// <summary>
    /// Resolve the community id of an amenity tenant-scoped (Req 38.2-38.4), rejecting an unknown /
    /// foreign-tenant id. The returned community anchors the slot/rule/blackout so each is always
    /// owned by the same community as its amenity (same-community by construction).
    /// </summary>
    private async Task<Guid> RequireAmenityCommunityAsync(Guid amenityId, CancellationToken cancellationToken)
    {
        var communityId = await db.Amenities.AsNoTracking()
            .Where(a => a.Id == amenityId)
            .Select(a => (Guid?)a.CommunityId)
            .FirstOrDefaultAsync(cancellationToken);

        return communityId ?? throw new ValidationException(
            "amenityId", "The amenity was not found in your authorized communities.");
    }

    /// <summary>
    /// Validate a daily window (Req 38.2): both endpoints in range and start strictly before end. A
    /// zero/negative/overflowing window → VALIDATION_ERROR.
    /// </summary>
    private static void ValidateWindow(int startMinutes, int endMinutes)
    {
        if (startMinutes < 0 || startMinutes >= MinutesPerDay)
        {
            throw new ValidationException("startMinutesOfDay", "The window start must be between 0 and 1439 minutes.");
        }

        if (endMinutes <= 0 || endMinutes > MinutesPerDay)
        {
            throw new ValidationException("endMinutesOfDay", "The window end must be between 1 and 1440 minutes.");
        }

        if (endMinutes <= startMinutes)
        {
            throw new ValidationException("endMinutesOfDay", "The window end must be after the window start.");
        }
    }

    /// <summary>
    /// Validate a weekday bitmask (Req 38.2): at least one bit set within the 7-day range (bits
    /// 0..6). A mask with no days or out-of-range bits → VALIDATION_ERROR.
    /// </summary>
    private static int RequireDaysOfWeekMask(int mask)
    {
        const int allDays = (1 << 7) - 1; // bits 0..6
        if (mask <= 0 || (mask & ~allDays) != 0)
        {
            throw new ValidationException("daysOfWeekMask", "The slot must apply to at least one valid weekday.");
        }

        return mask;
    }

    /// <summary>
    /// Validate an optional CSV of <c>Resident_Type</c> codes for eligibility (Req 38.3): null/blank
    /// → null ("all eligible"); otherwise each code is trimmed and validated against the editable
    /// Resident_Type list (an unknown code → VALIDATION_ERROR). Returns the normalized CSV. Never a
    /// hardcoded eligibility rule.
    /// </summary>
    private async Task<string?> ValidateResidentTypesCsvAsync(string? csv, Guid communityId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return null;
        }

        var codes = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (codes.Length == 0)
        {
            return null;
        }

        var allowed = await config.GetMasterDataAsync(MasterDataKeys.ResidentType, communityId, cancellationToken: cancellationToken);
        var allowedCodes = allowed.Select(e => e.Code).ToHashSet(StringComparer.Ordinal);

        var normalized = new List<string>(codes.Length);
        foreach (var code in codes)
        {
            if (!allowedCodes.Contains(code))
            {
                throw new ValidationException("allowedResidentTypes", $"'{code}' is not a valid resident type.");
            }

            if (!normalized.Contains(code, StringComparer.Ordinal))
            {
                normalized.Add(code);
            }
        }

        return string.Join(',', normalized);
    }

    /// <summary>
    /// Validate a code against a configurable master-data list resolved for the community (Req 38.1)
    /// — not a frozen enum. Returns the trimmed code; an unknown code → VALIDATION_ERROR. Mirrors
    /// MaintenanceService.RequireValidCodeAsync.
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

    private static int RequirePositive(int value, string field, string message)
    {
        if (value <= 0)
        {
            throw new ValidationException(field, message);
        }

        return value;
    }

    private static int? ValidateOptionalPositive(int? value, string field, string message)
    {
        if (value is { } v && v <= 0)
        {
            throw new ValidationException(field, message);
        }

        return value;
    }

    private static int? ValidateOptionalNonNegative(int? value, string field, string message)
    {
        if (value is { } v && v < 0)
        {
            throw new ValidationException(field, message);
        }

        return value;
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

    private static AmenityDto ToDto(DomainAmenity a) =>
        new(a.Id, a.CommunityId, a.Name, a.Code, a.AmenityType, a.Status, a.Location, a.Description, a.DefaultCapacity, a.CreatedAtUtc);

    private static FacilitySlotDto ToDto(DomainFacilitySlot s) =>
        new(s.Id, s.CommunityId, s.AmenityId, s.Name, s.StartMinutesOfDay, s.EndMinutesOfDay, s.DaysOfWeekMask, s.Capacity, s.IsActive, s.CreatedAtUtc);

    private static BookingRuleDto ToDto(DomainBookingRule r) =>
        new(r.Id, r.CommunityId, r.AmenityId, r.AllowedResidentTypes, r.AdvanceBookingWindowDays, r.MinDurationMinutes,
            r.MaxDurationMinutes, r.MaxBookingsPerResident, r.MaxBookingsPeriodDays, r.RequiresApproval, r.BookingCharge,
            r.SecurityDeposit, r.CancellationNoticeHours, r.RefundPercentOnCancellation);

    private static BlackoutDateDto ToDto(DomainBlackoutDate b) =>
        new(b.Id, b.CommunityId, b.AmenityId, b.StartUtc, b.EndUtc, b.Reason, b.CreatedAtUtc);

    private static BookingDto ToDto(DomainBooking b) =>
        new(b.Id, b.CommunityId, b.UnitId, b.AmenityId, b.FacilitySlotId, b.ResidentId, b.BookingDate,
            b.Status, b.RequiresApproval, b.BookingCharge, b.SecurityDeposit, b.ApprovedByUserId,
            b.ConfirmedAtUtc, b.CancelledAtUtc, b.CreatedAtUtc);

    private static ResidentEventDto ToDto(DomainResidentEvent e) =>
        new(e.Id, e.CommunityId, e.UnitId, e.OrganizerResidentId, e.Title, e.Description, e.Location,
            e.StartsAtUtc, e.EndsAtUtc, e.Capacity, e.RegisteredCount, e.Status, e.CreatedAtUtc);

    private static ResidentEventRegistrationDto ToDto(DomainResidentEventRegistration r) =>
        new(r.Id, r.CommunityId, r.ResidentEventId, r.ResidentId, r.UnitId, r.RegisteredAtUtc, r.IsCancelled);
}
