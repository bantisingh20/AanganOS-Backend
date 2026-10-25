using UrbanNest.Application.Notifications;
using UrbanNest.Domain.Notifications;
using UrbanNest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace UrbanNest.Infrastructure.Notifications;

/// <summary>
/// EF-backed Notification_Service (Req 11, 20.3–20.4, 64.6), Phase 1 Task 7.1 scope: resolve a
/// named template, apply the resident's communication preference, render it for the single intended
/// recipient, and write a <b>transactional outbox</b> row inside the caller's business transaction.
/// It never calls OneSignal — the Task 7.2 worker drains the outbox (Req 11.5).
///
/// <para><b>Transactional outbox.</b> <see cref="EnqueueAsync"/> only <c>Add</c>s the outbox row to
/// the shared request-scoped <see cref="AppDbContext"/>; it does NOT call <c>SaveChanges</c>. The
/// caller's own <c>SaveChanges</c> commits the business change and the outbox row atomically, so a
/// notification is queued only if the originating operation commits and a rollback leaves no stale
/// notification (Req 11.5). Dispatch is therefore always deferred and non-blocking.</para>
///
/// <para><b>Template resolution.</b> Community override wins over the platform default for the same
/// key (same fallback as Config_Service, Req 8.1/8.3). <see cref="NotificationTemplate"/> is a
/// platform-scoped entity, so the tenant global filter does not hide platform defaults; community
/// isolation is enforced here by only ever selecting the community id the caller passed.</para>
///
/// <para><b>Confidentiality (Req 11.3, 64.6).</b> Each request targets exactly one recipient; the
/// outbox row carries that single <see cref="NotificationOutboxMessage.RecipientUserId"/> and the
/// rendered text/payload are scoped to that recipient. No fan-out.</para>
///
/// <para><b>Preferences (Req 20.3, 20.4).</b> A mandatory template (account/security) is always
/// queued. An optional template is queued only if <see cref="ICommunicationPreferences"/> permits
/// the category for the recipient; otherwise a <see cref="OutboxStatus.Suppressed"/> row is written
/// so the decision is auditable and the call returns <see cref="EnqueueOutcome.SuppressedByPreference"/>.</para>
/// </summary>
public sealed class NotificationService(
    AppDbContext db,
    INotificationTemplateRenderer renderer,
    ICommunicationPreferences preferences) : INotificationService
{
    public async Task<EnqueueOutcome> EnqueueAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplateKey);

        var template = await ResolveTemplateAsync(request.TemplateKey, request.CommunityId, cancellationToken);
        if (template is null)
        {
            // Misconfiguration, not a user error: do not throw into the business op. The caller
            // gets a non-Queued outcome it can log; the originating operation is unaffected (Req 11.5).
            return EnqueueOutcome.TemplateNotFound;
        }

        // Preference gate: mandatory account/security notices bypass preferences (Req 20.4);
        // optional categories require the resident's opt-in (Req 20.3).
        var mandatory = template.CategoryKind == NotificationCategoryKind.Mandatory;
        var allowed = mandatory || await preferences.IsCategoryAllowedAsync(
            request.RecipientUserId, request.CommunityId, template.Category, cancellationToken);

        var title = renderer.Render(template.TitleTemplate, request.Placeholders);
        var body = renderer.Render(template.BodyTemplate, request.Placeholders);

        var message = new NotificationOutboxMessage
        {
            CommunityId = request.CommunityId,
            TemplateKey = template.Key,
            Category = template.Category,
            CategoryKind = template.CategoryKind,
            Channels = template.Channels,
            RecipientUserId = request.RecipientUserId,
            TargetPlayerId = string.IsNullOrWhiteSpace(request.TargetPlayerId) ? null : request.TargetPlayerId,
            Title = title,
            Body = body,
            PayloadJson = request.PayloadJson,
            Status = allowed ? OutboxStatus.Pending : OutboxStatus.Suppressed,
            Attempts = 0,
            NextAttemptUtc = null,
        };

        // Transactional outbox: stage the row in the caller's context WITHOUT saving. The business
        // operation's own SaveChanges/transaction commits it atomically (Req 11.5).
        await db.Set<NotificationOutboxMessage>().AddAsync(message, cancellationToken);

        return allowed ? EnqueueOutcome.Queued : EnqueueOutcome.SuppressedByPreference;
    }

    /// <summary>
    /// Resolve the active template for the key: community override → platform default (Req 8.1/8.3
    /// semantics). Reads at most two rows and picks in memory (no N+1); AsNoTracking since the
    /// template is only read, not mutated, here.
    /// </summary>
    private async Task<NotificationTemplate?> ResolveTemplateAsync(string key, Guid communityId, CancellationToken cancellationToken)
    {
        var rows = await db.Set<NotificationTemplate>()
            .AsNoTracking()
            .Where(t => t.Key == key && t.IsActive &&
                        (t.CommunityId == null || t.CommunityId == communityId))
            .ToListAsync(cancellationToken);

        return rows.FirstOrDefault(t => t.CommunityId == communityId)
               ?? rows.FirstOrDefault(t => t.CommunityId == null);
    }
}
