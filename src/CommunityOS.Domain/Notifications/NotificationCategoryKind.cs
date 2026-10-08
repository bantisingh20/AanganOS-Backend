namespace CommunityOS.Domain.Notifications;

/// <summary>
/// Whether a template's category is <see cref="Mandatory"/> (account/security notices that are
/// always delivered regardless of a resident's optional preferences — Req 20.4) or
/// <see cref="Optional"/> (delivered only if the resident opted into the category — Req 20.3).
///
/// <para>The <i>category</i> itself (verification, parcel, billing, ...) is open-ended data living
/// on each template; this enum only classifies whether a resident's opt-out can suppress it. That
/// keeps the mandatory/optional rule a property of the template data, not hardcoded per category.</para>
/// </summary>
public enum NotificationCategoryKind
{
    /// <summary>Delivered only when the resident has opted into the template's category (Req 20.3).</summary>
    Optional = 0,

    /// <summary>Account/security notice — always delivered, bypassing optional preferences (Req 20.4).</summary>
    Mandatory = 1,
}
