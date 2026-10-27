using App.Domain.Common;

namespace App.Domain.Gate;

/// <summary>
/// A visitor a resident authorizes to enter on a recurring schedule (Req 25.2) — a daily maid, a
/// regular cab, a weekly service provider — so a matching date needs no fresh walk-in approval.
/// Carries its owning <see cref="TenantOwnedEntity.CommunityId"/> and the <see cref="HostUnitId"/>
/// the authorization is for.
///
/// <para><b>Resident self-view + own-unit creation (Req 5.4, 25.2).</b> Implements
/// <see cref="IUnitScoped"/> via its non-null <see cref="HostUnitId"/> surfaced as <c>UnitId</c>, so
/// the DbContext resident self-scope filter restricts a resident principal to recurring visitors for
/// their own unit — the ones they created and may revoke. No bespoke predicate in the service.</para>
///
/// <para><b>Practical schedule as DATA, not code (steering: no hardcoding).</b> The recurrence is
/// stored, never compiled: <see cref="DaysOfWeek"/> is a 7-bit mask (Sun=1 … Sat=64) of the days the
/// visitor is authorized, <see cref="StartDate"/>/<see cref="EndDate"/> bound the valid date range,
/// and the optional <see cref="DailyStartMinutes"/>/<see cref="DailyEndMinutes"/> narrow it to a
/// daily time window (minutes from midnight, local-to-UTC handled by the caller). An authorization is
/// checked against this data by <see cref="IsAuthorizedOn"/>; the shape is deliberately simple
/// (ponytail: a bitmask + date range + optional daily window covers the real "daily/weekly helper"
/// case — a full RRULE engine is unneeded; upgrade path is a stored iCal RRULE string if a community
/// ever needs it).</para>
///
/// <para><b>Configurable status (steering: no hardcoding).</b> <see cref="Status"/> is a stable code
/// resolved against the editable <c>Recurring_Visitor_Status</c> list (active / revoked) — not a
/// frozen enum; <see cref="RecurringVisitorStatuses"/> only names the seeded codes. A revoked
/// authorization never admits, regardless of the schedule.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token, audit columns.</para>
/// </summary>
public class RecurringVisitor : TenantOwnedEntity, IUnitScoped
{
    /// <summary>
    /// The host unit the recurring authorization is for (Req 25.2). Non-null in practice; typed
    /// nullable to satisfy <see cref="IUnitScoped"/>, which the resident self-scope filter keys on.
    /// This IS the host unit — see <see cref="HostUnitId"/>.
    /// </summary>
    public Guid? UnitId { get; set; }

    /// <summary>The host unit being visited (Req 25.2). Alias over <see cref="UnitId"/> for intent.</summary>
    public Guid HostUnitId
    {
        get => UnitId ?? Guid.Empty;
        set => UnitId = value;
    }

    /// <summary>The resident who defined (and may revoke) the recurring visitor (Req 25.2). Required.</summary>
    public Guid HostResidentId { get; set; }

    /// <summary>The recurring visitor's name (Req 25.2). Required.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional visitor phone (Req 25.2).</summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Configurable visitor-category code resolved against the <c>Visitor_Category</c> master-data
    /// list (guest / cab / delivery / service_provider / contractor, Req 25.1). Stored as the stable
    /// code, never a frozen enum.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 7-bit mask of authorized days-of-week (Req 25.2): Sunday=1, Monday=2, Tuesday=4, Wednesday=8,
    /// Thursday=16, Friday=32, Saturday=64 — OR the days together (e.g. Mon–Fri = 2+4+8+16+32 = 62).
    /// Zero means no day is authorized.
    /// </summary>
    public int DaysOfWeek { get; set; }

    /// <summary>First date the authorization is valid (date component, inclusive, Req 25.2).</summary>
    public DateTime StartDate { get; set; }

    /// <summary>Last date the authorization is valid (date component, inclusive, Req 25.2).</summary>
    public DateTime EndDate { get; set; }

    /// <summary>
    /// Optional daily-window start, minutes from midnight (0–1439). Null means no daily lower bound.
    /// When set together with <see cref="DailyEndMinutes"/> the authorization only holds inside the
    /// window on an authorized day.
    /// </summary>
    public int? DailyStartMinutes { get; set; }

    /// <summary>Optional daily-window end, minutes from midnight (1–1440). Null means no daily upper bound.</summary>
    public int? DailyEndMinutes { get; set; }

    /// <summary>
    /// Configurable status code resolved against the <c>Recurring_Visitor_Status</c> master-data list
    /// (active / revoked, Req 25.2). Starts <c>active</c>; a resident revoke sets <c>revoked</c>.
    /// Stored as the stable code, never a frozen enum.
    /// </summary>
    public string Status { get; set; } = RecurringVisitorStatuses.Active;

    /// <summary>Whether the authorization is currently active (not revoked). Computed, not stored.</summary>
    public bool IsActive =>
        string.Equals(Status, RecurringVisitorStatuses.Active, StringComparison.Ordinal);

    /// <summary>
    /// Whether this recurring visitor is authorized to enter at <paramref name="whenUtc"/> (Req 25.2):
    /// the authorization is <c>active</c> AND the date is within [<see cref="StartDate"/>,
    /// <see cref="EndDate"/>] AND the day-of-week bit is set AND — when a daily window is configured —
    /// the time-of-day falls inside it. Pure, side-effect-free domain rule the entry workflow calls.
    /// </summary>
    public bool IsAuthorizedOn(DateTime whenUtc)
    {
        if (!IsActive)
        {
            return false;
        }

        var day = whenUtc.Date;
        if (day < StartDate.Date || day > EndDate.Date)
        {
            return false;
        }

        // Day-of-week bit: DayOfWeek is 0=Sunday..6=Saturday, so the bit for a day is 1 << dayIndex.
        var dayBit = 1 << (int)whenUtc.DayOfWeek;
        if ((DaysOfWeek & dayBit) == 0)
        {
            return false;
        }

        // Optional daily time window (minutes from midnight). Only enforced when both bounds are set.
        if (DailyStartMinutes is { } startMin && DailyEndMinutes is { } endMin)
        {
            var minutesOfDay = (whenUtc.Hour * 60) + whenUtc.Minute;
            if (minutesOfDay < startMin || minutesOfDay > endMin)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Well-known default <c>Recurring_Visitor_Status</c> codes (Req 25.2). Seeded master-data codes kept
/// as constants so the service sets/compares status without a magic string. <b>Data</b>, not a
/// behavioural enum — the status list is editable; this is only a convenience for the known codes.
/// </summary>
public static class RecurringVisitorStatuses
{
    /// <summary>Initial status on create (Req 25.2): authorizes entry on matching dates.</summary>
    public const string Active = "active";

    /// <summary>Revoked by the host resident (Req 25.2): never admits, regardless of the schedule.</summary>
    public const string Revoked = "revoked";
}
