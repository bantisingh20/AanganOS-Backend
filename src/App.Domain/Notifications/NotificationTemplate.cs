using App.Domain.Common;
using App.Domain.Config;

namespace App.Domain.Notifications;

/// <summary>
/// A named, reusable notification template (Req 11.1) — the title/body with <c>{placeholder}</c>
/// tokens, its category and the channels it targets, all stored as <b>data</b> so templates are
/// added/edited without a code change (steering: no hardcoding; the full notification matrix is
/// Req 64 / later phases). Phase 1 seeds the resident verification/approval templates (Req 11.2).
///
/// <para><b>Scoping mirrors <see cref="Configuration"/>/<see cref="MasterDataItem"/>.</b> A template
/// is a <see cref="PlatformEntity"/>, not a <see cref="TenantOwnedEntity"/>: a
/// <see cref="ConfigScope.Platform"/> row (null <see cref="CommunityId"/>) is the default every
/// community sees, and a <see cref="ConfigScope.Community"/> row lets one community override the
/// wording for the same <see cref="Key"/>. Platform rows must stay readable for the fallback, so —
/// exactly like Configuration — the global tenant filter is intentionally not applied (it would
/// hide platform defaults); community isolation is enforced one layer up in Notification_Service,
/// which only ever resolves a community row for the community id the caller passed.</para>
/// </summary>
public class NotificationTemplate : PlatformEntity
{
    /// <summary>Stable template name callers reference (e.g. <c>resident.verification.approved</c>). Required.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Platform-wide default template or a per-community override (Req 8.1 semantics).</summary>
    public ConfigScope Scope { get; set; }

    /// <summary>Owning community for a community-scoped template; null for a platform default.</summary>
    public Guid? CommunityId { get; set; }

    /// <summary>Grouping category used for the resident opt-in check (e.g. <c>verification</c>, <c>billing</c>). Required.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Whether the category is mandatory (account/security — always sent, Req 20.4) or optional
    /// (sent only if the resident opted in, Req 20.3).
    /// </summary>
    public NotificationCategoryKind CategoryKind { get; set; } = NotificationCategoryKind.Optional;

    /// <summary>Title with <c>{placeholder}</c> tokens filled from the enqueue data. Required.</summary>
    public string TitleTemplate { get; set; } = string.Empty;

    /// <summary>Body with <c>{placeholder}</c> tokens filled from the enqueue data. Required.</summary>
    public string BodyTemplate { get; set; } = string.Empty;

    /// <summary>Channels this template targets (bit flags, Req 64). Phase 1 dispatches the Push bit.</summary>
    public NotificationChannel Channels { get; set; } = NotificationChannel.Push;

    /// <summary>When false the template is retired: kept for history but not offered for new sends.</summary>
    public bool IsActive { get; set; } = true;
}
