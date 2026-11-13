using System.Security.Cryptography;
using App.Application.Abstractions;
using App.Application.Abstractions.Security;
using App.Application.Audit;
using App.Application.Config;
using App.Application.Exceptions;
using App.Application.Maintenance;
using App.Application.Notifications;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using DomainAsset = App.Domain.Maintenance.Asset;
using DomainAssetStatuses = App.Domain.Maintenance.AssetStatuses;
using DomainMaintenancePlan = App.Domain.Maintenance.MaintenancePlan;
using DomainMaintenancePlanFrequencies = App.Domain.Maintenance.MaintenancePlanFrequencies;
using DomainWorkOrder = App.Domain.Maintenance.WorkOrder;
using DomainWorkOrderStatuses = App.Domain.Maintenance.WorkOrderStatuses;
using DomainWorkOrderClassification = App.Domain.Maintenance.WorkOrderClassification;
using DomainWorkOrderPart = App.Domain.Maintenance.WorkOrderPart;
using DomainWorkOrderLabour = App.Domain.Maintenance.WorkOrderLabour;
using DomainEquipmentHistoryEntry = App.Domain.Maintenance.EquipmentHistoryEntry;
using DomainEquipmentHistoryEventTypes = App.Domain.Maintenance.EquipmentHistoryEventTypes;
using DomainInspectionChecklist = App.Domain.Maintenance.InspectionChecklist;
using DomainInspectionChecklistItem = App.Domain.Maintenance.InspectionChecklistItem;
using DomainAmcContract = App.Domain.Maintenance.AmcContract;
using DomainAmcContractStatuses = App.Domain.Maintenance.AmcContractStatuses;
using DomainMeterReading = App.Domain.Maintenance.MeterReading;

namespace App.Infrastructure.Maintenance;

/// <summary>
/// EF-backed <see cref="IMaintenanceService"/> (Task 21.1, Req 35.1–35.4): the asset register with
/// QR identification, a configurable lifecycle status and the tenant-scoped read surface. This is
/// the single cohesive Maintenance seam — work orders (Task 21.2), inspections / AMC / meters
/// (Task 21.3) and the HTTP controllers (Task 21.4) extend it in later tasks (mirrors how
/// <c>HelpdeskService</c> grows across its tasks).
///
/// <para><b>Registration (Req 35.1, 35.2).</b> A Facility Manager with <c>Create</c> on <c>Asset</c>
/// registers an asset for an authorized community: the type/category validate against the editable
/// <c>Asset_Type</c> / <c>Asset_Category</c> lists (no frozen enums), the status starts <c>active</c>,
/// and an opaque, community-unique QR token is generated. Serial-number uniqueness within the
/// community and QR-token uniqueness are DB guarantees (unique indexes) — the service lets the DB
/// decide the winner (regenerate-on-collision for the QR token; <c>UniqueConstraintGuard</c> maps a
/// duplicate serial to CONFLICT) rather than a read-then-write pre-check (steering: concurrency
/// safety via DB-level guarantees).</para>
///
/// <para><b>Lifecycle + edit (Req 35.1).</b> An <c>Edit</c> loads the asset TRACKED so EF carries the
/// <c>RowVersion</c> into the UPDATE: the update records exactly once under concurrency (a loser →
/// CONFLICT via the central mapping in <see cref="AppDbContext.SaveChangesAsync"/>). Any supplied
/// status is validated as an active <c>Asset_Status</c> code (21.1 does not enforce a transition
/// allow-list for the asset lifecycle).</para>
///
/// <para><b>Tenant scope + soft-delete (Req 35.3, 35.4).</b> <c>Asset</c> is tenant-owned (NOT
/// <c>IUnitScoped</c>), so the global tenant filter bounds every read to the caller's authorized
/// communities and hides soft-deleted rows. Reads use <c>AsNoTracking</c>; the list rides the shared
/// allow-listed <c>ListQueryConfiguration</c> + <c>ToPagedResultAsync</c>. A delete calls
/// <c>Remove</c>, which the <c>AuditSaveChangesInterceptor</c> converts into a soft delete so the row
/// stays traceable. All mutations are audited.</para>
/// </summary>
public sealed class MaintenanceService(
    AppDbContext db,
    IAuditService audit,
    IConfigService config,
    IClock clock,
    IAuthorizationGuard authorizationGuard,
    INotificationService notifications,
    ITenantContext tenant,
    IPageSizePolicy pageSizePolicy,
    IAsyncQueryExecutor executor)
    : IMaintenanceService
{
    // Allow-list for the Task 21.4 asset list endpoint (Req 35.3). Only these fields reach the query
    // — no arbitrary client LINQ (steering: no hardcoding, injection-safe). Sorts/filters ride the
    // asset's indexed columns; default is newest-registered-first. Mirrors ParcelQueryConfig.
    private static readonly ListQueryConfiguration<DomainAsset> AssetQueryConfig =
        new ListQueryConfiguration<DomainAsset>()
            .WithDefaultSort(a => a.CreatedAtUtc, SortDirection.Descending)
            .Sortable("createdAtUtc", a => a.CreatedAtUtc)
            .Sortable("name", a => a.Name)
            .Sortable("serialNumber", a => a.SerialNumber)
            .Sortable("status", a => a.Status)
            .Sortable("assetType", a => a.AssetType)
            .Filterable("status", a => a.Status)
            .Filterable("assetType", a => a.AssetType)
            .Filterable("category", a => a.Category)
            .Filterable("unitId", a => a.UnitId)
            .Searchable(a => a.Name)
            .Searchable(a => a.SerialNumber)
            .Searchable(a => a.Location);

    // Allow-list for the Task 21.4 maintenance-plan list endpoint (Req 36.1). Only these fields reach
    // the query — no arbitrary client LINQ (steering: no hardcoding, injection-safe). Mirrors AssetQueryConfig.
    private static readonly ListQueryConfiguration<DomainMaintenancePlan> MaintenancePlanQueryConfig =
        new ListQueryConfiguration<DomainMaintenancePlan>()
            .WithDefaultSort(p => p.NextDueAtUtc, SortDirection.Ascending)
            .Sortable("nextDueAtUtc", p => p.NextDueAtUtc)
            .Sortable("createdAtUtc", p => p.CreatedAtUtc)
            .Sortable("name", p => p.Name)
            .Sortable("frequency", p => p.Frequency)
            .Filterable("assetId", p => p.AssetId)
            .Filterable("frequency", p => p.Frequency)
            .Filterable("priority", p => p.Priority)
            .Filterable("isActive", p => p.IsActive)
            .Searchable(p => p.Name);

    // Allow-list for the Task 21.4 work-order list endpoint (Req 36.2, 36.3). Mirrors AssetQueryConfig.
    private static readonly ListQueryConfiguration<DomainWorkOrder> WorkOrderQueryConfig =
        new ListQueryConfiguration<DomainWorkOrder>()
            .WithDefaultSort(w => w.CreatedAtUtc, SortDirection.Descending)
            .Sortable("createdAtUtc", w => w.CreatedAtUtc)
            .Sortable("status", w => w.Status)
            .Sortable("priority", w => w.Priority)
            .Sortable("workOrderType", w => w.WorkOrderType)
            .Filterable("status", w => w.Status)
            .Filterable("priority", w => w.Priority)
            .Filterable("workOrderType", w => w.WorkOrderType)
            .Filterable("assetId", w => w.AssetId)
            .Filterable("assignedToUserId", w => w.AssignedToUserId)
            .Filterable("maintenancePlanId", w => w.MaintenancePlanId)
            .Searchable(w => w.Title)
            .Searchable(w => w.Description);

    // Default Work_Order_Priority (Req 36.2) when the request omits one and
    // maintenance.workOrder.defaultPriority is unset in Config_Service — a safe fallback, not a
    // hardcoded decision: a community overrides the key without a code change (steering: no hardcoding).
    private const string DefaultWorkOrderPriority = DomainWorkOrderClassification.PriorityMedium;

    // Per-frequency interval-day fallbacks when maintenance.plan.intervalDays.{frequency} is unset in
    // Config_Service (Req 36.1) — safe defaults, not a hardcoded decision: a community overrides the
    // per-frequency key to tune its spacing without a code change (steering: no hardcoding). A `custom`
    // frequency ignores these and uses the plan's own supplied interval. Mirrors the Ticket per-priority
    // SLA fallback table.
    private static readonly IReadOnlyDictionary<string, int> DefaultIntervalDaysByFrequency =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [DomainMaintenancePlanFrequencies.Daily] = 1,
            [DomainMaintenancePlanFrequencies.Weekly] = 7,
            [DomainMaintenancePlanFrequencies.Monthly] = 30,
            [DomainMaintenancePlanFrequencies.Quarterly] = 90,
            [DomainMaintenancePlanFrequencies.HalfYearly] = 182,
            [DomainMaintenancePlanFrequencies.Yearly] = 365,
        };

    // Hard upper bound on how many due plans a single preventive-generation sweep call processes,
    // regardless of the worker-supplied batch size — keeps the sweep's work bounded so it stays
    // correct and the app stays up under a large backlog (steering: bounded work / scalable). Mirrors
    // the Ticket/Parcel SLA sweep caps; the worker passes its configured batch size and this clamps it.
    private const int MaxPreventiveSweepBatch = 500;

    // Hard upper bound on how many AMC contracts a single expiry-reminder sweep call processes,
    // regardless of the worker-supplied batch size — keeps the sweep's work bounded (steering: bounded
    // work / scalable). Mirrors MaxPreventiveSweepBatch / the ticket SLA sweep cap.
    private const int MaxAmcSweepBatch = 500;

    // Seeded fallback for the AMC expiry-reminder window (days before expiry) when
    // maintenance.amc.expiryReminderDays is unset/invalid in Config_Service (Req 37.3) — a safe
    // default, not a hardcoded decision: a community overrides the key to tune its lead time without a
    // code change (steering: no hardcoding).
    private const int DefaultAmcExpiryReminderDays = 30;

    private const string DuplicateSerialNumberMessage =
        "An asset with the same serial number already exists in this community.";

    // How many QR-token generations to try before giving up on a (vanishingly unlikely) collision —
    // same bound GateService/ParcelService use for their opaque QR tokens.
    private const int QrTokenAttempts = 5;

    public async Task<AssetDto> RegisterAssetAsync(RegisterAssetRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 35.1): a Facility Manager registers an asset with Create on Asset.
        // The Task 21.4 endpoint gate declares the same; the invariant must hold regardless of
        // caller — no permission → FORBIDDEN with nothing mutated.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "Asset", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to register an asset.");
        }

        authorizationGuard.EnsureCommunityAuthorized(request.CommunityId);

        var name = RequireText(request.Name, "name", "An asset name is required.");
        var serialNumber = RequireText(request.SerialNumber, "serialNumber", "A serial number is required.");
        var location = RequireText(request.Location, "location", "An asset location is required.");

        var assetType = await RequireValidCodeAsync(
            request.AssetType, MasterDataKeys.AssetType, request.CommunityId, "assetType", "asset type", cancellationToken);
        var category = await ValidateOptionalCodeAsync(
            request.Category, MasterDataKeys.AssetCategory, request.CommunityId, "category", "asset category", cancellationToken);

        // Optional tied unit must belong to the asset's community (Req 35.1). The global tenant filter
        // already hides foreign-tenant units, so a cross-community/unknown id is a validation error.
        await RequireUnitInCommunityAsync(request.UnitId, request.CommunityId, cancellationToken);

        var asset = new DomainAsset
        {
            CommunityId = request.CommunityId,
            Name = name,
            SerialNumber = serialNumber,
            Location = location,
            AssetType = assetType,
            Category = category,
            Status = DomainAssetStatuses.Active,
            UnitId = request.UnitId,
            WarrantyProvider = Trimmed(request.WarrantyProvider),
            WarrantyReference = Trimmed(request.WarrantyReference),
            WarrantyStartUtc = request.WarrantyStartUtc,
            WarrantyEndUtc = request.WarrantyEndUtc,
            InstalledOnUtc = request.InstalledOnUtc,
            Notes = Trimmed(request.Notes),
        };

        db.Assets.Add(asset);

        await audit.AppendAsync(
            new AuditEntry("Create", "Asset", asset.Id.ToString(), asset.CommunityId, Detail: null),
            cancellationToken);

        // Opaque, community-unique QR token (Req 35.2) — the DB Unique(CommunityId, QrToken) index is
        // the source of truth; on the (astronomically unlikely) collision we regenerate and retry
        // rather than a read-then-write pre-check (same approach as the visit-pass / parcel QR token).
        // A duplicate serial surfaces as the OTHER unique index on this insert and is mapped to a
        // clearer CONFLICT message by SaveWithUniqueAssetAsync.
        await SaveWithUniqueAssetAsync(asset, cancellationToken);

        return ToDto(asset);
    }

    public async Task<AssetDto> UpdateAssetAsync(Guid assetId, UpdateAssetRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 35.1): editing an asset / driving its lifecycle is an Edit on Asset.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "Asset", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to edit an asset.");
        }

        // TRACKED load: we mutate the row and need the RowVersion carried into the UPDATE so the
        // edit records exactly once under concurrency. The global tenant filter restricts this to the
        // caller's authorized communities (and hides soft-deleted rows), so a foreign-tenant/archived
        // asset is a not-found.
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken)
            ?? throw new NotFoundException("Asset not found.");

        authorizationGuard.EnsureCommunityAuthorized(asset.CommunityId);

        if (request.Name is not null)
        {
            asset.Name = RequireText(request.Name, "name", "An asset name is required.");
        }

        if (request.Location is not null)
        {
            asset.Location = RequireText(request.Location, "location", "An asset location is required.");
        }

        if (request.AssetType is not null)
        {
            asset.AssetType = await RequireValidCodeAsync(
                request.AssetType, MasterDataKeys.AssetType, asset.CommunityId, "assetType", "asset type", cancellationToken);
        }

        if (request.Category is not null)
        {
            asset.Category = await ValidateOptionalCodeAsync(
                request.Category, MasterDataKeys.AssetCategory, asset.CommunityId, "category", "asset category", cancellationToken);
        }

        if (request.Status is not null)
        {
            // 21.1 validates the status as an active Asset_Status code (no transition allow-list for
            // the asset lifecycle — the requirement needs a tracked state, not transition validation).
            asset.Status = await RequireValidCodeAsync(
                request.Status, MasterDataKeys.AssetStatus, asset.CommunityId, "status", "asset status", cancellationToken);
        }

        // ClearUnit wins over UnitId: an explicit detach sets UnitId null; otherwise a supplied UnitId
        // is validated and (re)assigned, and a null UnitId with ClearUnit=false leaves it unchanged.
        if (request.ClearUnit)
        {
            asset.UnitId = null;
        }
        else if (request.UnitId is { } unitId)
        {
            await RequireUnitInCommunityAsync(unitId, asset.CommunityId, cancellationToken);
            asset.UnitId = unitId;
        }

        if (request.WarrantyProvider is not null)
        {
            asset.WarrantyProvider = Trimmed(request.WarrantyProvider);
        }

        if (request.WarrantyReference is not null)
        {
            asset.WarrantyReference = Trimmed(request.WarrantyReference);
        }

        if (request.WarrantyStartUtc is not null)
        {
            asset.WarrantyStartUtc = request.WarrantyStartUtc;
        }

        if (request.WarrantyEndUtc is not null)
        {
            asset.WarrantyEndUtc = request.WarrantyEndUtc;
        }

        if (request.InstalledOnUtc is not null)
        {
            asset.InstalledOnUtc = request.InstalledOnUtc;
        }

        if (request.Notes is not null)
        {
            asset.Notes = Trimmed(request.Notes);
        }

        await audit.AppendAsync(
            new AuditEntry("Update", "Asset", asset.Id.ToString(), asset.CommunityId, Detail: null),
            cancellationToken);

        // RowVersion makes the edit record exactly once under concurrency: the loser's save trips a
        // DbUpdateConcurrencyException mapped centrally to CONFLICT (Req 22.4).
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(asset);
    }

    public async Task<AssetDto?> GetAssetAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        // Tenant-scoped AsNoTracking read (Req 35.3, 35.4): an unknown/foreign/soft-deleted id is
        // hidden by the global filter → null.
        var asset = await db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken);
        return asset is null ? null : ToDto(asset);
    }

    public async Task<PagedData<AssetDto>> ListAssetsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // Defense-in-depth (Req 35.3): listing the register requires View on Asset. The Task 21.4
        // endpoint gate declares the same; the invariant holds regardless of caller.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "Asset", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view assets.");
        }

        // Tenant-scoped by the global query filter (soft-deleted/archived rows hidden); the
        // allow-listed query config fixes the sort/filter/search surface; AsNoTracking projection,
        // page size clamped by the shared policy — mirrors ListParcelsAsync.
        return await db.Assets.AsNoTracking().ToPagedResultAsync(
            parameters, AssetQueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
    }

    public async Task DeleteAssetAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        // Defense-in-depth (Req 35.4): archiving an asset is a Delete on Asset.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Delete, "Asset", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to delete an asset.");
        }

        // TRACKED load so EF carries the RowVersion into the soft-delete UPDATE. The global tenant
        // filter restricts this to the caller's authorized communities (and hides already-deleted
        // rows), so a foreign-tenant/already-archived asset is a not-found.
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken)
            ?? throw new NotFoundException("Asset not found.");

        authorizationGuard.EnsureCommunityAuthorized(asset.CommunityId);

        // Remove is converted into a Soft_Delete by AuditSaveChangesInterceptor (sets IsDeleted +
        // DeletedAtUtc), so the row stays in the table and remains traceable (Req 35.4).
        db.Assets.Remove(asset);

        await audit.AppendAsync(
            new AuditEntry("Delete", "Asset", asset.Id.ToString(), asset.CommunityId, Detail: null),
            cancellationToken);

        // RowVersion makes the soft-delete record exactly once under concurrency (a loser → CONFLICT).
        await db.SaveChangesAsync(cancellationToken);
    }

    // =============================================================================================
    // Task 21.2 — preventive/corrective work orders with parts/labour (Req 36.1-36.5).
    // =============================================================================================

    public async Task<MaintenancePlanDto> CreateMaintenancePlanAsync(CreateMaintenancePlanRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 36.1): defining a preventive plan is a Create on MaintenancePlan.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "MaintenancePlan", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to create a maintenance plan.");
        }

        var name = RequireText(request.Name, "name", "A plan name is required.");

        // The asset anchors the plan's community (same-community by construction). The global tenant
        // filter already hides foreign-tenant assets, so an unknown/foreign id is a validation error.
        var communityId = await RequireAssetCommunityAsync(request.AssetId, cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(communityId);

        var frequency = await RequireValidCodeAsync(
            request.Frequency, MasterDataKeys.MaintenancePlanFrequency, communityId, "frequency", "plan frequency", cancellationToken);
        var priority = await RequireValidCodeAsync(
            request.Priority, MasterDataKeys.WorkOrderPriority, communityId, "priority", "work-order priority", cancellationToken);

        var intervalDays = await ResolveIntervalDaysAsync(frequency, request.CustomIntervalDays, communityId, cancellationToken);
        var firstDue = request.FirstDueAtUtc ?? clock.UtcNow;

        var plan = new DomainMaintenancePlan
        {
            CommunityId = communityId,
            AssetId = request.AssetId,
            Name = name,
            Description = Trimmed(request.Description),
            Frequency = frequency,
            IntervalDays = intervalDays,
            Priority = priority,
            NextDueAtUtc = firstDue,
            IsActive = true,
        };

        db.MaintenancePlans.Add(plan);

        await audit.AppendAsync(
            new AuditEntry("Create", "MaintenancePlan", plan.Id.ToString(), plan.CommunityId,
                Detail: $"assetId={plan.AssetId}; frequency={frequency}; intervalDays={intervalDays}"),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(plan);
    }

    public async Task<MaintenancePlanDto> UpdateMaintenancePlanAsync(Guid planId, UpdateMaintenancePlanRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "MaintenancePlan", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to edit a maintenance plan.");
        }

        // TRACKED load so EF carries the RowVersion into the UPDATE (the edit records exactly once
        // under concurrency). The global tenant filter hides foreign/archived rows → not found.
        var plan = await db.MaintenancePlans.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken)
            ?? throw new NotFoundException("Maintenance plan not found.");

        authorizationGuard.EnsureCommunityAuthorized(plan.CommunityId);

        if (request.Name is not null)
        {
            plan.Name = RequireText(request.Name, "name", "A plan name is required.");
        }

        if (request.Description is not null)
        {
            plan.Description = Trimmed(request.Description);
        }

        if (request.Priority is not null)
        {
            plan.Priority = await RequireValidCodeAsync(
                request.Priority, MasterDataKeys.WorkOrderPriority, plan.CommunityId, "priority", "work-order priority", cancellationToken);
        }

        // Changing the frequency re-resolves the interval (using the supplied custom interval for a
        // custom frequency). A custom-interval change WITHOUT a frequency change re-resolves against
        // the plan's current frequency so a custom plan can be re-spaced.
        if (request.Frequency is not null)
        {
            plan.Frequency = await RequireValidCodeAsync(
                request.Frequency, MasterDataKeys.MaintenancePlanFrequency, plan.CommunityId, "frequency", "plan frequency", cancellationToken);
            plan.IntervalDays = await ResolveIntervalDaysAsync(plan.Frequency, request.CustomIntervalDays, plan.CommunityId, cancellationToken);
        }
        else if (request.CustomIntervalDays is { } customDays)
        {
            plan.IntervalDays = await ResolveIntervalDaysAsync(plan.Frequency, customDays, plan.CommunityId, cancellationToken);
        }

        if (request.NextDueAtUtc is { } nextDue)
        {
            plan.NextDueAtUtc = nextDue;
        }

        if (request.IsActive is { } isActive)
        {
            plan.IsActive = isActive;
        }

        await audit.AppendAsync(
            new AuditEntry("Update", "MaintenancePlan", plan.Id.ToString(), plan.CommunityId, Detail: null),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(plan);
    }

    public async Task<MaintenancePlanDto?> GetMaintenancePlanAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        var plan = await db.MaintenancePlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        return plan is null ? null : ToDto(plan);
    }

    public async Task<PagedData<MaintenancePlanDto>> ListMaintenancePlansAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "MaintenancePlan", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view maintenance plans.");
        }

        return await db.MaintenancePlans.AsNoTracking().ToPagedResultAsync(
            parameters, MaintenancePlanQueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
    }

    public async Task<int> GenerateDuePreventiveWorkOrdersAsync(int maxPlans, CancellationToken cancellationToken = default)
    {
        // Bound the work regardless of the caller-supplied size (steering: bounded work / scalable):
        // process at most MaxPreventiveSweepBatch due plans per call so one pass can't run unbounded
        // under a large backlog. The worker loops to drain the rest across passes. Mirrors the ticket/
        // parcel SLA sweeps.
        var take = Math.Clamp(maxPlans, 1, MaxPreventiveSweepBatch);
        var now = clock.UtcNow;

        // Candidate due plans: active, next occurrence at/before now. TRACKED (we advance the schedule
        // and stamp the plan). The worker runs under a super-admin context, so the global tenant filter
        // does not hide rows; the (CommunityId, IsActive, NextDueAtUtc) index backs the predicate and
        // the page cap keeps memory bounded (no table scan, no N+1).
        var duePlans = await db.MaintenancePlans
            .Where(p => p.IsActive && p.NextDueAtUtc <= now)
            .OrderBy(p => p.NextDueAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        if (duePlans.Count == 0)
        {
            return 0;
        }

        var generated = 0;
        foreach (var plan in duePlans)
        {
            // The occurrence we are generating for is the plan's CURRENT NextDueAtUtc. Capture it
            // before advancing the schedule so the generated work order + the plan bookkeeping both
            // reference the same instant.
            var occurrence = plan.NextDueAtUtc;

            // Idempotency guard against a crash/replay: if a work order for this plan+occurrence
            // already exists (a previous pass generated it but failed to advance the schedule), skip
            // generating a duplicate and just advance the plan. Belt-and-braces on top of the RowVersion
            // race guard below (steering: at-most-once per due occurrence).
            var alreadyGenerated = await db.WorkOrders
                .AnyAsync(w => w.MaintenancePlanId == plan.Id && w.ScheduledForUtc == occurrence, cancellationToken);

            if (!alreadyGenerated)
            {
                var workOrder = new DomainWorkOrder
                {
                    CommunityId = plan.CommunityId,
                    AssetId = plan.AssetId,
                    MaintenancePlanId = plan.Id,
                    Title = plan.Name,
                    Description = plan.Description ?? plan.Name,
                    WorkOrderType = DomainWorkOrderClassification.TypePreventive,
                    Priority = plan.Priority,
                    Status = DomainWorkOrderStatuses.Open,
                    ScheduledForUtc = occurrence,
                };

                db.WorkOrders.Add(workOrder);

                await audit.AppendAsync(
                    new AuditEntry("GeneratePreventive", "WorkOrder", workOrder.Id.ToString(), workOrder.CommunityId,
                        Detail: $"planId={plan.Id}; occurrence={occurrence:O}"),
                    cancellationToken);
            }

            // Advance the schedule and stamp the at-most-once bookkeeping. All under the plan's
            // RowVersion: two concurrent sweeps both read the same occurrence, but only one SaveChanges
            // wins; the loser trips a DbUpdateConcurrencyException mapped centrally to CONFLICT and is
            // skipped below — so the plan can never double-generate for the same occurrence.
            plan.LastGeneratedAtUtc = now;
            plan.LastGeneratedForDueAtUtc = occurrence;
            plan.NextDueAtUtc = AdvanceDue(occurrence, plan.IntervalDays, now);

            try
            {
                // Save per plan so one plan's concurrency loss is isolated — the rest of the batch still
                // generates. Keeps the sweep correct under concurrency without a batch-wide rollback.
                await db.SaveChangesAsync(cancellationToken);
                if (!alreadyGenerated)
                {
                    generated++;
                }
            }
            catch (ConflictException)
            {
                // Another sweep advanced this plan first. Our staged changes for THIS plan are discarded
                // on the failed save; detach the plan (and any staged work order) so the next iteration
                // is clean, and skip without double-generating.
                foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
                {
                    entry.State = EntityState.Detached;
                }
            }
        }

        return generated;
    }

    public async Task<WorkOrderDto> CreateWorkOrderAsync(CreateWorkOrderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 36.2): raising a corrective work order is a Create on WorkOrder.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "WorkOrder", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to create a work order.");
        }

        var title = RequireText(request.Title, "title", "A work-order title is required.");
        var description = RequireText(request.Description, "description", "A work-order description is required.");

        var communityId = await RequireAssetCommunityAsync(request.AssetId, cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(communityId);

        // Priority is optional: fall back to the configured community default (also validated) when
        // omitted — never a hardcoded choice (steering: no hardcoding).
        var priorityCode = string.IsNullOrWhiteSpace(request.Priority)
            ? await config.GetStringAsync(ConfigKeys.WorkOrderDefaultPriority, DefaultWorkOrderPriority, communityId, cancellationToken)
            : request.Priority;
        var priority = await RequireValidCodeAsync(
            priorityCode, MasterDataKeys.WorkOrderPriority, communityId, "priority", "work-order priority", cancellationToken);

        var workOrder = new DomainWorkOrder
        {
            CommunityId = communityId,
            AssetId = request.AssetId,
            SourceTicketId = request.SourceTicketId,
            Title = title,
            Description = description,
            WorkOrderType = DomainWorkOrderClassification.TypeCorrective,
            Priority = priority,
            Status = DomainWorkOrderStatuses.Open,
        };

        db.WorkOrders.Add(workOrder);

        await audit.AppendAsync(
            new AuditEntry("Create", "WorkOrder", workOrder.Id.ToString(), workOrder.CommunityId,
                Detail: $"assetId={workOrder.AssetId}; type=corrective; priority={priority}"),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(workOrder);
    }

    public async Task<WorkOrderDto> AssignWorkOrderAsync(Guid workOrderId, AssignWorkOrderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 36.3): (re)assigning a work order needs the Assign permission.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Assign, "WorkOrder", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to assign a work order.");
        }

        if (request.AssignedToUserId == Guid.Empty)
        {
            throw new ValidationException("assignedToUserId", "An assignee is required.");
        }

        // TRACKED load so EF carries the RowVersion into the UPDATE (the transition records exactly
        // once under concurrency). An unknown/foreign id is hidden by the global filter → not found.
        var workOrder = await db.WorkOrders.FirstOrDefaultAsync(w => w.Id == workOrderId, cancellationToken)
            ?? throw new NotFoundException("The work order was not found in your authorized communities.");

        authorizationGuard.EnsureCommunityAuthorized(workOrder.CommunityId);

        // The assignee must be a staff user holding Edit on WorkOrder in the community (RBAC data, no
        // hardcoded recipient) — otherwise a validation error, nothing mutated.
        if (!await IsEligibleAssigneeAsync(request.AssignedToUserId, workOrder.CommunityId, cancellationToken))
        {
            throw new ValidationException(
                "assignedToUserId", "The selected assignee is not a maintenance-staff member for this community.");
        }

        // Move status to `assigned` through the SAME configurable allow-list as every other transition
        // (open->assigned first assignment, *->assigned re-assignment): an illegal transition →
        // VALIDATION_ERROR with nothing mutated — no hardcoded table.
        await RequireAllowedTransitionAsync(workOrder.Status, DomainWorkOrderStatuses.Assigned, workOrder.CommunityId, cancellationToken);

        var now = clock.UtcNow;
        workOrder.AssignedToUserId = request.AssignedToUserId;
        workOrder.AssignedAtUtc = now;
        workOrder.Status = DomainWorkOrderStatuses.Assigned;

        await audit.AppendAsync(
            new AuditEntry("Assign", "WorkOrder", workOrder.Id.ToString(), workOrder.CommunityId,
                Detail: $"assignedTo={request.AssignedToUserId}"),
            cancellationToken);

        // Notify the assignee (Req 36.3). EnqueueAsync only Adds the outbox row; the single SaveChanges
        // below commits the assignment + audit + notification atomically (transactional outbox).
        await notifications.EnqueueAsync(
            new NotificationRequest(NotificationTemplateKeys.WorkOrderAssigned, request.AssignedToUserId, workOrder.CommunityId,
                Placeholders: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["workOrderTitle"] = workOrder.Title,
                    ["workOrderType"] = workOrder.WorkOrderType,
                    ["priority"] = workOrder.Priority,
                }),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(workOrder);
    }

    public async Task<WorkOrderDto> ChangeWorkOrderStatusAsync(Guid workOrderId, ChangeWorkOrderStatusRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 36.1-36.5): driving a work order's status is an Edit on WorkOrder.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "WorkOrder", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to change a work order's status.");
        }

        var toStatus = RequireText(request.ToStatus, "toStatus", "A target status is required.");

        var workOrder = await db.WorkOrders.FirstOrDefaultAsync(w => w.Id == workOrderId, cancellationToken)
            ?? throw new NotFoundException("The work order was not found in your authorized communities.");

        authorizationGuard.EnsureCommunityAuthorized(workOrder.CommunityId);

        // The target must be a valid Work_Order_Status code …
        await RequireValidCodeAsync(
            toStatus, MasterDataKeys.WorkOrderStatus, workOrder.CommunityId, "toStatus", "work-order status", cancellationToken);

        // … and the from->to transition must be in the editable allow-list (an illegal transition →
        // VALIDATION_ERROR, nothing mutated — no hardcoded table, mirrors Ticket/Parcel).
        await RequireAllowedTransitionAsync(workOrder.Status, toStatus, workOrder.CommunityId, cancellationToken);

        var fromStatus = workOrder.Status;
        var now = clock.UtcNow;
        workOrder.Status = toStatus;

        // On completion (Req 36.5): stamp the completion instant, record the completion notes, and
        // append a SINGLE append-only equipment-history entry to the asset's history — staged on the
        // same context so the single SaveChanges below commits the transition + audit + history entry
        // atomically. A re-entry into a non-completed status is a normal transition (the allow-list
        // decides legality); completed is terminal in the seeded allow-list so it won't be left.
        if (toStatus == DomainWorkOrderStatuses.Completed)
        {
            workOrder.CompletedAtUtc = now;
            workOrder.CompletionNotes = Trimmed(request.CompletionNotes);
            AppendEquipmentHistory(workOrder, now);
        }

        await audit.AppendAsync(
            new AuditEntry("ChangeStatus", "WorkOrder", workOrder.Id.ToString(), workOrder.CommunityId,
                Detail: $"from={fromStatus}; to={toStatus}"),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(workOrder);
    }

    public async Task<WorkOrderDto?> GetWorkOrderAsync(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        var workOrder = await db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workOrderId, cancellationToken);
        return workOrder is null ? null : ToDto(workOrder);
    }

    public async Task<PagedData<WorkOrderDto>> ListWorkOrdersAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "WorkOrder", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view work orders.");
        }

        return await db.WorkOrders.AsNoTracking().ToPagedResultAsync(
            parameters, WorkOrderQueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
    }

    public async Task<WorkOrderPartDto> AddWorkOrderPartAsync(Guid workOrderId, AddWorkOrderPartRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Parts are managed via the work order's Edit grant (Req 36.4).
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "WorkOrder", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to record parts against a work order.");
        }

        var partName = RequireText(request.PartName, "partName", "A part name is required.");
        var quantity = RequireNonNegative(request.Quantity, "quantity", "The quantity must not be negative.");
        var unitCost = RequireNonNegative(request.UnitCost, "unitCost", "The unit cost must not be negative.");

        // Resolve the work order's community tenant-scoped (the global filter hides foreign/archived
        // rows), so the child always carries the parent's community (Req 36.4). AsNoTracking — we only
        // need the community; the part is a new row.
        var communityId = await RequireWorkOrderCommunityAsync(workOrderId, cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(communityId);

        var part = new DomainWorkOrderPart
        {
            CommunityId = communityId,
            WorkOrderId = workOrderId,
            PartName = partName,
            PartNumber = Trimmed(request.PartNumber),
            Quantity = quantity,
            UnitCost = unitCost,
            LineTotal = quantity * unitCost,
        };

        db.WorkOrderParts.Add(part);

        await audit.AppendAsync(
            new AuditEntry("AddPart", "WorkOrder", workOrderId.ToString(), communityId,
                Detail: $"partId={part.Id}; lineTotal={part.LineTotal}"),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(part);
    }

    public async Task<WorkOrderLabourDto> AddWorkOrderLabourAsync(Guid workOrderId, AddWorkOrderLabourRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "WorkOrder", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to record labour against a work order.");
        }

        var workerName = RequireText(request.WorkerName, "workerName", "A worker name is required.");
        var hours = RequireNonNegative(request.Hours, "hours", "The hours must not be negative.");
        var hourlyRate = RequireNonNegative(request.HourlyRate, "hourlyRate", "The hourly rate must not be negative.");

        var communityId = await RequireWorkOrderCommunityAsync(workOrderId, cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(communityId);

        var labour = new DomainWorkOrderLabour
        {
            CommunityId = communityId,
            WorkOrderId = workOrderId,
            WorkerUserId = request.WorkerUserId,
            WorkerName = workerName,
            Hours = hours,
            HourlyRate = hourlyRate,
            LabourCost = hours * hourlyRate,
            Notes = Trimmed(request.Notes),
        };

        db.WorkOrderLabour.Add(labour);

        await audit.AppendAsync(
            new AuditEntry("AddLabour", "WorkOrder", workOrderId.ToString(), communityId,
                Detail: $"labourId={labour.Id}; labourCost={labour.LabourCost}"),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(labour);
    }

    public async Task<IReadOnlyList<WorkOrderPartDto>> GetWorkOrderPartsAsync(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "WorkOrder", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view work-order parts.");
        }

        // Tenant-scoped AsNoTracking read riding the (CommunityId, WorkOrderId) index — a foreign-tenant
        // work order's parts are hidden by the global filter, so an unknown/foreign id yields an empty
        // list (no leak). Ordered by creation then id as a stable tie-break.
        var parts = await db.WorkOrderParts.AsNoTracking()
            .Where(p => p.WorkOrderId == workOrderId)
            .OrderBy(p => p.CreatedAtUtc).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        return parts.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<WorkOrderLabourDto>> GetWorkOrderLabourAsync(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "WorkOrder", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view work-order labour.");
        }

        var labour = await db.WorkOrderLabour.AsNoTracking()
            .Where(l => l.WorkOrderId == workOrderId)
            .OrderBy(l => l.CreatedAtUtc).ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);

        return labour.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<EquipmentHistoryEntryDto>> GetEquipmentHistoryAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "WorkOrder", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view equipment history.");
        }

        // Tenant-scoped AsNoTracking read riding the (CommunityId, AssetId, OccurredAtUtc) index — a
        // foreign-tenant asset's history is hidden by the global filter, so an unknown/foreign id yields
        // an empty list. Chronological order (Req 36.5).
        var history = await db.EquipmentHistoryEntries.AsNoTracking()
            .Where(e => e.AssetId == assetId)
            .OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

        return history.Select(ToDto).ToList();
    }

    // =============================================================================================
    // Task 21.3 — inspections, AMC contracts and meter history (Req 37.1-37.5).
    // =============================================================================================

    public async Task<InspectionChecklistDto> RecordInspectionAsync(RecordInspectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 37.1): completing an inspection is a Create on InspectionChecklist.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "InspectionChecklist", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to record an inspection.");
        }

        var name = RequireText(request.Name, "name", "An inspection name is required.");

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ValidationException("items", "An inspection requires at least one checklist item.");
        }

        // The asset anchors the inspection's community. The global tenant filter already hides
        // foreign-tenant assets, so an unknown/foreign id is a validation error.
        var communityId = await RequireAssetCommunityAsync(request.AssetId, cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(communityId);

        // Optional work-order link must belong to the same community (Req 37.1).
        if (request.WorkOrderId is { } workOrderId)
        {
            await RequireWorkOrderInCommunityAsync(workOrderId, communityId, cancellationToken);
        }

        var overallResult = await RequireValidCodeAsync(
            request.OverallResult, MasterDataKeys.InspectionResult, communityId, "overallResult", "inspection result", cancellationToken);

        var inspectedAt = request.InspectedAtUtc ?? clock.UtcNow;

        var inspection = new DomainInspectionChecklist
        {
            CommunityId = communityId,
            AssetId = request.AssetId,
            WorkOrderId = request.WorkOrderId,
            Name = name,
            OverallResult = overallResult,
            InspectedByUserId = tenant.UserId,
            InspectedAtUtc = inspectedAt,
            Notes = Trimmed(request.Notes),
        };

        var sortOrder = 0;
        foreach (var item in request.Items)
        {
            var itemText = RequireText(item.ItemText, "items.itemText", "A checklist item text is required.");
            var itemResult = await RequireValidCodeAsync(
                item.Result, MasterDataKeys.InspectionResult, communityId, "items.result", "inspection result", cancellationToken);

            inspection.Items.Add(new DomainInspectionChecklistItem
            {
                CommunityId = communityId,
                ItemText = itemText,
                Result = itemResult,
                Remark = Trimmed(item.Remark),
                SortOrder = sortOrder++,
            });
        }

        db.InspectionChecklists.Add(inspection);

        // Append a single append-only equipment-history entry (Req 37.5) — staged on the same context
        // so the single SaveChanges below commits the inspection + its items + the history entry
        // atomically. Inspection/meter entries carry no WorkOrderId unless the inspection is tied to one.
        db.EquipmentHistoryEntries.Add(new DomainEquipmentHistoryEntry
        {
            CommunityId = communityId,
            AssetId = request.AssetId,
            WorkOrderId = request.WorkOrderId,
            EventType = DomainEquipmentHistoryEventTypes.InspectionCompleted,
            Summary = name,
            Detail = $"result={overallResult}; items={inspection.Items.Count}",
            ActingUserId = tenant.UserId,
            OccurredAtUtc = inspectedAt,
        });

        await audit.AppendAsync(
            new AuditEntry("RecordInspection", "InspectionChecklist", inspection.Id.ToString(), communityId,
                Detail: $"assetId={request.AssetId}; result={overallResult}"),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(inspection);
    }

    public async Task<InspectionChecklistDto?> GetInspectionAsync(Guid inspectionId, CancellationToken cancellationToken = default)
    {
        // Tenant-scoped AsNoTracking read (Req 37.1): an unknown/foreign/soft-deleted id is hidden by
        // the global filter → null. Items are eagerly loaded and ordered for a stable shape.
        var inspection = await db.InspectionChecklists.AsNoTracking()
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == inspectionId, cancellationToken);

        return inspection is null ? null : ToDto(inspection);
    }

    public async Task<IReadOnlyList<InspectionChecklistDto>> GetAssetInspectionsAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "InspectionChecklist", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view inspections.");
        }

        // Tenant-scoped AsNoTracking read riding the (CommunityId, AssetId, InspectedAtUtc) index — a
        // foreign-tenant asset's inspections are hidden by the global filter, so an unknown/foreign id
        // yields an empty list. Chronological order (Req 37.1).
        var inspections = await db.InspectionChecklists.AsNoTracking()
            .Include(i => i.Items)
            .Where(i => i.AssetId == assetId)
            .OrderBy(i => i.InspectedAtUtc).ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);

        return inspections.Select(ToDto).ToList();
    }

    public async Task<AmcContractDto> RecordAmcContractAsync(RecordAmcContractRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 37.2): recording an AMC contract is a Create on AmcContract.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "AmcContract", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to record an AMC contract.");
        }

        var contractReference = RequireText(request.ContractReference, "contractReference", "A contract reference is required.");
        var vendorName = RequireText(request.VendorName, "vendorName", "A vendor name is required.");

        if (request.ResponsibleUserId == Guid.Empty)
        {
            throw new ValidationException("responsibleUserId", "A responsible user is required.");
        }

        if (request.EndDateUtc <= request.StartDateUtc)
        {
            throw new ValidationException("endDateUtc", "The contract end date must be after the start date.");
        }

        if (request.ContractValue is { } value && value < 0)
        {
            throw new ValidationException("contractValue", "The contract value must not be negative.");
        }

        var communityId = await RequireAssetCommunityAsync(request.AssetId, cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(communityId);

        var contract = new DomainAmcContract
        {
            CommunityId = communityId,
            AssetId = request.AssetId,
            ContractReference = contractReference,
            VendorName = vendorName,
            VendorContact = Trimmed(request.VendorContact),
            StartDateUtc = request.StartDateUtc,
            EndDateUtc = request.EndDateUtc,
            ResponsibleUserId = request.ResponsibleUserId,
            Status = DomainAmcContractStatuses.Active,
            ContractValue = request.ContractValue,
            Notes = Trimmed(request.Notes),
        };

        db.AmcContracts.Add(contract);

        await audit.AppendAsync(
            new AuditEntry("Create", "AmcContract", contract.Id.ToString(), communityId,
                Detail: $"assetId={request.AssetId}; vendor={vendorName}; endDate={request.EndDateUtc:O}"),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(contract);
    }

    public async Task<AmcContractDto> UpdateAmcContractAsync(Guid contractId, UpdateAmcContractRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "AmcContract", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to edit an AMC contract.");
        }

        // TRACKED load so EF carries the RowVersion into the UPDATE (the edit records exactly once
        // under concurrency). The global tenant filter hides foreign/archived rows → not found.
        var contract = await db.AmcContracts.FirstOrDefaultAsync(a => a.Id == contractId, cancellationToken)
            ?? throw new NotFoundException("AMC contract not found.");

        authorizationGuard.EnsureCommunityAuthorized(contract.CommunityId);

        if (request.ContractReference is not null)
        {
            contract.ContractReference = RequireText(request.ContractReference, "contractReference", "A contract reference is required.");
        }

        if (request.VendorName is not null)
        {
            contract.VendorName = RequireText(request.VendorName, "vendorName", "A vendor name is required.");
        }

        if (request.VendorContact is not null)
        {
            contract.VendorContact = Trimmed(request.VendorContact);
        }

        if (request.EndDateUtc is { } endDate)
        {
            if (endDate <= contract.StartDateUtc)
            {
                throw new ValidationException("endDateUtc", "The contract end date must be after the start date.");
            }

            contract.EndDateUtc = endDate;
        }

        if (request.ResponsibleUserId is { } responsibleUserId)
        {
            if (responsibleUserId == Guid.Empty)
            {
                throw new ValidationException("responsibleUserId", "A responsible user is required.");
            }

            contract.ResponsibleUserId = responsibleUserId;
        }

        if (request.Status is not null)
        {
            contract.Status = await RequireValidCodeAsync(
                request.Status, MasterDataKeys.AmcContractStatus, contract.CommunityId, "status", "AMC contract status", cancellationToken);
        }

        if (request.ContractValue is { } value)
        {
            contract.ContractValue = RequireNonNegative(value, "contractValue", "The contract value must not be negative.");
        }

        if (request.Notes is not null)
        {
            contract.Notes = Trimmed(request.Notes);
        }

        await audit.AppendAsync(
            new AuditEntry("Update", "AmcContract", contract.Id.ToString(), contract.CommunityId, Detail: null),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(contract);
    }

    public async Task<AmcContractDto?> GetAmcContractAsync(Guid contractId, CancellationToken cancellationToken = default)
    {
        var contract = await db.AmcContracts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == contractId, cancellationToken);
        return contract is null ? null : ToDto(contract);
    }

    public async Task<IReadOnlyList<AmcContractDto>> GetAssetAmcContractsAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "AmcContract", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view AMC contracts.");
        }

        // Tenant-scoped AsNoTracking read riding the (CommunityId, AssetId) index — a foreign-tenant
        // asset's contracts are hidden by the global filter, so an unknown/foreign id yields an empty
        // list. Ordered by end date (soonest-expiring first) then id as a stable tie-break.
        var contracts = await db.AmcContracts.AsNoTracking()
            .Where(a => a.AssetId == assetId)
            .OrderBy(a => a.EndDateUtc).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

        return contracts.Select(ToDto).ToList();
    }

    public async Task<int> SendDueAmcExpiryRemindersAsync(int maxContracts, CancellationToken cancellationToken = default)
    {
        // Bound the work regardless of the caller-supplied size (steering: bounded work / scalable):
        // process at most MaxAmcSweepBatch contracts per call so one pass can't run unbounded under a
        // large backlog. The worker loops to drain the rest across passes. Mirrors the ticket SLA sweep.
        var take = Math.Clamp(maxContracts, 1, MaxAmcSweepBatch);
        var now = clock.UtcNow;

        // Candidate contracts: active, not yet reminded for their CURRENT end date (the
        // ExpiryReminderSentForEndDateUtc != EndDateUtc guard is the at-most-once dedupe marker so a
        // contract is reminded once per expiry window, not every pass — a renewal that changes the end
        // date re-arms it). TRACKED (we stamp the marker). The worker runs under a super-admin context,
        // so the global tenant filter does not hide rows; the (CommunityId, Status, EndDateUtc) index
        // backs the predicate and the page cap keeps memory bounded (no scan, no N+1).
        //
        // The reminder WINDOW is per-community config, so it can't be a single SQL predicate across all
        // tenants. We shortlist active, not-already-reminded contracts whose end date has not yet passed,
        // ordered by soonest expiry (so the most urgent are handled first within the batch), then apply
        // each contract's own community-resolved window in memory — bounded by `take`, so still no scan.
        var candidates = await db.AmcContracts
            .Where(a => a.Status == DomainAmcContractStatuses.Active
                && a.EndDateUtc > now
                && (a.ExpiryReminderSentForEndDateUtc == null || a.ExpiryReminderSentForEndDateUtc != a.EndDateUtc))
            .OrderBy(a => a.EndDateUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return 0;
        }

        var reminded = 0;
        foreach (var contract in candidates)
        {
            // Resolve this contract's community-specific reminder window (days before expiry), never a
            // hardcoded day count (steering: no hardcoding). A non-positive config value falls back to
            // the seeded default.
            var windowDays = await config.GetIntAsync(
                ConfigKeys.AmcExpiryReminderDays, DefaultAmcExpiryReminderDays, contract.CommunityId, cancellationToken);
            if (windowDays <= 0)
            {
                windowDays = DefaultAmcExpiryReminderDays;
            }

            // Not yet inside the window → leave it for a later pass (its marker stays unset).
            if (now < contract.EndDateUtc.AddDays(-windowDays))
            {
                continue;
            }

            var daysToExpiry = Math.Max(0, (int)Math.Ceiling((contract.EndDateUtc - now).TotalDays));

            // Stamp the at-most-once marker so this contract is never reminded twice for this end date.
            // TRACKED, so its RowVersion rides the UPDATE: a concurrent sweep/mutation losing the
            // row-version check surfaces as a CONFLICT on SaveChanges — treated as "someone else handled
            // it" and skipped (handled by the per-contract save below), never re-reminded.
            contract.ExpiryReminderSentForEndDateUtc = contract.EndDateUtc;

            await audit.AppendAsync(
                new AuditEntry("AmcExpiryReminder", "AmcContract", contract.Id.ToString(), contract.CommunityId,
                    Detail: $"daysToExpiry={daysToExpiry}; endDate={contract.EndDateUtc:O}"),
                cancellationToken);

            // Notify the responsible user (Req 37.3). EnqueueAsync only Adds the outbox row; the
            // per-contract SaveChanges below commits the marker + audit + notification atomically
            // (transactional outbox) so a reminder is never sent without the marker (no re-send) and the
            // marker is never stamped without a reminder enqueued.
            await EnqueueAmcExpiryReminderAsync(contract, daysToExpiry, cancellationToken);

            try
            {
                // Save per contract so one contract's concurrency loss is isolated — the rest of the
                // batch still reminds. Keeps the sweep correct under concurrency without a batch-wide
                // rollback.
                await db.SaveChangesAsync(cancellationToken);
                reminded++;
            }
            catch (ConflictException)
            {
                // Another writer updated this contract first (a concurrent edit/renewal or a parallel
                // sweep). Our staged changes for THIS contract are discarded on the failed save; detach
                // so the next iteration's save is clean, and skip without re-notifying.
                db.Entry(contract).State = EntityState.Detached;
            }
        }

        return reminded;
    }

    public async Task<MeterReadingDto> RecordMeterReadingAsync(RecordMeterReadingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth (Req 37.4): recording a meter reading is a Create on MeterReading.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "MeterReading", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to record a meter reading.");
        }

        var communityId = await RequireAssetCommunityAsync(request.AssetId, cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(communityId);

        var meterType = await RequireValidCodeAsync(
            request.MeterType, MasterDataKeys.MeterType, communityId, "meterType", "meter type", cancellationToken);
        var unit = await RequireValidCodeAsync(
            request.Unit, MasterDataKeys.MeterUnit, communityId, "unit", "meter unit", cancellationToken);

        RequireNonNegative(request.Value, "value", "The meter reading must not be negative.");

        // Equipment counters are non-decreasing (Req 37.4): reject a value below the latest recorded
        // reading for this asset + meter type. The (CommunityId, AssetId, MeterType, ReadingAtUtc) index
        // backs this as a single ordered indexed read (no scan). ponytail: this is a read-then-insert,
        // so two concurrent inserts of a lower-than-latest value could both pass the check (small race);
        // chronological correctness and the common-case rejection are preserved. A hard DB guarantee
        // would need a per-(asset,meter) running-max column with an atomic WHERE guard — deferred as not
        // warranted for a manually recorded maintenance counter.
        var latestValue = await db.MeterReadings.AsNoTracking()
            .Where(m => m.AssetId == request.AssetId && m.MeterType == meterType)
            .OrderByDescending(m => m.ReadingAtUtc).ThenByDescending(m => m.Id)
            .Select(m => (decimal?)m.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestValue is { } previous && request.Value < previous)
        {
            throw new ValidationException(
                "value", $"The meter reading ({request.Value}) is below the latest recorded reading ({previous}) for this asset's {meterType} meter.");
        }

        var readingAt = request.ReadingAtUtc ?? clock.UtcNow;

        var reading = new DomainMeterReading
        {
            CommunityId = communityId,
            AssetId = request.AssetId,
            MeterType = meterType,
            Unit = unit,
            Value = request.Value,
            ReadingAtUtc = readingAt,
            RecordedByUserId = tenant.UserId,
            Notes = Trimmed(request.Notes),
        };

        db.MeterReadings.Add(reading);

        // Append a single append-only equipment-history entry (Req 37.4, 37.5) — staged on the same
        // context so the single SaveChanges below commits the reading + the history entry atomically.
        db.EquipmentHistoryEntries.Add(new DomainEquipmentHistoryEntry
        {
            CommunityId = communityId,
            AssetId = request.AssetId,
            WorkOrderId = null,
            EventType = DomainEquipmentHistoryEventTypes.MeterReadingRecorded,
            Summary = $"{meterType}: {request.Value} {unit}",
            Detail = string.IsNullOrWhiteSpace(reading.Notes) ? null : $"notes={reading.Notes}",
            ActingUserId = tenant.UserId,
            OccurredAtUtc = readingAt,
        });

        await audit.AppendAsync(
            new AuditEntry("RecordMeterReading", "MeterReading", reading.Id.ToString(), communityId,
                Detail: $"assetId={request.AssetId}; meterType={meterType}; value={request.Value}"),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(reading);
    }

    public async Task<IReadOnlyList<MeterReadingDto>> GetAssetMeterReadingsAsync(Guid assetId, string? meterType = null, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "MeterReading", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view meter readings.");
        }

        // Tenant-scoped AsNoTracking read riding the (CommunityId, AssetId, MeterType, ReadingAtUtc)
        // index — a foreign-tenant asset's readings are hidden by the global filter, so an
        // unknown/foreign id yields an empty list. An optional meter-type filter narrows to one counter;
        // chronological order (Req 37.4, 37.5).
        var query = db.MeterReadings.AsNoTracking().Where(m => m.AssetId == assetId);

        if (!string.IsNullOrWhiteSpace(meterType))
        {
            var code = meterType.Trim();
            query = query.Where(m => m.MeterType == code);
        }

        var readings = await query
            .OrderBy(m => m.ReadingAtUtc).ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);

        return readings.Select(ToDto).ToList();
    }

    /// <summary>
    /// Enqueue the AMC expiry reminder to the contract's responsible user (Req 37.3). EnqueueAsync only
    /// Adds the outbox row; the caller's SaveChanges commits it in the same transaction (transactional
    /// outbox). The asset name is resolved tenant-scoped for the placeholder (one indexed read).
    /// </summary>
    private async Task EnqueueAmcExpiryReminderAsync(DomainAmcContract contract, int daysToExpiry, CancellationToken cancellationToken)
    {
        var assetName = await db.Assets.AsNoTracking()
            .Where(a => a.Id == contract.AssetId)
            .Select(a => a.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? contract.ContractReference;

        await notifications.EnqueueAsync(
            new NotificationRequest(NotificationTemplateKeys.AmcExpiryReminder, contract.ResponsibleUserId, contract.CommunityId,
                Placeholders: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["assetName"] = assetName,
                    ["vendorName"] = contract.VendorName,
                    ["endDate"] = contract.EndDateUtc.ToString("yyyy-MM-dd"),
                    ["daysToExpiry"] = daysToExpiry.ToString(),
                }),
            cancellationToken);
    }

    /// <summary>
    /// Require that a work order exists in the given community (Req 37.1). The global tenant filter
    /// already hides foreign-tenant rows, so a cross-community/unknown id is a validation error.
    /// AsNoTracking — existence/community check only.
    /// </summary>
    private async Task RequireWorkOrderInCommunityAsync(Guid workOrderId, Guid communityId, CancellationToken cancellationToken)
    {
        var found = await db.WorkOrders.AsNoTracking()
            .AnyAsync(w => w.Id == workOrderId && w.CommunityId == communityId, cancellationToken);

        if (!found)
        {
            throw new ValidationException("workOrderId", "The work order was not found in the asset's community.");
        }
    }

    private static InspectionChecklistDto ToDto(DomainInspectionChecklist i) =>
        new(i.Id, i.CommunityId, i.AssetId, i.WorkOrderId, i.Name, i.OverallResult, i.InspectedByUserId,
            i.InspectedAtUtc, i.Notes,
            i.Items.OrderBy(it => it.SortOrder).ThenBy(it => it.Id)
                .Select(it => new InspectionChecklistItemDto(it.Id, it.ItemText, it.Result, it.Remark, it.SortOrder))
                .ToList());

    private static AmcContractDto ToDto(DomainAmcContract a) =>
        new(a.Id, a.CommunityId, a.AssetId, a.ContractReference, a.VendorName, a.VendorContact,
            a.StartDateUtc, a.EndDateUtc, a.ResponsibleUserId, a.Status, a.ContractValue, a.Notes, a.CreatedAtUtc);

    private static MeterReadingDto ToDto(DomainMeterReading m) =>
        new(m.Id, m.CommunityId, m.AssetId, m.MeterType, m.Unit, m.Value, m.ReadingAtUtc, m.RecordedByUserId, m.Notes);

    /// <summary>
    /// Append a single append-only <c>EquipmentHistoryEntry</c> recording a completed work order
    /// (Req 36.5). Only Adds the row — the caller's SaveChanges commits it with the status transition.
    /// The data layer independently rejects any later modify/delete of the row (append-only guard).
    /// </summary>
    private void AppendEquipmentHistory(DomainWorkOrder workOrder, DateTime now)
    {
        db.EquipmentHistoryEntries.Add(new DomainEquipmentHistoryEntry
        {
            CommunityId = workOrder.CommunityId,
            AssetId = workOrder.AssetId,
            WorkOrderId = workOrder.Id,
            EventType = DomainEquipmentHistoryEventTypes.WorkOrderCompleted,
            Summary = workOrder.Title,
            Detail = BuildCompletionDetail(workOrder),
            OccurredAtUtc = now,
        });
    }

    private static string BuildCompletionDetail(DomainWorkOrder workOrder)
    {
        var detail = $"type={workOrder.WorkOrderType}; priority={workOrder.Priority}";
        return string.IsNullOrWhiteSpace(workOrder.CompletionNotes)
            ? detail
            : $"{detail}; notes={workOrder.CompletionNotes}";
    }

    /// <summary>
    /// Advance a plan's next-due instant by its interval (Req 36.1). To avoid a long-dormant plan
    /// (one whose sweep was down for several intervals) back-filling every missed occurrence at once,
    /// the next due is advanced in whole intervals until it is strictly in the future — so a plan
    /// generates ONE catch-up work order now and resumes its normal cadence from the next future
    /// occurrence (ponytail: skip-missed-occurrences; a FM re-runs a plan manually if a full back-fill
    /// is wanted). The interval is always positive (the service rejects a non-positive one).
    /// </summary>
    private static DateTime AdvanceDue(DateTime occurrence, int intervalDays, DateTime now)
    {
        var next = occurrence.AddDays(intervalDays);
        while (next <= now)
        {
            next = next.AddDays(intervalDays);
        }

        return next;
    }

    /// <summary>
    /// Resolve the interval in days for a plan frequency (Req 36.1): a <c>custom</c> frequency uses
    /// the caller-supplied interval; otherwise the per-frequency config value
    /// <c>maintenance.plan.intervalDays.{frequency}</c> (community override, platform fallback),
    /// falling back to the seeded per-frequency default when unset — never a hardcoded days table. A
    /// non-positive result → VALIDATION_ERROR (a plan with a zero/negative interval would never
    /// advance / would double-generate).
    /// </summary>
    private async Task<int> ResolveIntervalDaysAsync(string frequency, int? customIntervalDays, Guid communityId, CancellationToken cancellationToken)
    {
        if (string.Equals(frequency, DomainMaintenancePlanFrequencies.Custom, StringComparison.Ordinal))
        {
            var custom = customIntervalDays ?? 0;
            if (custom <= 0)
            {
                throw new ValidationException("customIntervalDays", "A positive interval in days is required for a custom frequency.");
            }

            return custom;
        }

        var fallback = DefaultIntervalDaysByFrequency.TryGetValue(frequency, out var seeded) ? seeded : 30;
        var days = await config.GetIntAsync(ConfigKeys.MaintenancePlanIntervalDaysKey(frequency), fallback, communityId, cancellationToken);
        return days > 0 ? days : fallback;
    }

    /// <summary>
    /// Require that a work order's from->to transition is present (and active) in the editable
    /// <c>Work_Order_Status_Transition</c> allow-list for the community (Req 36.1-36.5). Each entry's
    /// code is a <c>from-&gt;to</c> pair of Work_Order_Status codes; a transition not in the list is
    /// rejected — no hardcoded transition table. Mirrors the Ticket/Parcel state machines.
    /// </summary>
    private async Task RequireAllowedTransitionAsync(string from, string to, Guid communityId, CancellationToken cancellationToken)
    {
        var pair = $"{from}->{to}";
        var allowed = await config.GetMasterDataAsync(MasterDataKeys.WorkOrderStatusTransition, communityId, cancellationToken: cancellationToken);
        if (!allowed.Any(e => string.Equals(e.Code, pair, StringComparison.Ordinal)))
        {
            throw new ValidationException(
                "status", $"A work order cannot move from '{from}' to '{to}'; the transition is not allowed.");
        }
    }

    /// <summary>
    /// Whether <paramref name="userId"/> is an eligible work-order assignee in
    /// <paramref name="communityId"/> (Req 36.3): a user whose (community-scoped or platform-wide)
    /// roles grant <c>Edit</c> on <c>WorkOrder</c> in that community — RBAC data, no hardcoded role
    /// name (mirrors the ticket-assignee resolution). One projecting existence query, no N+1.
    /// </summary>
    private Task<bool> IsEligibleAssigneeAsync(Guid userId, Guid communityId, CancellationToken cancellationToken) =>
        (from ur in db.UserRoles.AsNoTracking()
         where ur.UserId == userId && (ur.CommunityId == communityId || ur.CommunityId == null)
         join rp in db.RolePermissions.AsNoTracking() on ur.RoleId equals rp.RoleId
         join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
         where p.ResourceType == "WorkOrder" && p.Action == PermissionAction.Edit
         select ur.UserId)
        .AnyAsync(cancellationToken);

    /// <summary>
    /// Resolve the community id of an asset tenant-scoped (Req 36.1, 36.2), rejecting an unknown /
    /// foreign-tenant id. The returned community anchors the plan/work order so it is always owned by
    /// the same community as its asset (same-community by construction).
    /// </summary>
    private async Task<Guid> RequireAssetCommunityAsync(Guid assetId, CancellationToken cancellationToken)
    {
        var communityId = await db.Assets.AsNoTracking()
            .Where(a => a.Id == assetId)
            .Select(a => (Guid?)a.CommunityId)
            .FirstOrDefaultAsync(cancellationToken);

        return communityId ?? throw new ValidationException(
            "assetId", "The asset was not found in your authorized communities.");
    }

    /// <summary>
    /// Resolve the community id of a work order tenant-scoped (Req 36.4), rejecting an unknown /
    /// foreign-tenant id. The returned community is stamped on the parts/labour children so they stay
    /// tenant-consistent with the parent.
    /// </summary>
    private async Task<Guid> RequireWorkOrderCommunityAsync(Guid workOrderId, CancellationToken cancellationToken)
    {
        var communityId = await db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == workOrderId)
            .Select(w => (Guid?)w.CommunityId)
            .FirstOrDefaultAsync(cancellationToken);

        return communityId ?? throw new NotFoundException("The work order was not found in your authorized communities.");
    }

    private static decimal RequireNonNegative(decimal value, string field, string message)
    {
        if (value < 0)
        {
            throw new ValidationException(field, message);
        }

        return value;
    }

    private static MaintenancePlanDto ToDto(DomainMaintenancePlan p) =>
        new(p.Id, p.CommunityId, p.AssetId, p.Name, p.Description, p.Frequency, p.IntervalDays,
            p.Priority, p.NextDueAtUtc, p.LastGeneratedAtUtc, p.LastGeneratedForDueAtUtc, p.IsActive, p.CreatedAtUtc);

    private static WorkOrderDto ToDto(DomainWorkOrder w) =>
        new(w.Id, w.CommunityId, w.AssetId, w.MaintenancePlanId, w.SourceTicketId, w.Title, w.Description,
            w.WorkOrderType, w.Priority, w.Status, w.AssignedToUserId, w.AssignedAtUtc, w.CompletedAtUtc,
            w.ScheduledForUtc, w.CompletionNotes, w.CreatedAtUtc);

    private static WorkOrderPartDto ToDto(DomainWorkOrderPart p) =>
        new(p.Id, p.CommunityId, p.WorkOrderId, p.PartName, p.PartNumber, p.Quantity, p.UnitCost, p.LineTotal);

    private static WorkOrderLabourDto ToDto(DomainWorkOrderLabour l) =>
        new(l.Id, l.CommunityId, l.WorkOrderId, l.WorkerUserId, l.WorkerName, l.Hours, l.HourlyRate, l.LabourCost, l.Notes);

    private static EquipmentHistoryEntryDto ToDto(DomainEquipmentHistoryEntry e) =>
        new(e.Id, e.CommunityId, e.AssetId, e.WorkOrderId, e.EventType, e.Summary, e.Detail, e.ActingUserId, e.OccurredAtUtc);

    /// <summary>
    /// Save the asset, (re)generating the opaque QR token on the (vanishingly unlikely)
    /// Unique(CommunityId, QrToken) collision rather than a read-then-write pre-check — the same
    /// approach GateService/ParcelService use. A duplicate serial number trips the OTHER unique index
    /// (Unique(CommunityId, SerialNumber)) and is mapped to a clear CONFLICT (Req 35.1).
    /// </summary>
    private async Task SaveWithUniqueAssetAsync(DomainAsset asset, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < QrTokenAttempts; attempt++)
        {
            asset.QrToken = GenerateOpaqueToken();
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException ex) when (IsSerialNumberViolation(ex))
            {
                // A duplicate serial number is a user-facing CONFLICT, not a token collision to retry.
                throw new ConflictException(DuplicateSerialNumberMessage);
            }
            catch (DbUpdateException) when (attempt < QrTokenAttempts - 1)
            {
                // QR-token collision on the unique index: retry with a fresh token. (The serial-number
                // violation is handled above; any other unique violation retries harmlessly with a new
                // token and, if it persists, surfaces on the final attempt below.)
            }
        }

        throw new ConflictException("Could not allocate a unique asset QR token; please retry.");
    }

    // The serial-number unique index is the only one whose violation must map to the serial message;
    // a QR-token collision has a different index name. Matching on the index name keeps the SQL Server
    // production path and the SQLite test path on the same code (ponytail: inspect the message rather
    // than a provider-specific error number). On a provider that does not surface the index name the
    // match falls through to the token-retry branch, which is still correct (it retries the token and,
    // if the serial truly collides, exhausts attempts into a generic CONFLICT).
    private static bool IsSerialNumberViolation(DbUpdateException ex) =>
        ex is not DbUpdateConcurrencyException &&
        (ex.InnerException?.Message.Contains("UX_Asset_CommunityId_SerialNumber", StringComparison.Ordinal) ?? false);

    /// <summary>Opaque, high-entropy, URL-safe token for the QR code (Req 35.2), not derived from data.</summary>
    private static string GenerateOpaqueToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>
    /// Require that the optional tied unit exists in the asset's community (Req 35.1). The global
    /// tenant filter already hides foreign-tenant rows, so a cross-community/unknown id is a
    /// validation error. A null unit id is allowed (a common-area/community asset). AsNoTracking —
    /// we only check existence/community, never mutate it.
    /// </summary>
    private async Task RequireUnitInCommunityAsync(Guid? unitId, Guid communityId, CancellationToken cancellationToken)
    {
        if (unitId is not { } id)
        {
            return;
        }

        var found = await db.Units.AsNoTracking()
            .AnyAsync(u => u.Id == id && u.CommunityId == communityId, cancellationToken);

        if (!found)
        {
            throw new ValidationException("unitId", "The unit was not found in the asset's community.");
        }
    }

    /// <summary>
    /// Validate a code against a configurable master-data list resolved for the community (Req 35.1)
    /// — not a frozen enum, so the vocabulary stays editable per community. Returns the trimmed code
    /// on success; an unknown/retired code → VALIDATION_ERROR. Mirrors ParcelService.RequireValidCodeAsync.
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
    /// Validate an OPTIONAL code: null/blank → null (left unset); otherwise validated against the
    /// configurable list exactly like <see cref="RequireValidCodeAsync"/> (an unknown code →
    /// VALIDATION_ERROR). Used for the optional asset category.
    /// </summary>
    private async Task<string?> ValidateOptionalCodeAsync(
        string? value, string listKey, Guid communityId, string field, string what, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return await RequireValidCodeAsync(value, listKey, communityId, field, what, cancellationToken);
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

    private static AssetDto ToDto(DomainAsset a) =>
        new(a.Id, a.CommunityId, a.Name, a.SerialNumber, a.Location, a.AssetType, a.Category,
            a.Status, a.QrToken, a.UnitId, a.WarrantyProvider, a.WarrantyReference,
            a.WarrantyStartUtc, a.WarrantyEndUtc, a.InstalledOnUtc, a.Notes, a.CreatedAtUtc);
}
