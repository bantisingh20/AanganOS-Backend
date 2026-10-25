using UrbanNest.Application.Abstractions;

namespace UrbanNest.IntegrationTests;

/// <summary>Mutable <see cref="ITenantContext"/> for tests — set the scope, then query.</summary>
public sealed class TestTenantContext : ITenantContext
{
    public IReadOnlyCollection<Guid> AuthorizedCommunityIds { get; set; } = [];
    public bool IsPlatformSuperAdmin { get; set; }
    public bool IsResidentSelfScoped { get; set; }
    public Guid? ResidentUnitId { get; set; }
    public Guid? HouseholdId { get; set; }
    public string? UserId { get; set; }
}
