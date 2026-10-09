namespace CommunityOS.Application.Residents;

/// <summary>Read shape for a <c>Resident</c> returned to callers (Req 15.1).</summary>
/// <param name="Id">The resident id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="UserId">Optional linked login user, or null.</param>
/// <param name="Name">Resident's full name.</param>
/// <param name="Email">Optional contact email.</param>
/// <param name="Phone">Optional contact phone.</param>
/// <param name="ResidentType">Configurable resident-type code (Req 15.1).</param>
/// <param name="VerificationStatus">Verification status code; <c>pending</c> on create (Req 15.1).</param>
/// <param name="RejectionReason">Reason recorded on rejection (Task 12.3), else null.</param>
/// <param name="IsArchived">Whether the resident is soft-deleted (Req 15.5).</param>
/// <param name="PhotoFileId">Optional File_Service reference to the resident's photo, else null.</param>
public sealed record ResidentDto(
    Guid Id,
    Guid CommunityId,
    Guid? UserId,
    string Name,
    string? Email,
    string? Phone,
    string ResidentType,
    string VerificationStatus,
    string? RejectionReason,
    bool IsArchived,
    string? PhotoFileId = null);

/// <summary>Read shape for a <c>HouseholdMember</c> association (Req 15.2).</summary>
/// <param name="Id">The association id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="UnitId">The associated unit.</param>
/// <param name="ResidentId">The associated resident.</param>
/// <param name="Relationship">Household relationship.</param>
/// <param name="StartDate">When the association began.</param>
/// <param name="EndDate">When it ended, or null while active.</param>
public sealed record HouseholdMemberDto(
    Guid Id,
    Guid CommunityId,
    Guid UnitId,
    Guid ResidentId,
    string Relationship,
    DateTime StartDate,
    DateTime? EndDate);

/// <summary>
/// Read shape for one active household member of a unit (member association + the resident's name
/// and type), used to show a unit's household and to decide whether the unit already has an owner.
/// </summary>
/// <param name="ResidentId">The resident.</param>
/// <param name="Name">The resident's full name.</param>
/// <param name="ResidentType">The resident's configurable type code (e.g. <c>owner</c>).</param>
/// <param name="Relationship">Household relationship recorded on the association.</param>
/// <param name="StartDate">When the association began.</param>
public sealed record UnitHouseholdMemberDto(
    Guid ResidentId,
    string Name,
    string ResidentType,
    string Relationship,
    DateTime StartDate);

/// <summary>
/// One of a RESIDENT's own active unit memberships (Req 15.2) — the unit the resident belongs to
/// plus their relationship to it. Returned by <c>GET /residents/{id}/household</c> so a resident's
/// self-service screens resolve THEIR OWN unit(s) instead of listing the whole community.
/// </summary>
/// <param name="UnitId">The unit the resident belongs to.</param>
/// <param name="UnitNumber">The unit's number (e.g. C-409).</param>
/// <param name="UnitType">Configurable unit-type code.</param>
/// <param name="UnitStatus">Configurable unit-status code.</param>
/// <param name="Floor">Optional floor label.</param>
/// <param name="CommunityId">Owning community (for uploads/dropdowns scoped to it).</param>
/// <param name="Relationship">The resident's household relationship to the unit (owner / tenant / ...).</param>
/// <param name="StartDate">When the association began.</param>
public sealed record ResidentHouseholdUnitDto(
    Guid UnitId,
    string UnitNumber,
    string UnitType,
    string UnitStatus,
    string? Floor,
    Guid CommunityId,
    string Relationship,
    DateTime StartDate);

/// <summary>
/// "Who am I" shape for the signed-in principal (<c>GET /residents/me</c>): the ONE resident row
/// linked to the access token's user (<c>Resident.UserId == token sub</c>) plus that resident's own
/// active unit memberships. Lets the app resolve the signed-in person's name + units in a single
/// targeted call instead of listing residents and filtering by userId client-side. When the
/// principal has no linked resident row (e.g. a platform superadmin) <paramref name="Resident"/> is
/// null and <paramref name="Units"/> is empty — NOT a 404 — so callers fall back cleanly.
/// </summary>
/// <param name="Resident">The signed-in person's own resident row, or null when none is linked.</param>
/// <param name="Units">The resident's own active unit memberships (empty when none / no resident).</param>
public sealed record ResidentMeDto(
    ResidentDto? Resident,
    IReadOnlyList<ResidentHouseholdUnitDto> Units);

/// <summary>Read shape for an <c>EmergencyContact</c> (Req 15.3).</summary>
/// <param name="Id">The contact id.</param>
/// <param name="ResidentId">The owning resident.</param>
/// <param name="Name">Contact's name.</param>
/// <param name="Phone">Contact's phone.</param>
/// <param name="Relationship">Relationship to the resident.</param>
public sealed record EmergencyContactDto(
    Guid Id,
    Guid ResidentId,
    string Name,
    string Phone,
    string Relationship);

/// <summary>
/// Create a resident (Req 15.1). <paramref name="ResidentType"/> is validated against the
/// configurable <c>Resident_Type</c> list. <c>VerificationStatus</c> is NOT accepted from the
/// caller — it always starts at <c>pending</c> (the Task 12.3 workflow owns transitions).
/// </summary>
/// <param name="CommunityId">Owning community. Required.</param>
/// <param name="UserId">Optional login user to link; null if none.</param>
/// <param name="Name">Resident's full name. Required.</param>
/// <param name="Email">Optional contact email.</param>
/// <param name="Phone">Optional contact phone.</param>
/// <param name="ResidentType">Resident-type code from the Resident_Type list. Required.</param>
/// <param name="PhotoFileId">Optional File_Service photo reference; null when none.</param>
public sealed record CreateResidentRequest(
    Guid CommunityId,
    Guid? UserId,
    string Name,
    string? Email,
    string? Phone,
    string ResidentType,
    string? PhotoFileId = null);

/// <summary>
/// Edit a resident's profile fields. Null string fields are left unchanged; a changed type is
/// re-validated. Verification status is deliberately not editable here (Task 12.3).
/// </summary>
/// <param name="Name">New name, or null to leave unchanged.</param>
/// <param name="ResidentType">New resident-type code (validated), or null to leave unchanged.</param>
/// <param name="Email">New email; see <see cref="UpdateEmail"/>.</param>
/// <param name="UpdateEmail">When true, <see cref="Email"/> is applied (allowing null = clear); when false untouched.</param>
/// <param name="Phone">New phone; see <see cref="UpdatePhone"/>.</param>
/// <param name="UpdatePhone">When true, <see cref="Phone"/> is applied (allowing null = clear); when false untouched.</param>
/// <param name="UserId">New linked user; see <see cref="UpdateUserId"/>.</param>
/// <param name="UpdateUserId">When true, <see cref="UserId"/> is applied (allowing null = unlink); when false untouched.</param>
/// <param name="PhotoFileId">New photo reference; see <see cref="UpdatePhotoFileId"/>.</param>
/// <param name="UpdatePhotoFileId">When true, <see cref="PhotoFileId"/> is applied (allowing null = clear); when false untouched.</param>
public sealed record UpdateResidentRequest(
    string? Name,
    string? ResidentType,
    string? Email,
    bool UpdateEmail,
    string? Phone,
    bool UpdatePhone,
    Guid? UserId,
    bool UpdateUserId,
    string? PhotoFileId = null,
    bool UpdatePhotoFileId = false);

/// <summary>
/// Associate a resident with a unit as a household member (Req 15.2). The unit must belong to the
/// same community as the resident. When <paramref name="StartDate"/> is null the current UTC time is
/// used; the Task 12.4 move workflow manages later start/end transitions.
/// </summary>
/// <param name="UnitId">The unit to associate. Required; must be in the resident's community.</param>
/// <param name="Relationship">Household relationship (e.g. "self", "spouse"). Required.</param>
/// <param name="StartDate">Optional association start; defaults to now when null.</param>
public sealed record AssociateHouseholdRequest(
    Guid UnitId,
    string Relationship,
    DateTime? StartDate);

/// <summary>Add an emergency contact to a resident (Req 15.3).</summary>
/// <param name="Name">Contact's name. Required.</param>
/// <param name="Phone">Contact's phone. Required.</param>
/// <param name="Relationship">Relationship to the resident. Required.</param>
public sealed record AddEmergencyContactRequest(
    string Name,
    string Phone,
    string Relationship);

/// <summary>
/// Create a login (User account) for a resident and link it (Req 3.1, 15.1). The password is sent in
/// plaintext over HTTPS and hashed server-side (the admin never computes a hash). On success a
/// <c>User</c> is created with the identifier, the resident's <c>UserId</c> is set, and the named
/// role (<paramref name="RoleName"/>, default "Resident") is assigned scoped to the resident's
/// community so the login can use resident features. Rejected if the resident already has a login.
/// </summary>
/// <param name="Identifier">The login identifier — an email or phone. Required.</param>
/// <param name="Password">The initial plaintext password (hashed server-side). Required.</param>
/// <param name="RoleName">Role to assign (scoped to the community); null/empty = the default "Resident" role.</param>
public sealed record CreateResidentLoginRequest(
    string Identifier,
    string Password,
    string? RoleName = null);

/// <summary>Result of creating a resident login — the new user id + the identifier (never the password/hash).</summary>
/// <param name="UserId">The created login user's id.</param>
/// <param name="Identifier">The login identifier (email/phone).</param>
/// <param name="RoleAssigned">The role name assigned, or null if no matching role was found to assign.</param>
public sealed record ResidentLoginResult(
    Guid UserId,
    string Identifier,
    string? RoleAssigned);

/// <summary>
/// Reject a pending resident (Req 17.3, Task 12.3). The <paramref name="Reason"/> is required and is
/// stored on the resident's <c>RejectionReason</c> and recorded in the Audit_Log alongside the acting
/// user and timestamp. Approve and resubmit need no body beyond the resident id, so they have no
/// request record.
/// </summary>
/// <param name="Reason">Why the resident was rejected. Required, non-blank.</param>
public sealed record RejectResidentRequest(string Reason);

/// <summary>
/// Record a move-in for a resident + unit (Req 18.1, Task 12.4). The unit must belong to the
/// resident's community. The resident's household association with the unit is started (its
/// <c>StartDate</c> is set/confirmed to <paramref name="MoveInDate"/>, creating the association if
/// none exists), and the unit's status is set to <c>occupied</c> — both in one transaction.
/// </summary>
/// <param name="UnitId">The unit the resident moves into. Required; must be in the resident's community.</param>
/// <param name="MoveInDate">Optional move-in date; defaults to now (UTC) when null.</param>
/// <param name="Relationship">
/// Household relationship to record when the association must be created (e.g. "self", "tenant").
/// Required only when there is no existing association for this resident+unit; ignored when one
/// already exists (its StartDate is confirmed instead).
/// </param>
public sealed record MoveInRequest(
    Guid UnitId,
    DateTime? MoveInDate,
    string? Relationship);

/// <summary>
/// Record a move-out for a resident + unit (Req 18.2, 18.3, Task 12.4). Ends the resident's open
/// household association with the unit (stamps its <c>EndDate</c> to <paramref name="MoveOutDate"/>);
/// when no open household member remains for the unit afterwards, the unit's status is set to
/// <c>vacant</c> — all in one transaction.
/// </summary>
/// <param name="UnitId">The unit the resident moves out of. Required.</param>
/// <param name="MoveOutDate">Optional move-out date; defaults to now (UTC) when null.</param>
public sealed record MoveOutRequest(
    Guid UnitId,
    DateTime? MoveOutDate);
