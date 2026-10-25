using UrbanNest.Application.Notifications;
using UrbanNest.Domain.Residents;
using UrbanNest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace UrbanNest.Infrastructure.Notifications;

/// <summary>
/// The REAL per-resident <see cref="ICommunicationPreferences"/> (Req 20.3, Task 12.6). Replaces
/// <see cref="DefaultCommunicationPreferences"/>: the Notification_Service consults this for an
/// OPTIONAL category and gets the resident's stored opt-in. Mandatory account/security notices never
/// reach this seam — the Notification_Service always sends them (Req 20.4), so this type governs
/// optional categories only and keeps that contract.
///
/// <para><b>Recipient mapping.</b> The Notification_Service's recipient is a login <c>userId</c>,
/// but preferences are per-<c>Resident</c>. This resolves the link <c>Resident.UserId == userId</c>
/// (within the recipient's community) in one indexed read, then reads that resident's preference row
/// for the category (the <c>Unique(ResidentId, Category)</c> index makes it a single lookup, no
/// N+1). A row's <see cref="CommunicationPreference.OptedIn"/> is authoritative.</para>
///
/// <para><b>Graceful fallback (no hardcoding).</b> When the recipient has no linked resident, or the
/// resident has no row for the category, the configured default
/// (<see cref="NotificationOptions.OptionalCategoriesAllowedByDefault"/>) applies — so an early
/// optional notice is not silently dropped before the resident has set any preference, and the
/// default is a config value, never a literal.</para>
///
/// <para><b>Reads.</b> <c>AsNoTracking</c> read-only lookups on indexed columns; the global tenant
/// filter bounds them to the caller's authorized communities (the Task 7.2 worker drains the outbox
/// under a cross-tenant super-admin context, so its reads here are not tenant-blocked either).</para>
/// </summary>
public sealed class ResidentCommunicationPreferences(
    AppDbContext db,
    IOptions<NotificationOptions> options) : ICommunicationPreferences
{
    private readonly bool _allowByDefault = options.Value.OptionalCategoriesAllowedByDefault;

    public async Task<bool> IsCategoryAllowedAsync(
        Guid userId, Guid communityId, string category, CancellationToken cancellationToken = default)
    {
        // Map recipient userId → resident. A resident may or may not have a linked login user; scope
        // to the community the notification targets so the right resident row is picked. Project just
        // the id (no whole-entity load).
        var residentId = await db.Residents.AsNoTracking()
            .Where(r => r.UserId == userId && r.CommunityId == communityId)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // No resident behind this recipient → fall back to the configured default-allow for optional.
        if (residentId is not { } rid)
        {
            return _allowByDefault;
        }

        // Single indexed lookup on (ResidentId, Category). A stored row is authoritative; its absence
        // falls back to the configured default (Req 20.3 — opt-in governs, default fills the gap).
        var optedIn = await db.CommunicationPreferences.AsNoTracking()
            .Where(p => p.ResidentId == rid && p.Category == category)
            .Select(p => (bool?)p.OptedIn)
            .FirstOrDefaultAsync(cancellationToken);

        return optedIn ?? _allowByDefault;
    }
}
