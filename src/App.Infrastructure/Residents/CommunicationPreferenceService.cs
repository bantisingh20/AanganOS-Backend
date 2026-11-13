using App.Application.Audit;
using App.Application.Config;
using App.Application.Exceptions;
using App.Application.Residents;
using App.Domain.Residents;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Residents;

/// <summary>
/// EF-backed <see cref="ICommunicationPreferenceService"/> (Req 20.1, 20.2, Task 12.6): read and
/// upsert a resident's per-category opt-in rows.
///
/// <para><b>Configurable category (steering: no hardcoding).</b> Each category in a set request is
/// validated against the editable <c>Notification_Category</c> master-data list resolved for the
/// resident's own community (so a per-community override applies) — not a frozen enum.</para>
///
/// <para><b>Tenant scope + audit.</b> The resident is resolved through the global tenant filter, so
/// an unknown/foreign-tenant id is a not-found; the upsert and its audit row commit in one
/// <c>SaveChangesAsync</c> (Req 6.1). The <c>Unique(ResidentId, Category)</c> index makes the per-
/// category row single — a changed preference updates the existing row rather than inserting a
/// duplicate.</para>
/// </summary>
public sealed class CommunicationPreferenceService(
    AppDbContext db,
    IAuditService audit,
    IConfigService config)
    : ICommunicationPreferenceService
{
    public async Task<IReadOnlyList<CommunicationPreferenceDto>> GetAsync(Guid residentId, CancellationToken cancellationToken = default)
    {
        // Confirm the resident is visible to the caller (tenant-scoped) before returning children, so
        // a cross-tenant/unknown id gets a not-found rather than an empty list.
        var exists = await db.Residents.AsNoTracking().AnyAsync(r => r.Id == residentId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException("Resident not found.");
        }

        var prefs = await db.CommunicationPreferences.AsNoTracking()
            .Where(p => p.ResidentId == residentId)
            .OrderBy(p => p.Category)
            .ToListAsync(cancellationToken);

        return prefs.Select(p => new CommunicationPreferenceDto(p.Category, p.OptedIn)).ToList();
    }

    public async Task<IReadOnlyList<CommunicationPreferenceDto>> SetAsync(
        Guid residentId,
        SetCommunicationPreferencesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Preferences is null || request.Preferences.Count == 0)
        {
            throw new ValidationException("preferences", "At least one communication preference is required.");
        }

        // Tracked load so a changed preference updates the existing row (RowVersion carried into the
        // UPDATE). The global tenant filter scopes it to the caller — unknown/foreign-tenant → not found.
        var resident = await db.Residents
            .Where(r => r.Id == residentId)
            .Select(r => new { r.Id, r.CommunityId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Resident not found.");

        // Validate every category against the configurable Notification_Category list resolved for
        // the resident's community (Req 20.1, steering: no hardcoding). One list read, checked in
        // memory — no N+1 per category.
        var allowed = await config.GetMasterDataAsync(
            MasterDataKeys.NotificationCategory, resident.CommunityId, cancellationToken: cancellationToken);
        var allowedCodes = allowed.Select(e => e.Code).ToHashSet(StringComparer.Ordinal);

        // Collapse duplicate categories in the request (last write wins) before touching the DB.
        var updates = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var update in request.Preferences)
        {
            var category = update.Category?.Trim();
            if (string.IsNullOrEmpty(category))
            {
                throw new ValidationException("category", "A notification category is required.");
            }

            if (!allowedCodes.Contains(category))
            {
                throw new ValidationException("category", $"'{category}' is not a valid notification category.");
            }

            updates[category] = update.OptedIn;
        }

        // Load the resident's existing rows for exactly the categories being changed (single indexed
        // read on (ResidentId, Category)); upsert each — update the row if present, insert otherwise.
        var categories = updates.Keys.ToList();
        var existing = await db.CommunicationPreferences
            .Where(p => p.ResidentId == residentId && categories.Contains(p.Category))
            .ToListAsync(cancellationToken);
        var byCategory = existing.ToDictionary(p => p.Category, StringComparer.Ordinal);

        foreach (var (category, optedIn) in updates)
        {
            if (byCategory.TryGetValue(category, out var row))
            {
                row.OptedIn = optedIn;
            }
            else
            {
                db.CommunicationPreferences.Add(new CommunicationPreference
                {
                    CommunityId = resident.CommunityId,
                    ResidentId = resident.Id,
                    Category = category,
                    OptedIn = optedIn,
                });
            }
        }

        await audit.AppendAsync(
            new AuditEntry("Update", "CommunicationPreference", resident.Id.ToString(), resident.CommunityId, Detail: null),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return await GetAsync(residentId, cancellationToken);
    }
}
