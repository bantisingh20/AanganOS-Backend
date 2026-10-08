using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Gate;

/// <summary>
/// A rostered guard shift associating a guard, a gate and a shift period (Req 26.4). Carries its
/// owning <see cref="TenantOwnedEntity.CommunityId"/>.
///
/// <para><b>Guard profiles are Users, not a duplicate entity.</b> A "guard" is a Phase 1
/// <c>User</c> holding the Guard role; this shift only REFERENCES that user by
/// <see cref="GuardUserId"/> (Req 26.2 — don't duplicate the guard profile). The gate is referenced
/// by an optional <see cref="GateId"/> (the Gate master is a later task) plus a free-text
/// <see cref="GateLabel"/> in the meantime.</para>
///
/// <para><b>Not <c>IUnitScoped</c>.</b> A roster is a community-wide operations register, not
/// resident self-view data — scoped by community (the tenant filter), never pinned to one unit.</para>
///
/// <para><b>Configurable status (steering: no hardcoding).</b> <see cref="Status"/> resolves against
/// the editable <c>Guard_Shift_Status</c> list (scheduled / active / completed / cancelled) — not a
/// frozen enum. The config declares a <c>(CommunityId, ShiftStartUtc)</c> index so the roster list
/// (16.4) is a single indexed, chronologically ordered read.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token, audit columns.</para>
/// </summary>
public class GuardShift : TenantOwnedEntity
{
    /// <summary>The guard this shift is rostered for — a Phase 1 <c>User</c> with the Guard role (Req 26.4). Required.</summary>
    public Guid GuardUserId { get; set; }

    /// <summary>Optional gate id this shift covers (the Gate master is a later task), or null.</summary>
    public Guid? GateId { get; set; }

    /// <summary>Optional free-text gate label/identifier captured on the roster (until the Gate master lands).</summary>
    public string? GateLabel { get; set; }

    /// <summary>Shift start (Req 26.4).</summary>
    public DateTime ShiftStartUtc { get; set; }

    /// <summary>Shift end (Req 26.4). Must be after <see cref="ShiftStartUtc"/>.</summary>
    public DateTime ShiftEndUtc { get; set; }

    /// <summary>
    /// Configurable status code resolved against the <c>Guard_Shift_Status</c> master-data list
    /// (scheduled / active / completed / cancelled, Req 26.4). Starts <c>scheduled</c>. Stored as the
    /// stable code, never a frozen enum.
    /// </summary>
    public string Status { get; set; } = GuardShiftStatuses.Scheduled;
}

/// <summary>
/// Well-known default <c>Guard_Shift_Status</c> codes (Req 26.4). Seeded master-data codes kept as
/// constants. <b>Data</b>, not a behavioural enum — the list is editable.
/// </summary>
public static class GuardShiftStatuses
{
    /// <summary>Initial status on roster create (Req 26.4): the shift is planned.</summary>
    public const string Scheduled = "scheduled";

    /// <summary>The shift is currently on duty.</summary>
    public const string Active = "active";

    /// <summary>The shift has finished.</summary>
    public const string Completed = "completed";

    /// <summary>The shift was cancelled before/while scheduled.</summary>
    public const string Cancelled = "cancelled";
}
