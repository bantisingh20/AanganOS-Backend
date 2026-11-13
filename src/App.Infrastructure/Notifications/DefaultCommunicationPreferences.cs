using App.Application.Notifications;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Notifications;

/// <summary>
/// Default <see cref="ICommunicationPreferences"/> seam used until Phase 2 Task 12.6 provides the
/// real per-resident <c>CommunicationPreference</c> store (Req 20.1–20.3). It applies the
/// configured fallback for OPTIONAL categories only — mandatory account/security notices never
/// reach this seam (Notification_Service always sends them, Req 20.4).
///
/// <para>Fallback is config-driven (<see cref="NotificationOptions.OptionalCategoriesAllowedByDefault"/>,
/// no hardcoding): default allow, so the Phase 1 resident verification/approval notices (which are
/// mandatory anyway) and any early optional notices are not silently dropped before preferences
/// exist. When Task 12.6 lands it registers its EF-backed implementation ahead of this one and the
/// resident's stored per-category opt-in takes over without any change to Notification_Service.</para>
/// </summary>
public sealed class DefaultCommunicationPreferences(IOptions<NotificationOptions> options) : ICommunicationPreferences
{
    private readonly bool _allowByDefault = options.Value.OptionalCategoriesAllowedByDefault;

    public Task<bool> IsCategoryAllowedAsync(
        Guid userId, Guid communityId, string category, CancellationToken cancellationToken = default)
        => Task.FromResult(_allowByDefault);
}
