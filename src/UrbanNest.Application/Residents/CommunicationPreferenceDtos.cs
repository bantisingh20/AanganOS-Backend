namespace UrbanNest.Application.Residents;

/// <summary>Read shape for a single resident communication preference (Req 20.1).</summary>
/// <param name="Category">The notification-category code (from the <c>Notification_Category</c> list).</param>
/// <param name="OptedIn">Whether the resident has opted into this category.</param>
public sealed record CommunicationPreferenceDto(string Category, bool OptedIn);

/// <summary>
/// One opt-in change in a <see cref="SetCommunicationPreferencesRequest"/> (Req 20.2): the resident
/// opts into (<c>OptedIn = true</c>) or out of (<c>false</c>) the named <paramref name="Category"/>.
/// </summary>
/// <param name="Category">The notification-category code; validated against <c>Notification_Category</c>.</param>
/// <param name="OptedIn">The new opt-in value.</param>
public sealed record CommunicationPreferenceUpdate(string Category, bool OptedIn);

/// <summary>
/// Update a resident's communication preferences (Req 20.2). Each entry upserts the resident's row
/// for that category; categories not present are left unchanged. Every category is validated against
/// the configurable <c>Notification_Category</c> master-data list for the resident's community.
/// </summary>
/// <param name="Preferences">The per-category opt-in changes to persist.</param>
public sealed record SetCommunicationPreferencesRequest(
    IReadOnlyList<CommunicationPreferenceUpdate> Preferences);
