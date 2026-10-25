using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Tenancy;

/// <summary>
/// Minimal tenant-owned entity used only to exercise the multi-tenant global query filter,
/// soft-delete exclusion and RowVersion concurrency wiring in tests. Real domain entities
/// (Community, Unit, Resident, ...) arrive in Phase 2 — ponytail: nothing more is built here.
///
/// It also carries an optional <see cref="UnitId"/> so the resident self-scoping hook has
/// something to scope on (Req 5.4) without pulling in Phase 2 types.
/// </summary>
public class SampleTenantRecord : TenantOwnedEntity, IUnitScoped
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional owning unit, used by the resident self-scope predicate (Req 5.4).</summary>
    public Guid? UnitId { get; set; }
}
