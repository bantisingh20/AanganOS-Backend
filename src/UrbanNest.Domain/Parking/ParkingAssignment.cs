using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Parking;

/// <summary>
/// One entry in a parking slot's chronological assignment record (Req 40.2, 40.3). Each row records
/// that a <see cref="ParkingSlotId"/> was assigned — permanent, temporary or visitor
/// (<see cref="AssignmentType"/>) — to a <see cref="UnitId"/> (resident) or a <see cref="VisitorId"/>
/// (guest) from <see cref="StartedAtUtc"/> until <see cref="EndedAtUtc"/> (null while this is the
/// <b>current/active</b> assignment).
///
/// <para><b>Append-only history + one active per slot (Req 40.3, the headline invariant).</b> An
/// assignment is never edited in place: assigning or reassigning a slot <i>appends</i> a new row and
/// <i>closes</i> the prior open row by stamping its <see cref="EndedAtUtc"/> — it never deletes or
/// overwrites a prior row, so the full assignment history stays traceable (mirrors
/// <c>OwnershipHistory</c> / the custody register). The close+insert happen in a single transaction
/// so a slot never has two active (<see cref="EndedAtUtc"/> == null) assignments. On SQL Server a
/// filtered unique index on <c>(ParkingSlotId) WHERE EndedAtUtc IS NULL</c> (added in
/// <c>AppDbContext.OnModelCreating</c>, where the provider is known) makes "a given slot has at most
/// one active assignment at a time" a hard DB guarantee — two concurrent assignments of the SAME
/// slot race to that index and exactly ONE wins; the loser's unique violation maps to CONFLICT via
/// <c>UniqueConstraintGuard</c>. This is the one-active-assignment-per-slot approach of
/// <c>CustodyLocation</c> (18.2) / <c>OwnershipHistory</c> (12.2): the DB decides the winner, never a
/// read-then-write check. ponytail: the SQLite test provider has no filtered-index equivalent, so
/// under that provider the invariant rests on the single-transaction close+insert rather than a DB
/// constraint — acceptable for the test path; production (SQL Server) gets the index.</para>
///
/// <para><b>Subject (Req 40.2).</b> Exactly one of <see cref="UnitId"/> (a permanent/temporary
/// resident assignment) or <see cref="VisitorId"/> (a visitor assignment, Task 24.2 guest parking)
/// is set, enforced in the service. The subject and the slot are validated same-community before
/// persist (same convention the ownership/booking registers use).</para>
///
/// <para>Not <see cref="IUnitScoped"/>: the assignment register is an admin/operations record of the
/// slot's history, not resident self-view data (same stance as <c>OwnershipHistory</c>). Derives from
/// <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion concurrency token,
/// audit columns.</para>
/// </summary>
public class ParkingAssignment : TenantOwnedEntity
{
    /// <summary>The parking slot this assignment is for (Req 40.2). Required.</summary>
    public Guid ParkingSlotId { get; set; }

    /// <summary>
    /// The resident unit the slot is assigned to for a permanent/temporary assignment (Req 40.2), or
    /// null for a visitor assignment. Exactly one of this and <see cref="VisitorId"/> is set.
    /// </summary>
    public Guid? UnitId { get; set; }

    /// <summary>
    /// The visitor the slot is assigned to for a visitor assignment (Req 40.2, Task 24.2 guest
    /// parking), or null for a resident assignment. Exactly one of this and <see cref="UnitId"/> is set.
    /// </summary>
    public Guid? VisitorId { get; set; }

    /// <summary>
    /// Configurable assignment-type code resolved against the <c>Parking_Assignment_Type</c>
    /// master-data list (permanent / temporary / visitor) (Req 40.2). Stored as the stable code,
    /// never a frozen enum.
    /// </summary>
    public string AssignmentType { get; set; } = ParkingAssignmentTypes.Permanent;

    /// <summary>When this assignment began (Req 40.2). Set on assign/reassign.</summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>
    /// When this assignment ended, or null while it is the current/active assignment (Req 40.3). Set
    /// (never cleared) when a reassignment supersedes it or the slot is released, closing the prior
    /// entry. The null value is what the filtered unique index keys the one-active invariant on.
    /// </summary>
    public DateTime? EndedAtUtc { get; set; }
}

/// <summary>
/// Well-known default <c>Parking_Assignment_Type</c> codes (Req 40.2). Seeded master-data codes kept
/// as constants so the service sets/compares the assignment type without a magic string. <b>Data</b>,
/// not a behavioural enum — the list is editable per community; these constants only name the seeded
/// defaults.
/// </summary>
public static class ParkingAssignmentTypes
{
    /// <summary>A permanent resident allotment.</summary>
    public const string Permanent = "permanent";

    /// <summary>A temporary resident allotment (time-bounded).</summary>
    public const string Temporary = "temporary";

    /// <summary>A visitor/guest assignment (Task 24.2 guest parking).</summary>
    public const string Visitor = "visitor";
}
