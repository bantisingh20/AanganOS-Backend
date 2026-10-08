namespace CommunityOS.Application.Notifications;

/// <summary>
/// What a module hands the Notification_Service to enqueue a template-based notification (Req 11.1,
/// 11.2). The caller names a <see cref="TemplateKey"/>, the intended <see cref="RecipientUserId"/>
/// (Req 11.3 — exactly one recipient per request), the owning <see cref="CommunityId"/> (for
/// community-scoped template resolution and tenant stamping), the <see cref="Placeholders"/> that
/// fill the template's <c>{token}</c>s, and optional <see cref="PayloadJson"/> for the push data.
///
/// <para>The service does NOT accept a title/body directly — only a template key — so every
/// notification goes through a named, reusable, editable template (steering: no hardcoded strings).</para>
/// </summary>
/// <param name="TemplateKey">The named template to render (Req 11.1).</param>
/// <param name="RecipientUserId">The single intended recipient (Req 11.3, 64.6).</param>
/// <param name="CommunityId">The originating/owning community — scopes template resolution and the outbox row.</param>
/// <param name="Placeholders">Values that fill the template's <c>{token}</c>s; keys are token names without braces.</param>
/// <param name="PayloadJson">Optional structured push payload scoped to the intended recipient only.</param>
/// <param name="TargetPlayerId">
/// Optional explicit push target (a OneSignal player id). When set, the worker delivers to exactly
/// this device instead of resolving the recipient's current active devices — used to notify a device
/// that is being deregistered in the same operation (e.g. "signed out elsewhere"). Null = normal
/// user-resolved delivery.
/// </param>
public sealed record NotificationRequest(
    string TemplateKey,
    Guid RecipientUserId,
    Guid CommunityId,
    IReadOnlyDictionary<string, string>? Placeholders = null,
    string? PayloadJson = null,
    string? TargetPlayerId = null);
