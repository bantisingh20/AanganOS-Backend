using CommunityOS.Application.Abstractions;

namespace CommunityOS.Infrastructure.Tenancy;

/// <summary>
/// Safe default <see cref="ITenantContext"/> for hosts until the request pipeline builds a real
/// one from the authenticated principal (Task 2.2). It grants <b>no</b> community access and is
/// not a super admin, so global query filters return zero tenant-owned rows — fail-closed, never
/// fail-open. Audit stamping records a null acting user. Tests substitute their own
/// <see cref="ITenantContext"/>.
/// </summary>
public sealed class NullTenantContext : ITenantContext
{
    public IReadOnlyCollection<Guid> AuthorizedCommunityIds { get; } = [];
    public bool IsPlatformSuperAdmin => false;
    public bool IsResidentSelfScoped => false;
    public Guid? ResidentUnitId => null;
    public Guid? HouseholdId => null;
    public string? UserId => null;
}
