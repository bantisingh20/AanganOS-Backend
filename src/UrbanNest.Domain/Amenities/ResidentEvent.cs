using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Amenities;

/// <summary>
/// A resident-published event — a party, gathering, celebration or community activity (Req 41.4,
/// 41.5, Task 24.2): an organiser announces an event with a schedule and a capacity, and residents
/// register for it up to that capacity. A tenant-owned record carrying its organiser's host
/// <see cref="UnitId"/>.
///
/// <para><b>Resident self-scope (Req 5.4, 41.4).</b> Implements <see cref="IUnitScoped"/> via its
/// non-null <see cref="UnitId"/> (the organiser's host unit at publish time), so the DbContext
/// resident self-scope filter restricts a resident principal to their OWN unit's published events
/// for edit/management — a resident manages only the events their unit organised, with no bespoke
/// predicate in the service (same stance as <c>Booking</c> / <c>Ticket</c>). The
/// <see cref="OrganizerResidentId"/> records WHO published it. A community-wide read surface for
/// browsing/registering is a separate (tenant-scoped) concern.</para>
///
/// <para><b>Capacity is counted on this row (Req 41.5).</b> Unlike the per-(slot, date) booking
/// counter, a resident event is a single occurrence, so its seat ledger lives inline:
/// <see cref="RegisteredCount"/> is the authoritative count of confirmed registrations and is mutated
/// ONLY via an atomic, DB-level guarded conditional UPDATE —
/// <c>UPDATE … SET RegisteredCount = RegisteredCount + 1 WHERE Id = @id AND RegisteredCount &lt; Capacity</c>
/// (EF Core <c>ExecuteUpdateAsync</c> with the <c>RegisteredCount &lt; Capacity</c> guard in the
/// WHERE) — the SAME capacity-concurrency guarantee as <c>SlotBookingCount</c>. Two requests racing
/// the last seat serialize at the row: exactly one sees <c>RegisteredCount &lt; Capacity</c> and
/// increments (affected rows = 1 → registered), the other finds the event full (affected rows = 0 →
/// rejected, Req 41.5). There is NO read-then-write gap in application code. A cancellation releases
/// the seat with the symmetric guarded decrement (<c>RegisteredCount &gt; 0</c>).</para>
///
/// <para><b>Configurable status (steering: no hardcoding, Req 41.4).</b> <see cref="Status"/> resolves
/// against the editable <c>Resident_Event_Status</c> master-data list (published / cancelled /
/// completed) and starts <see cref="ResidentEventStatuses.Published"/> — a stable code, never a frozen
/// enum.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token, audit columns. The guarded increment is the capacity gate; the RowVersion still
/// guards any tracked edit of the row.</para>
/// </summary>
public class ResidentEvent : TenantOwnedEntity, IUnitScoped
{
    /// <summary>
    /// The organiser's host unit at publish time (Req 41.4). Non-null in practice; typed nullable to
    /// satisfy <see cref="IUnitScoped"/>, which the resident self-scope filter keys on so a resident
    /// manages only their own unit's events.
    /// </summary>
    public Guid? UnitId { get; set; }

    /// <summary>The resident who published the event (Req 41.4): the organiser. Required.</summary>
    public Guid OrganizerResidentId { get; set; }

    /// <summary>The event title (Req 41.4). Required.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional free-text description of the event (Req 41.4).</summary>
    public string? Description { get; set; }

    /// <summary>Optional free-text venue/location within the community (Req 41.4).</summary>
    public string? Location { get; set; }

    /// <summary>When the event starts (Req 41.4). Required.</summary>
    public DateTime StartsAtUtc { get; set; }

    /// <summary>When the event ends (Req 41.4). Required; must be after <see cref="StartsAtUtc"/>.</summary>
    public DateTime EndsAtUtc { get; set; }

    /// <summary>
    /// The maximum number of registrations the event accepts (Req 41.4, 41.5). Always positive; the
    /// guarded increment permits a registration only while <see cref="RegisteredCount"/> &lt; this.
    /// </summary>
    public int Capacity { get; set; }

    /// <summary>
    /// The number of confirmed registrations currently held (Req 41.5). Mutated ONLY via the atomic
    /// guarded UPDATE/decrement described on the type; never read-modified-written in application
    /// memory. Starts 0.
    /// </summary>
    public int RegisteredCount { get; set; }

    /// <summary>
    /// Configurable status code resolved against the <c>Resident_Event_Status</c> master-data list
    /// (Req 41.4). Starts <see cref="ResidentEventStatuses.Published"/>. Stored as the stable code,
    /// never a frozen enum.
    /// </summary>
    public string Status { get; set; } = ResidentEventStatuses.Published;
}

/// <summary>
/// Well-known default <c>Resident_Event_Status</c> codes (Req 41.4). Seeded master-data codes kept as
/// constants so the service sets/compares status without a magic string. <b>Data</b>, not a
/// behavioural enum — the list is editable per community; these constants only name the seeded
/// defaults.
/// </summary>
public static class ResidentEventStatuses
{
    /// <summary>Initial status on publish (Req 41.4): the event is open for registration.</summary>
    public const string Published = "published";

    /// <summary>The event was cancelled by its organiser (Req 41.4). Terminal — no new registrations.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>The event has elapsed and was completed (Req 41.4). Terminal.</summary>
    public const string Completed = "completed";
}
