using System.Globalization;
using App.Application.Abstractions;
using App.Application.Abstractions.Security;
using App.Application.Audit;
using App.Application.Community;
using App.Application.Config;
using App.Application.Exceptions;
using App.Contracts.Errors;
using App.Domain.Community;
using App.Infrastructure.Files;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Community;

/// <summary>
/// EF-backed <see cref="IUnitBulkService"/> (Req 21.1–21.5, 65.2) — bulk import/export of Units as CSV.
///
/// <para><b>Partial-success import (Req 21.1, 21.2).</b> Every row is validated independently; a
/// valid row is created, an invalid row is skipped with a per-row reason and processing continues.
/// Validation resolves the configurable <c>Unit_Type</c>/<c>Unit_Status</c> lists <i>once</i> for
/// the target community (not per row — no N+1) and loads the community's existing unit numbers
/// <i>once</i>, so a duplicate within the file or against an existing unit becomes a per-row skip
/// rather than a whole-batch failure. The DB unique index <c>(CommunityId, UnitNumber)</c> stays the
/// final authority: valid rows are saved in one batch and, if a concurrent create races a row to the
/// index, the residual <see cref="ConflictException"/> is isolated to the offending row (the rest of
/// the batch still commits) — never a read-then-write pre-check that two concurrent imports could
/// both pass.</para>
///
/// <para><b>Export⊆View (Req 21.3, 66.6).</b> Export calls
/// <see cref="IAuthorizationGuard.EnsureCanExportAsync"/> (the caller must also hold View) and reads
/// through the tenant-filtered <c>db.Units</c>, so the exported set can never exceed the caller's
/// viewable, authorized scope.</para>
///
/// <para><b>Bounded (Req 21.4, 65.2).</b> Both paths honour the configured
/// <see cref="BulkOptions.MaxRowsPerOperation"/>: an oversized import is rejected with
/// PAYLOAD_TOO_LARGE before any work, and the export projects at most that many rows with
/// <c>AsNoTracking</c>. Both operations append an Audit_Log entry with the counts (Req 21.5).</para>
/// </summary>
public sealed class UnitBulkService(
    AppDbContext db,
    IAuditService audit,
    IConfigService config,
    IAuthorizationGuard authorizationGuard,
    ITenantContext tenant,
    IOptions<BulkOptions> bulkOptions)
    : IUnitBulkService
{
    private const string DuplicateReason = "A unit with this number already exists in the community.";
    private readonly BulkOptions _bulk = bulkOptions.Value;

    public async Task<BulkImportResult> ImportUnitsAsync(
        Guid communityId, string csvContent, CancellationToken cancellationToken = default)
    {
        // Defense-in-depth: the Create gate ran on the endpoint; block a cross-tenant communityId
        // (same convention as the File upload endpoint).
        authorizationGuard.EnsureCommunityAuthorized(communityId);

        var records = UnitCsv.Parse(csvContent ?? string.Empty);

        // Row cap FIRST (Req 21.4): reject an oversized file before processing any row.
        if (records.Count > _bulk.MaxRowsPerOperation)
        {
            throw new AppExceptionWithCode(ErrorCode.PayloadTooLarge,
                $"The import exceeds the maximum of {_bulk.MaxRowsPerOperation} rows per operation.");
        }

        // Resolve the configurable type/status lists ONCE for this community (Req 21.1, no N+1).
        var allowedTypes = await LoadCodesAsync(MasterDataKeys.UnitType, communityId, cancellationToken);
        var allowedStatuses = await LoadCodesAsync(MasterDataKeys.UnitStatus, communityId, cancellationToken);

        // Load the community's existing unit numbers ONCE so a duplicate is a per-row skip, not a
        // batch abort. The DB unique index remains the authority for concurrent races below.
        var existingNumbers = await db.Units.AsNoTracking()
            .Where(u => u.CommunityId == communityId)
            .Select(u => u.UnitNumber)
            .ToListAsync(cancellationToken);
        var seenNumbers = new HashSet<string>(existingNumbers, StringComparer.OrdinalIgnoreCase);

        var rejected = new List<RejectedRow>();
        var toCreate = new List<PendingUnit>();

        for (var i = 0; i < records.Count; i++)
        {
            var rowNumber = i + 1; // 1-based, header excluded
            if (TryBuildUnit(records[i], communityId, allowedTypes, allowedStatuses, seenNumbers,
                    out var unit, out var reason))
            {
                toCreate.Add(new PendingUnit(unit!, rowNumber));
            }
            else
            {
                rejected.Add(new RejectedRow(rowNumber, reason!));
            }
        }

        var created = await PersistAsync(toCreate, rejected, cancellationToken);

        // Audit the import operation with counts (Req 21.5).
        await audit.AppendAsync(
            new AuditEntry("BulkImport", "Unit", ResourceId: null, communityId,
                Detail: $"total={records.Count};created={created};rejected={rejected.Count}"),
            cancellationToken);

        // Rejections are reported in file order regardless of the create/skip interleaving above.
        rejected.Sort((a, b) => a.RowNumber.CompareTo(b.RowNumber));
        return new BulkImportResult(records.Count, created, rejected);
    }

    public async Task<BulkExportResult> ExportUnitsAsync(CancellationToken cancellationToken = default)
    {
        // Export⊆View (Req 21.3, 66.6): the caller must also hold View on Unit. The endpoint's own
        // Export requirement is enforced by the gate; together with the tenant filter below the
        // exported set stays a subset of the viewable, authorized set.
        await authorizationGuard.EnsureCanExportAsync("Unit", cancellationToken);

        // Tenant-filtered + AsNoTracking read, capped to the configured maximum (Req 21.4, 65.2).
        // Project only the exported columns; never materialize the whole entity graph.
        var units = await db.Units.AsNoTracking()
            .OrderBy(u => u.UnitNumber)
            .Take(_bulk.MaxRowsPerOperation)
            .Select(u => new
            {
                u.Id,
                u.CommunityId,
                u.UnitNumber,
                u.UnitType,
                u.Area,
                u.Floor,
                u.Bedrooms,
                u.UnitStatus,
            })
            .ToListAsync(cancellationToken);

        var csv = UnitCsv.Write(units.Select(u => new[]
        {
            u.Id.ToString(),
            u.CommunityId.ToString(),
            u.UnitNumber,
            u.UnitType,
            UnitCsv.Number(u.Area),
            u.Floor,
            UnitCsv.Number(u.Bedrooms),
            u.UnitStatus,
        }));

        // Audit the export operation with the row count and acting-user scope (Req 21.5). CommunityId
        // is null because an export can span the caller's authorized communities.
        await audit.AppendAsync(
            new AuditEntry("BulkExport", "Unit", ResourceId: null, CommunityId: null,
                Detail: $"rows={units.Count};authorizedCommunities={tenant.AuthorizedCommunityIds.Count}"),
            cancellationToken);

        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        return new BulkExportResult(bytes, "text/csv", "units-export.csv", units.Count);
    }

    /// <summary>
    /// Save the valid rows. The happy path is a single batched <c>SaveChanges</c>. If a concurrent
    /// create raced a row to the unique index (residual conflict), fall back to saving the batch
    /// one row at a time so the offending row is isolated as a per-row rejection and the rest still
    /// commit — partial success is preserved under concurrency (Req 21.2, 22.1).
    /// </summary>
    private async Task<int> PersistAsync(
        List<PendingUnit> toCreate, List<RejectedRow> rejected, CancellationToken cancellationToken)
    {
        if (toCreate.Count == 0)
        {
            return 0;
        }

        db.Units.AddRange(toCreate.Select(p => p.Unit));
        try
        {
            await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
                () => db.SaveChangesAsync(cancellationToken), DuplicateReason);
            return toCreate.Count;
        }
        catch (ConflictException)
        {
            // Detach the whole failed batch, then re-save each row on its own so a single racing
            // duplicate cannot sink its valid siblings.
            foreach (var pending in toCreate)
            {
                db.Entry(pending.Unit).State = EntityState.Detached;
            }

            return await PersistIndividuallyAsync(toCreate, rejected, cancellationToken);
        }
    }

    private async Task<int> PersistIndividuallyAsync(
        List<PendingUnit> toCreate, List<RejectedRow> rejected, CancellationToken cancellationToken)
    {
        var created = 0;
        foreach (var pending in toCreate)
        {
            db.Units.Add(pending.Unit);
            try
            {
                await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
                    () => db.SaveChangesAsync(cancellationToken), DuplicateReason);
                created++;
            }
            catch (ConflictException)
            {
                db.Entry(pending.Unit).State = EntityState.Detached;
                // Attribute the conflict to this unit's original file row (header excluded).
                rejected.Add(new RejectedRow(pending.RowNumber, DuplicateReason));
            }
        }

        return created;
    }

    /// <summary>A validated unit awaiting persistence, carrying its 1-based file row for conflict reporting.</summary>
    private readonly record struct PendingUnit(Unit Unit, int RowNumber);

    private async Task<HashSet<string>> LoadCodesAsync(string listKey, Guid communityId, CancellationToken cancellationToken)
    {
        var entries = await config.GetMasterDataAsync(listKey, communityId, cancellationToken: cancellationToken);
        return new HashSet<string>(entries.Select(e => e.Code), StringComparer.Ordinal);
    }

    /// <summary>
    /// Validate one parsed row and, when valid, build the <see cref="Unit"/> to create. Returns false
    /// with a reason for an invalid row (Req 21.2). A duplicate — within the file (via
    /// <paramref name="seenNumbers"/>) or against an existing unit — is a per-row failure.
    /// </summary>
    private static bool TryBuildUnit(
        IReadOnlyDictionary<string, string?> record,
        Guid communityId,
        HashSet<string> allowedTypes,
        HashSet<string> allowedStatuses,
        HashSet<string> seenNumbers,
        out Unit? unit,
        out string? reason)
    {
        unit = null;
        reason = null;

        var unitNumber = Get(record, "unitnumber");
        if (string.IsNullOrWhiteSpace(unitNumber))
        {
            reason = "A unit number is required.";
            return false;
        }

        unitNumber = unitNumber.Trim();

        var unitType = Get(record, "unittype")?.Trim();
        if (string.IsNullOrWhiteSpace(unitType) || !allowedTypes.Contains(unitType))
        {
            reason = $"'{unitType}' is not a valid unit type.";
            return false;
        }

        var unitStatus = Get(record, "unitstatus")?.Trim();
        if (string.IsNullOrWhiteSpace(unitStatus) || !allowedStatuses.Contains(unitStatus))
        {
            reason = $"'{unitStatus}' is not a valid unit status.";
            return false;
        }

        if (!TryParseDecimal(Get(record, "area"), out var area))
        {
            reason = "Area must be a number.";
            return false;
        }

        if (!TryParseInt(Get(record, "bedrooms"), out var bedrooms))
        {
            reason = "Bedrooms must be a whole number.";
            return false;
        }

        // Duplicate within the file or against an existing unit → per-row skip (not a batch abort).
        if (!seenNumbers.Add(unitNumber))
        {
            reason = DuplicateReason;
            return false;
        }

        var floor = Get(record, "floor")?.Trim();
        unit = new Unit
        {
            CommunityId = communityId,
            UnitNumber = unitNumber,
            UnitType = unitType,
            UnitStatus = unitStatus,
            Area = area,
            Bedrooms = bedrooms,
            Floor = string.IsNullOrWhiteSpace(floor) ? null : floor,
        };
        return true;
    }

    private static string? Get(IReadOnlyDictionary<string, string?> record, string key)
        => record.TryGetValue(key, out var value) ? value : null;

    private static bool TryParseDecimal(string? raw, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true; // optional
        }

        if (decimal.TryParse(raw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static bool TryParseInt(string? raw, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true; // optional
        }

        if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }
}
