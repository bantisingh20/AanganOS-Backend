using App.Domain.Common;

namespace App.Domain.Residents;

/// <summary>
/// Associates a <see cref="Resident"/> with a Unit as part of that unit's household (Req 15.2). This
/// is the household relationship realized in Task 12.1 — the move-in/move-out WORKFLOW (Task 12.4)
/// drives the <see cref="StartDate"/>/<see cref="EndDate"/> transitions; here the entity and the
/// basic association are created.
///
/// <para><b>Resident self-scoping (Req 5.4, 66.3).</b> Implements <see cref="IUnitScoped"/> via its
/// non-null <see cref="UnitId"/>, so the DbContext applies the resident self-scope filter: a
/// resident-self-scoped principal sees only household rows whose <c>UnitId</c> matches their own
/// unit. This is how a resident's "my household / my unit" view is bounded to their own data, and
/// how they reach their own <see cref="Resident"/> profile.</para>
///
/// <para><b>Same-community association.</b> The linked Unit must belong to the same community as the
/// resident (enforced in the service before persist, same convention as the HierarchyNode/Unit
/// same-community checks). The row carries its own <see cref="TenantOwnedEntity.CommunityId"/> for
/// tenant isolation.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token, audit columns.</para>
/// </summary>
public class HouseholdMember : TenantOwnedEntity, IUnitScoped
{
    /// <summary>
    /// The unit this resident is associated with (Req 15.2). Non-null in practice (a household
    /// member is always tied to a unit); typed as nullable to satisfy <see cref="IUnitScoped"/>,
    /// which the resident self-scope filter keys on.
    /// </summary>
    public Guid? UnitId { get; set; }

    /// <summary>The resident associated with the unit (Req 15.2). Required.</summary>
    public Guid ResidentId { get; set; }

    /// <summary>Navigation to the owning resident.</summary>
    public Resident? Resident { get; set; }

    /// <summary>
    /// The household relationship to the unit/primary occupant (e.g. "self", "spouse", "child",
    /// "tenant"). Free text in Task 12.1. Required.
    /// </summary>
    public string Relationship { get; set; } = string.Empty;

    /// <summary>
    /// When the association began (Req 15.2). Set on create; the Task 12.4 move workflow manages
    /// later transitions.
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// When the association ended, or null while active. Set by the Task 12.4 move-out workflow;
    /// null here means the resident is currently part of the unit's household.
    /// </summary>
    public DateTime? EndDate { get; set; }
}
