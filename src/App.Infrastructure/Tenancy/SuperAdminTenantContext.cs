using App.Application.Abstractions;

namespace App.Infrastructure.Tenancy;

/// <summary>
/// Cross-tenant <see cref="ITenantContext"/> for background hosts (the Task 7.2 outbox worker and
/// later the SLA/escalation/billing workers). The notification outbox is a tenant-owned entity, so
/// the <see cref="Persistence.AppDbContext"/> global query filter would hide every community's rows
/// from a fail-closed context (<see cref="NullTenantContext"/>). A worker has no HTTP principal to
/// derive a tenant from — it legitimately drains <b>all</b> communities' queues — so it runs under a
/// platform-super-admin context, which short-circuits the tenant predicate (Req 5.5) while
/// soft-delete filtering still applies.
///
/// <para>This is NOT exposed to the API host; it is wired only in the worker's composition root, so
/// request handling never accidentally runs cross-tenant. The acting user is recorded as a stable
/// system identity for audit stamping, not a real end user.</para>
/// </summary>
public sealed class SuperAdminTenantContext : ITenantContext
{
    public IReadOnlyCollection<Guid> AuthorizedCommunityIds { get; } = [];
    public bool IsPlatformSuperAdmin => true;
    public bool IsResidentSelfScoped => false;
    public Guid? ResidentUnitId => null;
    public Guid? HouseholdId => null;
    public string? UserId => "system:notification-worker";
}
