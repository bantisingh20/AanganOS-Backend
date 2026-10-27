namespace App.Application.Residents;

/// <summary>
/// A unit's ownership and tenancy history (Req 16.1–16.4). A unit-centric service kept separate from
/// <see cref="IResidentService"/> because the operations are keyed on the Unit — assign/transfer the
/// owner of a unit, begin/end a tenancy on a unit, and retrieve a unit's chronological history — and
/// map to the design's <c>GET /units/{id}/ownership-history | /tenancy-history</c> endpoints (the
/// HTTP surface is Task 12.7; this task builds the service).
///
/// <para><b>Append-only, retain prior (Req 16.1–16.3).</b> Every mutation <i>appends</i> a new row or
/// <i>closes</i> an existing one by stamping its <c>EndDate</c>; a prior entry is never physically
/// removed or overwritten, so the full history stays traceable.</para>
///
/// <para><b>At-most-one-open-owner (Req 16.3 invariant).</b> Assigning a new owner closes the prior
/// open owner row and inserts the new one in a <b>single transaction</b>, so a unit never has two
/// open (current) owners even under concurrent assignments — on SQL Server also backed by a filtered
/// unique index on <c>(UnitId) WHERE EndDate IS NULL</c>. Tenancy has no such constraint: a unit may
/// have multiple concurrent open tenancies (co-tenants).</para>
///
/// <para><b>Same-community (Req 16.1, 16.2).</b> The owner/tenant resident and the unit must belong
/// to the same community (same convention as the HouseholdMember/Unit checks) — else a validation
/// error. Reads are tenant-scoped (Req 5.2) and use <c>AsNoTracking</c>; all mutations are audited
/// (Req 6.1).</para>
/// </summary>
public interface IUnitHistoryService
{
    /// <summary>
    /// Assign or transfer ownership of a unit (Req 16.1, 16.3). Appends a new open ownership entry
    /// for the resident and closes any existing open owner of the same unit (stamping its EndDate) in
    /// one transaction — the prior entry is retained. Owner and unit must be in the same community;
    /// unknown unit/resident → not found. A concurrent assignment losing the open-owner race →
    /// CONFLICT. Returns the created entry.
    /// </summary>
    Task<OwnershipHistoryDto> AssignOwnershipAsync(AssignOwnershipRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Begin a tenancy on a unit (Req 16.2, 16.3). Appends a new open tenancy entry. Tenant and unit
    /// must be in the same community; unknown unit/resident → not found. Returns the created entry.
    /// </summary>
    Task<TenancyHistoryDto> BeginTenancyAsync(BeginTenancyRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// End an active tenancy (Req 16.2, 16.3). Closes the resident's open tenancy row for the unit by
    /// stamping its EndDate — the row is retained. No open tenancy for that resident/unit → not found.
    /// A concurrent end losing the optimistic-concurrency check → CONFLICT. Returns the closed entry.
    /// </summary>
    Task<TenancyHistoryDto> EndTenancyAsync(EndTenancyRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve a unit's full ownership + tenancy history in chronological order (Req 16.4),
    /// tenant-scoped. Unknown/foreign-tenant unit → not found. Both lists are ordered oldest-first by
    /// start date; a null EndDate marks the current/active association.
    /// </summary>
    Task<UnitHistoryDto> GetUnitHistoryAsync(Guid unitId, CancellationToken cancellationToken = default);
}
