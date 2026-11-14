using App.Application.Abstractions;
using App.Application.Abstractions;
using App.Application.Abstractions.Security;
using App.Application.Audit;
using App.Application.Config;
using App.Application.Exceptions;
using App.Application.MaintenanceBilling;
using App.Application.Notifications;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using DomainMaintenanceBill = App.Domain.MaintenanceBilling.MaintenanceBill;
using DomainMaintenanceBillStatuses = App.Domain.MaintenanceBilling.MaintenanceBillStatuses;
using DomainMaintenanceCharge = App.Domain.MaintenanceBilling.MaintenanceCharge;
using DomainMaintenancePayment = App.Domain.MaintenanceBilling.MaintenancePayment;
using DomainMaintenanceExpense = App.Domain.MaintenanceBilling.MaintenanceExpense;

namespace App.Infrastructure.MaintenanceBilling;

public sealed class MaintenanceBillingService(
    AppDbContext db,
    IAuditService audit,
    IConfigService config,
    IClock clock,
    IAuthorizationGuard authorizationGuard,
    ITenantContext tenant,
    IPageSizePolicy pageSizePolicy,
    IAsyncQueryExecutor executor,
    INotificationService notifications)
    : IMaintenanceBillingService
{
    private const string DuplicateActiveChargeMessage =
        "An active maintenance charge already exists for this unit. Edit the existing charge instead.";
    private const string DuplicateBillMessage =
        "A maintenance bill already exists for this unit and period.";
    private const string DomainPendingApproval = "pending_approval";
    private const string DomainApproved = "approved";
    private const string DomainRejected = "rejected";

    private static readonly ListQueryConfiguration<DomainMaintenanceBill> BillQueryConfig =
        new ListQueryConfiguration<DomainMaintenanceBill>()
            .WithDefaultSort(b => b.IssuedAtUtc, SortDirection.Descending)
            .Sortable("issuedAtUtc", b => b.IssuedAtUtc)
            .Sortable("status", b => b.Status)
            .Sortable("periodYear", b => b.PeriodYear)
            .Sortable("periodMonth", b => b.PeriodMonth)
            .Sortable("amount", b => b.Amount)
            .Filterable("status", b => b.Status)
            .Filterable("unitId", b => b.UnitId)
            .Filterable("communityId", b => b.CommunityId)
            .Filterable("periodYear", b => b.PeriodYear)
            .Filterable("periodMonth", b => b.PeriodMonth);

    private static readonly ListQueryConfiguration<DomainMaintenanceBill> MyBillQueryConfig =
        new ListQueryConfiguration<DomainMaintenanceBill>()
            .WithDefaultSort(b => b.IssuedAtUtc, SortDirection.Descending)
            .Sortable("issuedAtUtc", b => b.IssuedAtUtc)
            .Sortable("periodYear", b => b.PeriodYear)
            .Sortable("periodMonth", b => b.PeriodMonth)
            .Filterable("status", b => b.Status);

    public async Task<MaintenanceChargeDto> SetChargeAsync(SetMaintenanceChargeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "MaintenanceCharge", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to set a maintenance charge.");
        }

        authorizationGuard.EnsureCommunityAuthorized(request.CommunityId);

        if (request.Amount < 0)
        {
            throw new ValidationException("amount", "The maintenance charge amount must not be negative.");
        }

        await RequireUnitInCommunityAsync(request.UnitId, request.CommunityId, cancellationToken);

        var existing = await db.MaintenanceCharges
            .FirstOrDefaultAsync(c => c.UnitId == request.UnitId && c.IsActive, cancellationToken);

        if (existing is not null)
        {
            existing.Amount = request.Amount;
            existing.Label = Trimmed(request.Label) ?? string.Empty;
            existing.IsActive = request.IsActive;

            await audit.AppendAsync(
                new AuditEntry("Update", "MaintenanceCharge", existing.Id.ToString(), existing.CommunityId,
                    Detail: $"unitId={existing.UnitId}; amount={existing.Amount}; active={existing.IsActive}"),
                cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            return ToDto(existing);
        }

        var charge = new DomainMaintenanceCharge
        {
            CommunityId = request.CommunityId,
            UnitId = request.UnitId,
            Amount = request.Amount,
            Label = Trimmed(request.Label) ?? string.Empty,
            IsActive = request.IsActive,
        };

        db.MaintenanceCharges.Add(charge);

        await audit.AppendAsync(
            new AuditEntry("Create", "MaintenanceCharge", charge.Id.ToString(), charge.CommunityId,
                Detail: $"unitId={charge.UnitId}; amount={charge.Amount}; active={charge.IsActive}"),
            cancellationToken);

        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), DuplicateActiveChargeMessage);

        return ToDto(charge);
    }

    public async Task<BulkSetMaintenanceChargeResult> BulkSetChargesAsync(BulkSetMaintenanceChargeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "MaintenanceCharge", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to set maintenance charges.");
        }

        authorizationGuard.EnsureCommunityAuthorized(request.CommunityId);

        if (request.Amount < 0)
        {
            throw new ValidationException("amount", "The maintenance charge amount must not be negative.");
        }

        List<Guid> unitIds;
        if (request.ApplyToAllUnits)
        {
            unitIds = await db.Units.AsNoTracking()
                .Where(u => u.CommunityId == request.CommunityId)
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);
        }
        else
        {
            if (request.UnitIds is null || request.UnitIds.Count == 0)
            {
                throw new ValidationException("unitIds", "Select at least one unit, or choose apply-to-all.");
            }

            var distinct = request.UnitIds.Distinct().ToList();
            if (distinct.Count > 2000)
            {
                throw new ValidationException("unitIds", "Too many units in one request. Apply in smaller batches.");
            }

            var validIds = await db.Units.AsNoTracking()
                .Where(u => u.CommunityId == request.CommunityId && distinct.Contains(u.Id))
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);

            if (validIds.Count != distinct.Count)
            {
                throw new ValidationException("unitIds", "One or more units do not belong to this community.");
            }

            unitIds = validIds;
        }

        if (unitIds.Count == 0)
        {
            return new BulkSetMaintenanceChargeResult(request.CommunityId, 0, 0);
        }

        var label = Trimmed(request.Label) ?? string.Empty;

        var existing = await db.MaintenanceCharges
            .Where(c => c.CommunityId == request.CommunityId && c.IsActive && unitIds.Contains(c.UnitId))
            .ToListAsync(cancellationToken);

        var existingByUnit = existing.ToDictionary(c => c.UnitId);

        var created = 0;
        var updated = 0;

        foreach (var unitId in unitIds)
        {
            if (existingByUnit.TryGetValue(unitId, out var charge))
            {
                charge.Amount = request.Amount;
                charge.Label = label;
                charge.IsActive = request.IsActive;
                updated++;
            }
            else
            {
                db.MaintenanceCharges.Add(new DomainMaintenanceCharge
                {
                    CommunityId = request.CommunityId,
                    UnitId = unitId,
                    Amount = request.Amount,
                    Label = label,
                    IsActive = request.IsActive,
                });
                created++;
            }
        }

        await audit.AppendAsync(
            new AuditEntry("Create", "MaintenanceCharge", request.CommunityId.ToString(), request.CommunityId,
                Detail: $"bulk; created={created}; updated={updated}; amount={request.Amount}"),
            cancellationToken);

        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), DuplicateActiveChargeMessage);

        return new BulkSetMaintenanceChargeResult(request.CommunityId, created, updated);
    }

    public async Task<IReadOnlyList<MaintenanceChargeDto>> ListChargesAsync(Guid? communityId, Guid? organizationId = null, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "MaintenanceCharge", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view maintenance charges.");
        }

        var query = db.MaintenanceCharges.AsNoTracking().AsQueryable();

        if (communityId.HasValue && communityId.Value != Guid.Empty)
        {
            query = query.Where(c => c.CommunityId == communityId.Value);
        }
        else if (organizationId.HasValue && organizationId.Value != Guid.Empty)
        {
            var commIds = await db.Communities.AsNoTracking()
                .Where(c => c.OrganizationId == organizationId.Value)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);
            query = query.Where(c => commIds.Contains(c.CommunityId));
        }

        var charges = await query
            .OrderBy(c => c.UnitId)
            .ToListAsync(cancellationToken);

        var unitNumbers = await ResolveUnitNumbersAsync(charges.Select(c => c.UnitId), cancellationToken);
        var names = await App.Infrastructure.Community.UnitService.ResolveCommunityOrgNamesAsync(
            db, charges.Select(c => c.CommunityId).Distinct(), cancellationToken);

        return charges
            .Select(c =>
            {
                names.TryGetValue(c.CommunityId, out var n);
                var dto = unitNumbers.TryGetValue(c.UnitId, out var un) ? ToDto(c) with { UnitNumber = un } : ToDto(c);
                return dto with
                {
                    CommunityName = names.TryGetValue(c.CommunityId, out var nc) ? nc.Community : null,
                    OrganizationName = names.TryGetValue(c.CommunityId, out var nc2) ? nc2.Organization : null
                };
            })
            .ToList();
    }

    public async Task<MaintenanceChargeDto> DeactivateChargeAsync(Guid chargeId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "MaintenanceCharge", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to change a maintenance charge.");
        }

        var charge = await db.MaintenanceCharges.FirstOrDefaultAsync(c => c.Id == chargeId, cancellationToken)
            ?? throw new NotFoundException("Maintenance charge not found.");

        authorizationGuard.EnsureCommunityAuthorized(charge.CommunityId);

        charge.IsActive = false;

        await audit.AppendAsync(
            new AuditEntry("Update", "MaintenanceCharge", charge.Id.ToString(), charge.CommunityId,
                Detail: $"unitId={charge.UnitId}; deactivated"),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(charge);
    }

    public async Task<IReadOnlyList<MaintenanceDefaulterDto>> ListDefaultersAsync(CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "MaintenanceBill", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view maintenance bills.");
        }

        var isSuperAdmin = tenant.IsPlatformSuperAdmin;
        var communityIds = tenant.AuthorizedCommunityIds.ToList();
        if (!isSuperAdmin && communityIds.Count == 0)
        {
            return new List<MaintenanceDefaulterDto>();
        }

        var pendingBillIds = await db.MaintenancePayments.AsNoTracking().IgnoreQueryFilters()
            .Where(p => !p.IsDeleted && (isSuperAdmin || communityIds.Contains(p.CommunityId)) && p.Status == DomainPendingApproval)
            .Select(p => p.MaintenanceBillId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var pendingSet = pendingBillIds.ToHashSet();

        var unpaid = await db.MaintenanceBills.AsNoTracking().IgnoreQueryFilters()
            .Where(b => !b.IsDeleted && (isSuperAdmin || communityIds.Contains(b.CommunityId))
                && (b.Status == DomainMaintenanceBillStatuses.Unpaid || b.Status == DomainMaintenanceBillStatuses.PartiallyPaid)
                && b.UnitId != null)
            .Select(b => new { b.Id, UnitId = b.UnitId!.Value })
            .ToListAsync(cancellationToken);

        var byUnit = unpaid
            .Where(b => !pendingSet.Contains(b.Id))
            .GroupBy(b => b.UnitId)
            .ToDictionary(g => g.Key, g => g.Count());

        if (byUnit.Count == 0)
        {
            return new List<MaintenanceDefaulterDto>();
        }

        var unitIds = byUnit.Keys.ToList();

        var units = await db.Units.AsNoTracking().IgnoreQueryFilters()
            .Where(u => !u.IsDeleted && (isSuperAdmin || communityIds.Contains(u.CommunityId)) && unitIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UnitNumber })
            .ToListAsync(cancellationToken);

        var names = await (
            from hm in db.HouseholdMembers.AsNoTracking().IgnoreQueryFilters()
            where !hm.IsDeleted && (isSuperAdmin || communityIds.Contains(hm.CommunityId)) && hm.UnitId != null && unitIds.Contains(hm.UnitId.Value)
            join r in db.Residents.AsNoTracking().IgnoreQueryFilters() on hm.ResidentId equals r.Id
            where !r.IsDeleted
            select new { UnitId = hm.UnitId!.Value, r.Name })
            .ToListAsync(cancellationToken);

        var nameByUnit = names
            .GroupBy(x => x.UnitId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Name).Distinct()));

        return units
            .Select(u => new MaintenanceDefaulterDto(
                u.Id,
                u.UnitNumber,
                nameByUnit.TryGetValue(u.Id, out var n) ? n : "",
                byUnit[u.Id]))
            .OrderBy(d => d.UnitNumber, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<MaintenanceCollectionSummaryDto> GetCollectionSummaryAsync(Guid? communityId, Guid? organizationId = null, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "MaintenanceBill", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view maintenance bills.");
        }

        List<Guid>? scopedCommunityIds = null;
        if (communityId.HasValue && communityId.Value != Guid.Empty)
        {
            scopedCommunityIds = [communityId.Value];
        }
        else if (organizationId.HasValue && organizationId.Value != Guid.Empty)
        {
            scopedCommunityIds = await db.Communities.AsNoTracking()
                .Where(c => c.OrganizationId == organizationId.Value)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);
        }

        var bills = db.MaintenanceBills.AsNoTracking().AsQueryable();
        var expenseQuery = db.MaintenanceExpenses.AsNoTracking().AsQueryable();

        if (scopedCommunityIds != null)
        {
            bills = bills.Where(b => scopedCommunityIds.Contains(b.CommunityId));
            expenseQuery = expenseQuery.Where(e => scopedCommunityIds.Contains(e.CommunityId));
        }

        var perCommunity = await bills
            .GroupBy(b => b.CommunityId)
            .Select(g => new
            {
                CommunityId = g.Key,
                Collected = g.Sum(b => b.AmountPaid),
                Outstanding = g.Sum(b => b.Amount - b.AmountPaid),
                PaidCount = g.Count(b => b.Status == DomainMaintenanceBillStatuses.Paid),
                UnpaidCount = g.Count(b => b.Status == DomainMaintenanceBillStatuses.Unpaid || b.Status == DomainMaintenanceBillStatuses.PartiallyPaid),
            })
            .ToListAsync(cancellationToken);

        var ledgerByCommunity = await expenseQuery
            .GroupBy(e => e.CommunityId)
            .Select(g => new
            {
                CommunityId = g.Key,
                Spent = g.Sum(e => e.Direction == "credit" ? 0m : e.Amount),
                Received = g.Sum(e => e.Direction == "credit" ? e.Amount : 0m),
            })
            .ToDictionaryAsync(x => x.CommunityId, x => x, cancellationToken);

        var allCommunityIds = perCommunity.Select(x => x.CommunityId)
            .Union(ledgerByCommunity.Keys);
        if (scopedCommunityIds != null)
        {
            allCommunityIds = allCommunityIds.Union(scopedCommunityIds);
        }
        var distinctCommunityIds = allCommunityIds.Distinct().ToList();

        var names = await App.Infrastructure.Community.UnitService.ResolveCommunityOrgNamesAsync(
            db, distinctCommunityIds, cancellationToken);

        var perCommunityDict = perCommunity.ToDictionary(x => x.CommunityId);

        var byCommunity = distinctCommunityIds
            .Select(cid =>
            {
                var bill = perCommunityDict.TryGetValue(cid, out var b) ? b : null;
                var ledger = ledgerByCommunity.TryGetValue(cid, out var l) ? l : null;
                return new MaintenanceCollectionByCommunityDto(
                    cid,
                    names.TryGetValue(cid, out var n) ? n.Community : "",
                    names.TryGetValue(cid, out var n2) ? n2.Organization : "",
                    bill?.Collected ?? 0m,
                    bill?.Outstanding ?? 0m,
                    ledger?.Spent ?? 0m,
                    ledger?.Received ?? 0m);
            })
            .OrderBy(x => x.OrganizationName, StringComparer.Ordinal)
            .ThenBy(x => x.CommunityName, StringComparer.Ordinal)
            .ToList();

        var totalCollected = perCommunity.Sum(x => x.Collected);
        var totalReceived = ledgerByCommunity.Values.Sum(x => x.Received);
        var totalOutstanding = perCommunity.Sum(x => x.Outstanding);
        var paidCount = perCommunity.Sum(x => x.PaidCount);
        var unpaidCount = perCommunity.Sum(x => x.UnpaidCount);

        return new MaintenanceCollectionSummaryDto(
            totalCollected,
            totalOutstanding,
            paidCount,
            unpaidCount,
            byCommunity,
            totalReceived);
    }

    public async Task<IReadOnlyList<MaintenanceExpenseDto>> ListExpensesAsync(Guid? communityId, Guid? organizationId = null, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "MaintenanceBill", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view maintenance expenses.");
        }

        var query = db.MaintenanceExpenses.AsNoTracking().AsQueryable();

        if (communityId.HasValue && communityId.Value != Guid.Empty)
        {
            query = query.Where(e => e.CommunityId == communityId.Value);
        }
        else if (organizationId.HasValue && organizationId.Value != Guid.Empty)
        {
            var commIds = await db.Communities.AsNoTracking()
                .Where(c => c.OrganizationId == organizationId.Value)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);
            query = query.Where(e => commIds.Contains(e.CommunityId));
        }

        var rows = await query
            .OrderByDescending(e => e.SpentOnUtc)
            .ToListAsync(cancellationToken);

        var names = await App.Infrastructure.Community.UnitService.ResolveCommunityOrgNamesAsync(
            db, rows.Select(e => e.CommunityId).Distinct(), cancellationToken);

        return rows.Select(e =>
        {
            names.TryGetValue(e.CommunityId, out var n);
            return ToDto(e) with
            {
                CommunityName = names.TryGetValue(e.CommunityId, out var ne) ? ne.Community : null,
                    OrganizationName = names.TryGetValue(e.CommunityId, out var ne2) ? ne2.Organization : null
            };
        }).ToList();
    }

    public async Task<MaintenanceExpenseDto> CreateExpenseAsync(SaveMaintenanceExpenseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "MaintenanceBill", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to record a maintenance expense.");
        }

        authorizationGuard.EnsureCommunityAuthorized(request.CommunityId);

        var category = await RequireValidCodeAsync(
            request.Category, MasterDataKeys.MaintenanceExpenseCategory, request.CommunityId, "category", "expense category", cancellationToken);

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ValidationException("title", "A title is required.");
        }

        if (request.Amount <= 0)
        {
            throw new ValidationException("amount", "The expense amount must be greater than zero.");
        }

        var direction = App.Domain.MaintenanceBilling.MaintenanceLedgerDirections.IsValid(request.Direction)
            ? request.Direction
            : App.Domain.MaintenanceBilling.MaintenanceLedgerDirections.Debit;

        var expense = new DomainMaintenanceExpense
        {
            CommunityId = request.CommunityId,
            Direction = direction,
            Category = category,
            Title = request.Title.Trim(),
            Amount = request.Amount,
            SpentOnUtc = request.SpentOnUtc,
            Vendor = Trimmed(request.Vendor) ?? string.Empty,
            Notes = Trimmed(request.Notes) ?? string.Empty,
        };

        db.MaintenanceExpenses.Add(expense);

        await audit.AppendAsync(
            new AuditEntry("Create", "MaintenanceExpense", expense.Id.ToString(), expense.CommunityId,
                Detail: $"category={expense.Category}; amount={expense.Amount}"),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(expense);
    }

    public async Task<MaintenanceExpenseDto> UpdateExpenseAsync(Guid expenseId, SaveMaintenanceExpenseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "MaintenanceBill", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to edit a maintenance expense.");
        }

        var expense = await db.MaintenanceExpenses.FirstOrDefaultAsync(e => e.Id == expenseId, cancellationToken)
            ?? throw new NotFoundException("Maintenance expense not found.");

        authorizationGuard.EnsureCommunityAuthorized(expense.CommunityId);

        var category = await RequireValidCodeAsync(
            request.Category, MasterDataKeys.MaintenanceExpenseCategory, expense.CommunityId, "category", "expense category", cancellationToken);

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ValidationException("title", "A title is required.");
        }

        if (request.Amount <= 0)
        {
            throw new ValidationException("amount", "The expense amount must be greater than zero.");
        }

        expense.Direction = App.Domain.MaintenanceBilling.MaintenanceLedgerDirections.IsValid(request.Direction)
            ? request.Direction
            : App.Domain.MaintenanceBilling.MaintenanceLedgerDirections.Debit;
        expense.Category = category;
        expense.Title = request.Title.Trim();
        expense.Amount = request.Amount;
        expense.SpentOnUtc = request.SpentOnUtc;
        expense.Vendor = Trimmed(request.Vendor) ?? string.Empty;
        expense.Notes = Trimmed(request.Notes) ?? string.Empty;

        await audit.AppendAsync(
            new AuditEntry("Update", "MaintenanceExpense", expense.Id.ToString(), expense.CommunityId,
                Detail: $"category={expense.Category}; amount={expense.Amount}"),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(expense);
    }

    public async Task DeleteExpenseAsync(Guid expenseId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Delete, "MaintenanceBill", cancellationToken)
            && !await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "MaintenanceBill", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to delete a maintenance expense.");
        }

        var expense = await db.MaintenanceExpenses.FirstOrDefaultAsync(e => e.Id == expenseId, cancellationToken)
            ?? throw new NotFoundException("Maintenance expense not found.");

        authorizationGuard.EnsureCommunityAuthorized(expense.CommunityId);

        expense.IsDeleted = true;
        expense.DeletedAtUtc = clock.UtcNow;

        await audit.AppendAsync(
            new AuditEntry("Delete", "MaintenanceExpense", expense.Id.ToString(), expense.CommunityId,
                Detail: $"category={expense.Category}; amount={expense.Amount}"),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<GenerateBillsResult> GenerateBillsAsync(GenerateBillsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "MaintenanceBill", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to generate maintenance bills.");
        }

        authorizationGuard.EnsureCommunityAuthorized(request.CommunityId);

        if (request.PeriodMonth is < 1 or > 12)
        {
            throw new ValidationException("periodMonth", "The period month must be between 1 and 12.");
        }

        if (request.PeriodYear is < 2000 or > 3000)
        {
            throw new ValidationException("periodYear", "The period year is out of range.");
        }

        var charges = await db.MaintenanceCharges.AsNoTracking()
            .Where(c => c.CommunityId == request.CommunityId && c.IsActive && c.Amount > 0)
            .Select(c => new { c.UnitId, c.Amount, c.Label })
            .ToListAsync(cancellationToken);

        if (charges.Count == 0)
        {
            return new GenerateBillsResult(request.CommunityId, request.PeriodYear, request.PeriodMonth, 0, 0);
        }

        var alreadyBilledUnitIds = await db.MaintenanceBills.AsNoTracking()
            .Where(b => b.CommunityId == request.CommunityId
                && b.PeriodYear == request.PeriodYear
                && b.PeriodMonth == request.PeriodMonth)
            .Select(b => b.UnitId)
            .ToListAsync(cancellationToken);

        var billedSet = alreadyBilledUnitIds.Where(id => id is not null).Select(id => id!.Value).ToHashSet();

        var toCreate = charges.Where(c => !billedSet.Contains(c.UnitId)).ToList();
        var skipped = charges.Count - toCreate.Count;

        if (toCreate.Count == 0)
        {
            return new GenerateBillsResult(request.CommunityId, request.PeriodYear, request.PeriodMonth, 0, skipped);
        }

        var now = clock.UtcNow;

        foreach (var c in toCreate)
        {
            var bill = new DomainMaintenanceBill
            {
                CommunityId = request.CommunityId,
                UnitId = c.UnitId,
                PeriodYear = request.PeriodYear,
                PeriodMonth = request.PeriodMonth,
                Amount = c.Amount,
                AmountPaid = 0m,
                Label = c.Label,
                Status = DomainMaintenanceBillStatuses.Unpaid,
                DueDateUtc = request.DueDateUtc,
                IssuedAtUtc = now,
            };

            db.MaintenanceBills.Add(bill);
        }

        await audit.AppendAsync(
            new AuditEntry("Create", "MaintenanceBill", $"{request.CommunityId}:{request.PeriodYear}-{request.PeriodMonth}", request.CommunityId,
                Detail: $"generated={toCreate.Count}; skipped={skipped}"),
            cancellationToken);

        await NotifyBillGeneratedAsync(
            request.CommunityId,
            request.PeriodYear,
            request.PeriodMonth,
            toCreate.Select(c => (c.UnitId, c.Amount)).ToList(),
            cancellationToken);

        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), DuplicateBillMessage);

        return new GenerateBillsResult(request.CommunityId, request.PeriodYear, request.PeriodMonth, toCreate.Count, skipped);
    }

    public async Task<PagedData<MaintenanceBillDto>> ListBillsAsync(ListQueryParameters parameters, Guid? communityId = null, Guid? organizationId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var query = db.MaintenanceBills.AsNoTracking().AsQueryable();

        if (communityId.HasValue && communityId.Value != Guid.Empty)
        {
            query = query.Where(b => b.CommunityId == communityId.Value);
        }
        else if (organizationId.HasValue && organizationId.Value != Guid.Empty)
        {
            var commIds = await db.Communities.AsNoTracking()
                .Where(c => c.OrganizationId == organizationId.Value)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);
            query = query.Where(b => commIds.Contains(b.CommunityId));
        }

        var paged = await query.ToPagedResultAsync(
            parameters, BillQueryConfig, b => ToDto(b), pageSizePolicy, executor, cancellationToken);

        var names = await App.Infrastructure.Community.UnitService.ResolveCommunityOrgNamesAsync(
            db, paged.Items.Select(b => b.CommunityId), cancellationToken);

        var unitNumbers = await ResolveUnitNumbersAsync(
            paged.Items.Select(b => b.UnitId), cancellationToken);

        var billIds = paged.Items.Select(b => b.Id).ToList();
        var pendingBillIds = await db.MaintenancePayments.AsNoTracking()
            .Where(p => p.Status == DomainPendingApproval && billIds.Contains(p.MaintenanceBillId))
            .Select(p => p.MaintenanceBillId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var pendingSet = pendingBillIds.ToHashSet();

        return new PagedData<MaintenanceBillDto>
        {
            Items = paged.Items
                .Select(b =>
                {
                    var enriched = names.TryGetValue(b.CommunityId, out var n)
                        ? b with { CommunityName = n.Community, OrganizationName = n.Organization }
                        : b;
                    if (unitNumbers.TryGetValue(b.UnitId, out var un))
                    {
                        enriched = enriched with { UnitNumber = un };
                    }
                    return pendingSet.Contains(b.Id) ? enriched with { PendingApproval = true } : enriched;
                })
                .ToList(),
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalCount = paged.TotalCount,
        };
    }

    private async Task<Dictionary<Guid, string>> ResolveUnitNumbersAsync(
        IEnumerable<Guid> unitIds, CancellationToken cancellationToken)
    {
        var ids = unitIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await db.Units.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.UnitNumber })
            .ToDictionaryAsync(u => u.Id, u => u.UnitNumber, cancellationToken);
    }

    public async Task<MaintenanceBillDto?> GetBillAsync(Guid billId, CancellationToken cancellationToken = default)
    {
        var bill = await db.MaintenanceBills.AsNoTracking().FirstOrDefaultAsync(b => b.Id == billId, cancellationToken);
        return bill is null ? null : ToDto(bill);
    }

    public async Task<MaintenancePaymentDto> RecordPaymentAsync(RecordMaintenancePaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "MaintenancePayment", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to record a maintenance payment.");
        }

        var bill = await db.MaintenanceBills.FirstOrDefaultAsync(b => b.Id == request.MaintenanceBillId, cancellationToken)
            ?? throw new NotFoundException("Maintenance bill not found.");

        authorizationGuard.EnsureCommunityAuthorized(bill.CommunityId);

        if (string.Equals(bill.Status, DomainMaintenanceBillStatuses.Paid, StringComparison.Ordinal)
            || string.Equals(bill.Status, DomainMaintenanceBillStatuses.Waived, StringComparison.Ordinal))
        {
            throw new ValidationException("maintenanceBillId", "The bill is already settled and cannot take further payments.");
        }

        if (request.Amount <= 0)
        {
            throw new ValidationException("amount", "The payment amount must be greater than zero.");
        }

        var method = await RequireValidCodeAsync(
            request.Method, MasterDataKeys.MaintenancePaymentMethod, bill.CommunityId, "method", "payment method", cancellationToken);

        var pendingTotal = await db.MaintenancePayments.AsNoTracking()
            .Where(p => p.MaintenanceBillId == bill.Id && p.Status == DomainPendingApproval)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        var outstandingAfterPending = bill.Amount - bill.AmountPaid - pendingTotal;
        if (request.Amount > outstandingAfterPending)
        {
            throw new ValidationException("amount", $"The payment exceeds the collectable balance of {outstandingAfterPending}.");
        }

        var now = clock.UtcNow;
        var paidAt = request.PaidAtUtc ?? now;
        var approveNow = request.AutoApprove && !request.SubmittedByResident;

        var payment = new DomainMaintenancePayment
        {
            CommunityId = bill.CommunityId,
            MaintenanceBillId = bill.Id,
            UnitId = bill.UnitId,
            Amount = request.Amount,
            Method = method,
            Reference = Trimmed(request.Reference) ?? string.Empty,
            PaidAtUtc = paidAt,
            SubmittedByResident = request.SubmittedByResident,
            Status = approveNow ? DomainApproved : DomainPendingApproval,
        };

        if (approveNow)
        {
            payment.ApprovedByUserId = ParseUserId();
            payment.ApprovedAtUtc = now;
            ApplyPaymentToBill(bill, request.Amount);
        }

        db.MaintenancePayments.Add(payment);

        await audit.AppendAsync(
            new AuditEntry("Create", "MaintenancePayment", payment.Id.ToString(), payment.CommunityId,
                Detail: $"billId={bill.Id}; amount={request.Amount}; method={method}; status={payment.Status}"),
            cancellationToken);

        if (!approveNow && request.SubmittedByResident)
        {
            var unitNumber = await UnitNumberAsync(bill.UnitId, cancellationToken);
            await NotifyPaymentApproversAsync(bill.CommunityId, new Dictionary<string, string>
            {
                ["amount"] = request.Amount.ToString("0.00"),
                ["unitNumber"] = unitNumber,
                ["period"] = PeriodLabel(bill.PeriodYear, bill.PeriodMonth),
            }, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(payment);
    }

    public async Task<MaintenancePaymentDto> ApprovePaymentAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Approve, "MaintenancePayment", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to approve a maintenance payment.");
        }

        var payment = await db.MaintenancePayments.FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken)
            ?? throw new NotFoundException("Maintenance payment not found.");

        authorizationGuard.EnsureCommunityAuthorized(payment.CommunityId);

        if (!string.Equals(payment.Status, DomainPendingApproval, StringComparison.Ordinal))
        {
            throw new ValidationException("status", "Only a pending payment can be approved.");
        }

        var bill = await db.MaintenanceBills.FirstOrDefaultAsync(b => b.Id == payment.MaintenanceBillId, cancellationToken)
            ?? throw new NotFoundException("Maintenance bill not found.");

        if (string.Equals(bill.Status, DomainMaintenanceBillStatuses.Waived, StringComparison.Ordinal))
        {
            throw new ValidationException("status", "The bill has been waived and cannot take a payment.");
        }

        var outstanding = bill.Amount - bill.AmountPaid;
        if (payment.Amount > outstanding)
        {
            throw new ValidationException("amount", $"The payment exceeds the outstanding balance of {outstanding}.");
        }

        payment.Status = DomainApproved;
        payment.ApprovedByUserId = ParseUserId();
        payment.ApprovedAtUtc = clock.UtcNow;
        ApplyPaymentToBill(bill, payment.Amount);

        await audit.AppendAsync(
            new AuditEntry("Approve", "MaintenancePayment", payment.Id.ToString(), payment.CommunityId,
                Detail: $"billId={bill.Id}; amount={payment.Amount}; billStatus={bill.Status}"),
            cancellationToken);

        await NotifyUnitResidentsAsync(payment.CommunityId, payment.UnitId,
            NotificationTemplateKeys.MaintenancePaymentApproved,
            new Dictionary<string, string>
            {
                ["amount"] = payment.Amount.ToString("0.00"),
                ["period"] = PeriodLabel(bill.PeriodYear, bill.PeriodMonth),
            }, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(payment);
    }

    public async Task<MaintenancePaymentDto> RejectPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Approve, "MaintenancePayment", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to reject a maintenance payment.");
        }

        var payment = await db.MaintenancePayments.FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken)
            ?? throw new NotFoundException("Maintenance payment not found.");

        authorizationGuard.EnsureCommunityAuthorized(payment.CommunityId);

        if (!string.Equals(payment.Status, DomainPendingApproval, StringComparison.Ordinal))
        {
            throw new ValidationException("status", "Only a pending payment can be rejected.");
        }

        payment.Status = DomainRejected;
        payment.ApprovedByUserId = ParseUserId();
        payment.ApprovedAtUtc = clock.UtcNow;

        await audit.AppendAsync(
            new AuditEntry("Reject", "MaintenancePayment", payment.Id.ToString(), payment.CommunityId,
                Detail: $"billId={payment.MaintenanceBillId}; amount={payment.Amount}"),
            cancellationToken);

        var rejectedBill = await db.MaintenanceBills.AsNoTracking()
            .Where(b => b.Id == payment.MaintenanceBillId)
            .Select(b => new { b.PeriodYear, b.PeriodMonth })
            .FirstOrDefaultAsync(cancellationToken);

        await NotifyUnitResidentsAsync(payment.CommunityId, payment.UnitId,
            NotificationTemplateKeys.MaintenancePaymentRejected,
            new Dictionary<string, string>
            {
                ["amount"] = payment.Amount.ToString("0.00"),
                ["period"] = rejectedBill is null ? "" : PeriodLabel(rejectedBill.PeriodYear, rejectedBill.PeriodMonth),
                ["reason"] = "Please contact the office or submit a corrected payment.",
            }, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(payment);
    }

    public async Task<IReadOnlyList<PendingPaymentDto>> ListPendingPaymentsAsync(Guid? communityId, Guid? organizationId = null, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Approve, "MaintenancePayment", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view pending maintenance payments.");
        }

        var payments = db.MaintenancePayments.AsNoTracking()
            .Where(p => p.Status == DomainPendingApproval);

        if (communityId.HasValue && communityId.Value != Guid.Empty)
        {
            payments = payments.Where(p => p.CommunityId == communityId.Value);
        }
        else if (organizationId.HasValue && organizationId.Value != Guid.Empty)
        {
            var commIds = await db.Communities.AsNoTracking()
                .Where(c => c.OrganizationId == organizationId.Value)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);
            payments = payments.Where(p => commIds.Contains(p.CommunityId));
        }

        var rows = await payments
            .Join(
                db.MaintenanceBills.AsNoTracking(),
                p => p.MaintenanceBillId,
                b => b.Id,
                (p, b) => new
                {
                    p.Id,
                    p.CommunityId,
                    p.MaintenanceBillId,
                    p.UnitId,
                    b.PeriodYear,
                    b.PeriodMonth,
                    p.Amount,
                    p.Method,
                    p.Reference,
                    p.PaidAtUtc,
                    p.SubmittedByResident,
                })
            .OrderBy(x => x.PaidAtUtc)
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new PendingPaymentDto(
                x.Id, x.CommunityId, x.MaintenanceBillId, x.UnitId ?? Guid.Empty,
                x.PeriodYear, x.PeriodMonth, x.Amount, x.Method, x.Reference, x.PaidAtUtc, x.SubmittedByResident))
            .ToList();
    }

    public async Task<IReadOnlyList<MaintenancePaymentDto>> ListPaymentsForBillAsync(Guid billId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "MaintenancePayment", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view maintenance payments.");
        }

        var rows = await db.MaintenancePayments.AsNoTracking()
            .Where(p => p.MaintenanceBillId == billId)
            .OrderByDescending(p => p.PaidAtUtc).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<MaintenanceBillDto>> ListMyDueBillsAsync(int reminderDaysBeforeDue, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "MaintenanceBill", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view maintenance bills.");
        }

        var cutoff = clock.UtcNow.AddDays(reminderDaysBeforeDue <= 0 ? 10 : reminderDaysBeforeDue);

        var pendingBillIds = await db.MaintenancePayments.AsNoTracking()
            .Where(p => p.Status == DomainPendingApproval)
            .Select(p => p.MaintenanceBillId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var pendingSet = pendingBillIds.ToHashSet();

        var rows = await db.MaintenanceBills.AsNoTracking()
            .Where(b => (b.Status == DomainMaintenanceBillStatuses.Unpaid || b.Status == DomainMaintenanceBillStatuses.PartiallyPaid)
                && (b.DueDateUtc == null || b.DueDateUtc <= cutoff))
            .OrderBy(b => b.DueDateUtc)
            .ToListAsync(cancellationToken);

        return rows
            .Where(b => !pendingSet.Contains(b.Id))
            .Select(ToDto)
            .ToList();
    }

    public async Task<PagedData<MaintenanceBillDto>> ListMyBillsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "MaintenanceBill", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view maintenance bills.");
        }

        var paged = await db.MaintenanceBills.AsNoTracking().ToPagedResultAsync(
            parameters, MyBillQueryConfig, b => ToDto(b), pageSizePolicy, executor, cancellationToken);

        var unitNumbers = await ResolveUnitNumbersAsync(
            paged.Items.Select(b => b.UnitId), cancellationToken);

        var pendingBillIds = await db.MaintenancePayments.AsNoTracking()
            .Where(p => p.Status == DomainPendingApproval && paged.Items.Select(b => b.Id).Contains(p.MaintenanceBillId))
            .Select(p => p.MaintenanceBillId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var pendingSet = pendingBillIds.ToHashSet();

        return new PagedData<MaintenanceBillDto>
        {
            Items = paged.Items
                .Select(b =>
                {
                    var enriched = unitNumbers.TryGetValue(b.UnitId, out var un) ? b with { UnitNumber = un } : b;
                    return pendingSet.Contains(b.Id) ? enriched with { PendingApproval = true } : enriched;
                })
                .ToList(),
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalCount = paged.TotalCount,
        };
    }

    private static void ApplyPaymentToBill(DomainMaintenanceBill bill, decimal amount)
    {
        bill.AmountPaid += amount;
        bill.Status = bill.AmountPaid >= bill.Amount
            ? DomainMaintenanceBillStatuses.Paid
            : DomainMaintenanceBillStatuses.PartiallyPaid;
    }

    private Guid? ParseUserId() =>
        Guid.TryParse(tenant.UserId, out var id) ? id : null;

    public async Task<MaintenanceBillDto> WaiveBillAsync(Guid billId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "MaintenanceBill", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to waive a maintenance bill.");
        }

        var bill = await db.MaintenanceBills.FirstOrDefaultAsync(b => b.Id == billId, cancellationToken)
            ?? throw new NotFoundException("Maintenance bill not found.");

        authorizationGuard.EnsureCommunityAuthorized(bill.CommunityId);

        if (string.Equals(bill.Status, DomainMaintenanceBillStatuses.Paid, StringComparison.Ordinal))
        {
            throw new ValidationException("billId", "A fully paid bill cannot be waived.");
        }

        bill.Status = DomainMaintenanceBillStatuses.Waived;

        await audit.AppendAsync(
            new AuditEntry("Update", "MaintenanceBill", bill.Id.ToString(), bill.CommunityId, Detail: "status=waived"),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(bill);
    }

    private static readonly string[] MonthNames =
    {
        "", "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    };

    private static string PeriodLabel(int year, int month) =>
        $"{(month >= 1 && month <= 12 ? MonthNames[month] : month.ToString())} {year}";

    private async Task<string> UnitNumberAsync(Guid? unitId, CancellationToken cancellationToken)
    {
        if (unitId is null)
        {
            return "";
        }

        return await db.Units.AsNoTracking()
            .Where(u => u.Id == unitId.Value)
            .Select(u => u.UnitNumber)
            .FirstOrDefaultAsync(cancellationToken) ?? "";
    }

    private async Task NotifyPaymentApproversAsync(
        Guid communityId, IReadOnlyDictionary<string, string> placeholders, CancellationToken cancellationToken)
    {
        var approverUserIds = await (
            from ur in db.UserRoles.AsNoTracking()
            where ur.CommunityId == communityId || ur.CommunityId == null
            join rp in db.RolePermissions.AsNoTracking() on ur.RoleId equals rp.RoleId
            join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            where p.ResourceType == "MaintenancePayment" && p.Action == PermissionAction.Approve
            select ur.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var userId in approverUserIds)
        {
            await notifications.EnqueueAsync(
                new NotificationRequest(NotificationTemplateKeys.MaintenancePaymentSubmitted, userId, communityId, placeholders),
                cancellationToken);
        }
    }

    private async Task NotifyUnitResidentsAsync(
        Guid communityId, Guid? unitId, string templateKey,
        IReadOnlyDictionary<string, string> placeholders, CancellationToken cancellationToken)
    {
        if (unitId is null)
        {
            return;
        }

        var userIds = await UnitResidentUserIdsAsync(communityId, new[] { unitId.Value }, cancellationToken);
        foreach (var userId in userIds)
        {
            await notifications.EnqueueAsync(
                new NotificationRequest(templateKey, userId, communityId, placeholders), cancellationToken);
        }
    }

    private async Task NotifyBillGeneratedAsync(
        Guid communityId, int periodYear, int periodMonth,
        IReadOnlyList<(Guid UnitId, decimal Amount)> bills, CancellationToken cancellationToken)
    {
        if (bills.Count == 0)
        {
            return;
        }

        var unitIds = bills.Select(b => b.UnitId).Distinct().ToList();

        var residentsByUnit = await (
            from hm in db.HouseholdMembers.AsNoTracking()
            where hm.CommunityId == communityId && hm.UnitId != null && unitIds.Contains(hm.UnitId.Value)
            join r in db.Residents.AsNoTracking() on hm.ResidentId equals r.Id
            where r.UserId != null
            select new { UnitId = hm.UnitId!.Value, UserId = r.UserId!.Value })
            .Distinct()
            .ToListAsync(cancellationToken);

        var lookup = residentsByUnit
            .GroupBy(x => x.UnitId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.UserId).Distinct().ToList());

        var period = PeriodLabel(periodYear, periodMonth);

        foreach (var (unitId, amount) in bills)
        {
            if (!lookup.TryGetValue(unitId, out var userIds))
            {
                continue;
            }

            var placeholders = new Dictionary<string, string>
            {
                ["amount"] = amount.ToString("0.00"),
                ["period"] = period,
            };

            foreach (var userId in userIds)
            {
                await notifications.EnqueueAsync(
                    new NotificationRequest(NotificationTemplateKeys.MaintenanceBillGenerated, userId, communityId, placeholders),
                    cancellationToken);
            }
        }
    }

    private async Task<List<Guid>> UnitResidentUserIdsAsync(Guid communityId, IReadOnlyList<Guid> unitIds, CancellationToken cancellationToken)
    {
        return await (
            from hm in db.HouseholdMembers.AsNoTracking()
            where hm.CommunityId == communityId && hm.UnitId != null && unitIds.Contains(hm.UnitId.Value)
            join r in db.Residents.AsNoTracking() on hm.ResidentId equals r.Id
            where r.UserId != null
            select r.UserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    private async Task RequireUnitInCommunityAsync(Guid unitId, Guid communityId, CancellationToken cancellationToken)
    {
        var unitCommunityId = await db.Units.AsNoTracking()
            .Where(u => u.Id == unitId)
            .Select(u => (Guid?)u.CommunityId)
            .FirstOrDefaultAsync(cancellationToken);

        if (unitCommunityId is null || unitCommunityId.Value != communityId)
        {
            throw new ValidationException("unitId", "The unit must belong to the same community.");
        }
    }

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

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static MaintenanceChargeDto ToDto(DomainMaintenanceCharge c) =>
        new(c.Id, c.CommunityId, c.UnitId, c.Amount, c.Label, c.IsActive);

    private static MaintenanceBillDto ToDto(DomainMaintenanceBill b) =>
        new(b.Id, b.CommunityId, b.UnitId ?? Guid.Empty, b.PeriodYear, b.PeriodMonth, b.Amount, b.AmountPaid,
            b.Amount - b.AmountPaid, b.Label, b.Status, b.DueDateUtc, b.IssuedAtUtc);

    private static MaintenanceExpenseDto ToDto(DomainMaintenanceExpense e) =>
        new(e.Id, e.CommunityId, e.Direction, e.Category, e.Title, e.Amount, e.SpentOnUtc, e.Vendor, e.Notes);

    private static MaintenancePaymentDto ToDto(DomainMaintenancePayment p) =>
        new(p.Id, p.CommunityId, p.MaintenanceBillId, p.UnitId ?? Guid.Empty, p.Amount, p.Method, p.Reference, p.PaidAtUtc,
            p.Status, p.SubmittedByResident, p.ApprovedAtUtc);
}
