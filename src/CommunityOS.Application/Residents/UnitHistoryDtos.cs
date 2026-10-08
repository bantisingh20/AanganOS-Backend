namespace CommunityOS.Application.Residents;

/// <summary>Read shape for one <c>OwnershipHistory</c> entry (Req 16.1, 16.4).</summary>
/// <param name="Id">The entry id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="UnitId">The unit this ownership entry is for.</param>
/// <param name="ResidentId">The owner resident.</param>
/// <param name="StartDate">When this ownership began.</param>
/// <param name="EndDate">When it ended, or null while this is the current owner.</param>
public sealed record OwnershipHistoryDto(
    Guid Id,
    Guid CommunityId,
    Guid UnitId,
    Guid ResidentId,
    DateTime StartDate,
    DateTime? EndDate);

/// <summary>Read shape for one <c>TenancyHistory</c> entry (Req 16.2, 16.4).</summary>
/// <param name="Id">The entry id.</param>
/// <param name="CommunityId">Owning community.</param>
/// <param name="UnitId">The unit this tenancy entry is for.</param>
/// <param name="ResidentId">The tenant resident.</param>
/// <param name="StartDate">When this tenancy began.</param>
/// <param name="EndDate">When it ended, or null while active.</param>
public sealed record TenancyHistoryDto(
    Guid Id,
    Guid CommunityId,
    Guid UnitId,
    Guid ResidentId,
    DateTime StartDate,
    DateTime? EndDate);

/// <summary>
/// A unit's full ownership + tenancy history in chronological order (Req 16.4). Both lists are
/// ordered by <c>StartDate</c> then <c>Id</c> (stable), oldest first; a null <c>EndDate</c> marks a
/// current/active association.
/// </summary>
/// <param name="UnitId">The unit the history belongs to.</param>
/// <param name="Ownership">Ownership entries, chronological.</param>
/// <param name="Tenancy">Tenancy entries, chronological.</param>
public sealed record UnitHistoryDto(
    Guid UnitId,
    IReadOnlyList<OwnershipHistoryDto> Ownership,
    IReadOnlyList<TenancyHistoryDto> Tenancy);

/// <summary>
/// Assign or transfer ownership of a unit to a resident (Req 16.1, 16.3). Appends a new open
/// <c>OwnershipHistory</c> row; any existing open owner of the same unit is closed (its
/// <c>EndDate</c> stamped) in the same transaction — the prior row is retained, never removed. The
/// owner resident and the unit must be in the same community.
/// </summary>
/// <param name="UnitId">The unit whose ownership is being assigned/transferred. Required.</param>
/// <param name="ResidentId">The new owner resident. Required; must be in the unit's community.</param>
/// <param name="StartDate">When the new ownership begins; defaults to now when null. The prior open owner is closed at this instant.</param>
public sealed record AssignOwnershipRequest(
    Guid UnitId,
    Guid ResidentId,
    DateTime? StartDate);

/// <summary>
/// Begin a tenancy on a unit for a resident (Req 16.2, 16.3). Appends a new open
/// <c>TenancyHistory</c> row. The tenant resident and the unit must be in the same community. Prior
/// entries are retained.
/// </summary>
/// <param name="UnitId">The unit being rented. Required.</param>
/// <param name="ResidentId">The tenant resident. Required; must be in the unit's community.</param>
/// <param name="StartDate">When the tenancy begins; defaults to now when null.</param>
public sealed record BeginTenancyRequest(
    Guid UnitId,
    Guid ResidentId,
    DateTime? StartDate);

/// <summary>
/// End an active tenancy of a resident on a unit (Req 16.2, 16.3). Closes that resident's open
/// <c>TenancyHistory</c> row for the unit by stamping its <c>EndDate</c> — the row is retained, never
/// removed. Fails if there is no open tenancy to end for that resident/unit.
/// </summary>
/// <param name="UnitId">The unit whose tenancy is ending. Required.</param>
/// <param name="ResidentId">The tenant resident whose tenancy is ending. Required.</param>
/// <param name="EndDate">When the tenancy ends; defaults to now when null. Must not precede the start date.</param>
public sealed record EndTenancyRequest(
    Guid UnitId,
    Guid ResidentId,
    DateTime? EndDate);
