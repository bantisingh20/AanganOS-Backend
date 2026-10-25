using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Notifications;

/// <summary>
/// A user's registered push target — the mapping from a <see cref="UserId"/> to a OneSignal player
/// id (design Identity/data-model table: <c>Device | Id, UserId, Platform, OneSignalPlayerId,
/// LastSeen</c>). The mobile app registers its OneSignal player id here after login; the
/// Notification_Service recipient resolver turns an intended recipient user into their device(s)
/// so a message is delivered only to that user's own devices (Req 11.3, 64.6).
///
/// <para>A device belongs to a <see cref="User"/> — a platform principal, not one community — so it
/// is a <see cref="PlatformEntity"/> (same choice as <c>User</c>/<c>LoginSession</c>). It is not
/// community-scoped: a user may have devices while active in several communities, and targeting is
/// by user identity, not tenant.</para>
///
/// <para><b>Confidentiality.</b> <see cref="OneSignalPlayerId"/> is unique across devices — a given
/// push target maps to exactly one user — so a notification resolved for one recipient can never be
/// routed to another user's device.</para>
/// </summary>
public class Device : PlatformEntity
{
    /// <summary>The user this device belongs to (the push-target owner).</summary>
    public Guid UserId { get; set; }

    /// <summary>Client platform (Android for the Phase 1 apps).</summary>
    public DevicePlatform Platform { get; set; } = DevicePlatform.Unknown;

    /// <summary>
    /// The OneSignal player/subscription id the worker sends to. Unique across devices so a target
    /// maps to exactly one user (Req 11.3). Required.
    /// </summary>
    public string OneSignalPlayerId { get; set; } = string.Empty;

    /// <summary>When the device was last seen/registered (UTC). Used to prune stale targets.</summary>
    public DateTime LastSeenUtc { get; set; }

    /// <summary>When false the device is deregistered (e.g. logout/uninstall) and is not targeted.</summary>
    public bool IsActive { get; set; } = true;
}
