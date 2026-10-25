namespace UrbanNest.Application.Residents;

/// <summary>
/// Resident communication-preference management (Req 20.1, 20.2, Task 12.6). Stores and reads a
/// resident's per-category opt-in decisions; the Notification_Service consumes the resulting rows
/// through the separate <c>ICommunicationPreferences</c> seam to gate OPTIONAL categories (Req 20.3).
///
/// <para><b>Configurable categories (steering: no hardcoding).</b> Every category is validated
/// against the editable <c>Notification_Category</c> master-data list resolved for the resident's
/// community — not a frozen enum.</para>
///
/// <para><b>Tenant scope + audit.</b> Reads are tenant-scoped by the global query filter (Req 5.2)
/// and use <c>AsNoTracking</c>; the update is audited (Req 6.1). A resident edits only their own
/// preferences — the self-scope/authorization is applied by the Task 12.7 endpoint gate on top of
/// the tenant filter that already bounds the resident row to the caller's authorized communities.</para>
/// </summary>
public interface ICommunicationPreferenceService
{
    /// <summary>
    /// Get a resident's stored communication preferences (Req 20.1), tenant-scoped. Returns only the
    /// categories the resident has an explicit row for; absent categories fall back to the configured
    /// default at send time. Unknown/foreign-tenant resident → not found.
    /// </summary>
    Task<IReadOnlyList<CommunicationPreferenceDto>> GetAsync(Guid residentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Store/update a resident's communication preferences (Req 20.2): upsert one opt-in row per
    /// category in the request. Each category is validated against the configurable
    /// <c>Notification_Category</c> list (invalid → validation error). The change is committed in one
    /// transaction and audited. Unknown/foreign-tenant resident → not found. Returns the resident's
    /// full set of stored preferences after the update.
    /// </summary>
    Task<IReadOnlyList<CommunicationPreferenceDto>> SetAsync(
        Guid residentId,
        SetCommunicationPreferencesRequest request,
        CancellationToken cancellationToken = default);
}
