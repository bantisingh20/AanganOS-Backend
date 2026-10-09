using CommunityOS.Application.Querying;
using CommunityOS.Contracts.Envelope;

namespace CommunityOS.Application.Residents;

/// <summary>
/// Resident management (Req 15.1–15.3, 15.5). Create a resident with its type and community
/// (verification status defaults to <c>pending</c>, Req 15.1); associate a resident with a unit via
/// a household member (Req 15.2); add/list emergency contacts (Req 15.3); edit; soft-delete
/// (Req 15.5); get; and a paginated/filter/sort/search list (Req 15.4).
///
/// <para><b>Configurable type (Req 15.1).</b> <c>ResidentType</c> is validated against the editable
/// <c>Resident_Type</c> master-data list resolved at runtime — not a frozen enum.</para>
///
/// <para><b>Same-community association.</b> When a resident is associated with a unit, the unit must
/// belong to the resident's community (same convention as the HierarchyNode/Unit same-community
/// checks) — else a validation error.</para>
///
/// <para><b>Resident self-view (Req 5.4, 66.3).</b> The household link is <c>IUnitScoped</c>, so a
/// resident-self-scoped principal only sees their own household rows; the resident reaches their own
/// profile and contacts through those rows. The <c>Resident</c> row itself has no unit, so its
/// self-view is realized via the household link rather than a per-row unit filter.</para>
///
/// <para>Reads are tenant-scoped (Req 5.2) and use <c>AsNoTracking</c>; mutations are audited
/// (Req 6.1); delete is a soft delete that stays traceable (Req 15.5).</para>
/// </summary>
public interface IResidentService
{
    /// <summary>
    /// Create a resident (Req 15.1). Validates the type against the configurable <c>Resident_Type</c>
    /// list; verification status starts at <c>pending</c> (not caller-supplied). Returns the created
    /// resident.
    /// </summary>
    Task<ResidentDto> CreateAsync(CreateResidentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Edit a resident's profile. A changed type is re-validated. Verification status is not editable
    /// here (Task 12.3). A concurrent update losing the optimistic-concurrency check → CONFLICT
    /// (Req 22.4). Unknown id → not found.
    /// </summary>
    Task<ResidentDto> UpdateAsync(Guid residentId, UpdateResidentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Soft-delete (archive) a resident (Req 15.5). The row is retained and still retrievable with <c>includeArchived</c>.</summary>
    Task DeleteAsync(Guid residentId, CancellationToken cancellationToken = default);

    /// <summary>Fetch a single resident, tenant-scoped. When <paramref name="includeArchived"/> is true a soft-deleted resident is returned (Req 15.5).</summary>
    Task<ResidentDto?> GetAsync(Guid residentId, bool includeArchived = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated/filter(by verificationStatus, residentType)/sort/search list of (non-archived)
    /// residents (Req 15.4, 9.3). Scoped to the caller's authorized communities by the global tenant
    /// query filter; the <c>(CommunityId, VerificationStatus)</c> index backs the verification-queue
    /// filter at scale.
    /// </summary>
    Task<PagedData<ResidentDto>> ListAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Associate a resident with a unit as a household member (Req 15.2). The unit must be in the
    /// resident's community. Unknown resident → not found. Returns the created association.
    /// </summary>
    Task<HouseholdMemberDto> AssociateHouseholdAsync(Guid residentId, AssociateHouseholdRequest request, CancellationToken cancellationToken = default);

    /// <summary>Add an emergency contact to a resident (Req 15.3). Unknown resident → not found.</summary>
    Task<EmergencyContactDto> AddEmergencyContactAsync(Guid residentId, AddEmergencyContactRequest request, CancellationToken cancellationToken = default);

    /// <summary>List a resident's emergency contacts (Req 15.3), tenant-scoped. Unknown resident → not found.</summary>
    Task<IReadOnlyList<EmergencyContactDto>> ListEmergencyContactsAsync(Guid residentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List a unit's ACTIVE household members (open associations, <c>EndDate == null</c>) with each
    /// resident's name + type (Req 15.2). Tenant-scoped by the global filter; <c>AsNoTracking</c>.
    /// Used to show a unit's household and to decide whether the unit already has an owner (the
    /// owner-first household rule). An unknown/foreign unit → empty list.
    /// </summary>
    Task<IReadOnlyList<UnitHouseholdMemberDto>> GetUnitHouseholdAsync(Guid unitId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List a RESIDENT's own ACTIVE unit memberships (open associations, <c>EndDate == null</c>) with
    /// each unit's details (Req 15.2). Tenant/self-scoped by the global filter; <c>AsNoTracking</c>.
    /// Lets a resident's self-service screens resolve their OWN unit(s) instead of the whole community.
    /// Unknown resident → not found.
    /// </summary>
    Task<IReadOnlyList<ResidentHouseholdUnitDto>> GetResidentHouseholdAsync(Guid residentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve the SIGNED-IN principal's own resident row + their own active unit memberships
    /// (<c>GET /residents/me</c>). Matches the resident whose <c>UserId</c> equals the current access
    /// token's user id (the Resident→User link) — a single self-targeted lookup, never a list-and-
    /// filter. A principal with no linked resident row (e.g. a platform superadmin) yields a result
    /// with a null resident and empty units (not a NOT_FOUND), so the caller can fall back to the
    /// login identifier. Reads are tenant/self-scoped by the global filter and use <c>AsNoTracking</c>.
    /// </summary>
    Task<ResidentMeDto> GetMeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a login (User) for a resident and link it (Req 3.1, 15.1). Requires the <c>Create</c>
    /// permission on <c>User</c> + an authorized community (defense-in-depth). Hashes the plaintext
    /// password server-side, creates the user with the identifier (as email or phone), sets the
    /// resident's <c>UserId</c>, and assigns the named role scoped to the resident's community so the
    /// login can use resident features. A resident that already has a login → CONFLICT; a duplicate
    /// identifier → CONFLICT (DB unique). Unknown/foreign resident → not found. Audited. Returns the
    /// new user id + identifier (never the password/hash).
    /// </summary>
    Task<ResidentLoginResult> CreateLoginAsync(Guid residentId, CreateResidentLoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approve a <c>pending</c> resident (Req 17.2, Task 12.3): sets <c>VerificationStatus</c> to
    /// <c>verified</c> and records the approving user and timestamp in the Audit_Log. The resident is
    /// notified of the new status via the Notification_Service in the same transaction (Req 17.4).
    ///
    /// <para><b>Authorization (Req 17.5).</b> Defense-in-depth: the service itself requires the caller
    /// to hold the <c>Approve</c> permission on <c>Resident</c> and that the resident's community is
    /// authorized — a caller without it gets FORBIDDEN with no state change, regardless of whether the
    /// endpoint gate (Task 12.7) ran.</para>
    ///
    /// <para><b>Valid transition.</b> Only <c>pending → verified</c> is allowed; approving an
    /// already verified/rejected resident → CONFLICT (invalid transition), with no notification.</para>
    ///
    /// <para><b>Approve-once under concurrency (Req 22.3).</b> The transition is guarded by the
    /// resident's <c>RowVersion</c> optimistic-concurrency token: two concurrent approvals of the same
    /// pending resident both read the same version, the first <c>SaveChanges</c> bumps it, and the
    /// second trips a row-version mismatch mapped centrally to CONFLICT — so the approval (status
    /// change + its audit + its notification) is recorded exactly once and the loser sees CONFLICT.
    /// This is a DB-level guard, not an application read-then-write gap.</para>
    ///
    /// Unknown/foreign-tenant id → not found. Returns the updated resident.
    /// </summary>
    Task<ResidentDto> ApproveAsync(Guid residentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reject a <c>pending</c> resident (Req 17.3, Task 12.3): sets <c>VerificationStatus</c> to
    /// <c>rejected</c>, stores the <paramref name="request"/>'s reason on the resident, and records the
    /// reason, acting user and timestamp in the Audit_Log. The resident is notified in the same
    /// transaction (Req 17.4). Same <c>Approve</c>-permission + community guard as
    /// <see cref="ApproveAsync"/> (Req 17.5), same valid-transition rule (only <c>pending → rejected</c>;
    /// otherwise CONFLICT), and the same <c>RowVersion</c> concurrency guard (Req 22.3). A blank reason
    /// → validation error. Unknown/foreign-tenant id → not found. Returns the updated resident.
    /// </summary>
    Task<ResidentDto> RejectAsync(Guid residentId, RejectResidentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resubmit a <c>rejected</c> resident for verification (Req 17.1, Task 12.3): sets
    /// <c>VerificationStatus</c> back to <c>pending</c> and clears the prior <c>RejectionReason</c> so
    /// the resident re-enters the verification queue after a correction. Records the transition in the
    /// Audit_Log and notifies the resident in the same transaction (Req 17.4). Requires the
    /// <c>Approve</c> permission + authorized community (Req 17.5). Only <c>rejected → pending</c> is
    /// allowed; resubmitting a pending/verified resident → CONFLICT. Unknown/foreign-tenant id → not
    /// found. Returns the updated resident.
    /// </summary>
    Task<ResidentDto> ResubmitForVerificationAsync(Guid residentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Record a move-in for a resident + unit (Req 18.1, Task 12.4): start/confirm the resident's
    /// household association with the unit (set its <c>StartDate</c> to the move-in date, creating the
    /// association if none exists or reusing the existing open one) AND set the unit's
    /// <c>UnitStatus</c> to <c>occupied</c> — the household change, the unit status flip and the audit
    /// row all commit in one transaction (Req 18.4).
    ///
    /// <para><b>Authorization (Req 18.1).</b> Defense-in-depth: the service itself requires the caller
    /// to hold the <c>Create</c> permission on <c>Resident</c> (the move-in verb) and that the
    /// resident's community is authorized — no permission → FORBIDDEN with no state change, regardless
    /// of the Task 12.7 endpoint gate.</para>
    ///
    /// <para><b>Same-community (existing convention).</b> The unit must belong to the resident's
    /// community, else a validation error — the same check <see cref="AssociateHouseholdAsync"/> uses.</para>
    ///
    /// <para><b>Concurrency.</b> The unit is loaded tracked so its <c>RowVersion</c> guards the status
    /// flip; a concurrent update that loses the row-version check → CONFLICT (central mapping). Unknown
    /// resident/unit → not found. Returns the started/confirmed household association.</para>
    /// </summary>
    Task<HouseholdMemberDto> MoveInAsync(Guid residentId, MoveInRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Record a move-out for a resident + unit (Req 18.2, 18.3, Task 12.4): end the resident's open
    /// household association with the unit (stamp its <c>EndDate</c> to the move-out date) and, when no
    /// open household member remains for the unit afterwards, set the unit's <c>UnitStatus</c> to
    /// <c>vacant</c>. The household change, the possible unit status flip and the audit row all commit
    /// in one transaction (Req 18.4).
    ///
    /// <para><b>Authorization (Req 18.2).</b> Defense-in-depth: the service requires the caller to hold
    /// the <c>Edit</c> permission on <c>Resident</c> (the move-out verb) and an authorized community —
    /// no permission → FORBIDDEN with no state change.</para>
    ///
    /// <para><b>Last-occupant → Vacant, concurrency-safe (Req 18.3).</b> After stamping this member's
    /// <c>EndDate</c>, the remaining OPEN household members for the unit (<c>EndDate == null</c>) are
    /// re-counted inside the same transaction; the unit is flipped to <c>vacant</c> only when that
    /// count is zero. This is a decision made on freshly re-counted state (no read-then-write gap on a
    /// stale count), and the unit's <c>RowVersion</c> guards the flip so two concurrent last move-outs
    /// cannot both silently win: the loser trips a row-version mismatch mapped to CONFLICT and, on
    /// retry, re-counts and leaves the unit <c>vacant</c> exactly when the last occupant has left.</para>
    ///
    /// <para>No open association for this resident+unit → not found. Returns the ended association.</para>
    /// </summary>
    Task<HouseholdMemberDto> MoveOutAsync(Guid residentId, MoveOutRequest request, CancellationToken cancellationToken = default);
}
