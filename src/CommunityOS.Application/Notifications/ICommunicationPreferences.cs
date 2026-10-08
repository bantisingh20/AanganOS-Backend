namespace CommunityOS.Application.Notifications;

/// <summary>
/// The seam Notification_Service consults to honor per-resident communication preferences
/// (Req 20.3). The real <c>CommunicationPreference</c> store is <b>Phase 2 Task 12.6</b>; defining
/// this interface now lets Task 7.1's preference logic be correct and testable without that store
/// existing yet. Task 12.6 provides the EF-backed implementation; until then a default
/// implementation applies the safe fallback below. Mandatory account/security notifications never
/// consult this seam — they are always sent (Req 20.4), a decision made in the service, not here.
///
/// <para><b>Default behavior (until 12.6).</b> The default implementation allows optional
/// categories unless the platform/community is configured to opt-out-by-default, so no
/// verification/approval notice is silently dropped before preferences exist. Task 12.6 replaces it
/// with the resident's stored, per-category opt-in (Req 20.1–20.3).</para>
/// </summary>
public interface ICommunicationPreferences
{
    /// <summary>
    /// Whether <paramref name="userId"/> permits receiving notifications in the optional
    /// <paramref name="category"/> for <paramref name="communityId"/> (Req 20.3). Only called for
    /// optional categories — mandatory categories bypass this check entirely (Req 20.4).
    /// </summary>
    Task<bool> IsCategoryAllowedAsync(
        Guid userId,
        Guid communityId,
        string category,
        CancellationToken cancellationToken = default);
}
