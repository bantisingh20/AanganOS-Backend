using CommunityOS.Application.Abstractions;
using CommunityOS.Application.Abstractions.Security;

namespace CommunityOS.Api.Security;

/// <summary>
/// Request-scoped <see cref="ITenantContext"/> that derives the tenant/self-scope inputs from the
/// current request's authenticated principal via <see cref="ClaimsTenantContextMapper"/>. Registered
/// in the API host to replace the fail-closed <see cref="CommunityOS.Infrastructure.Tenancy.NullTenantContext"/>
/// so the Phase 1.1 global query filters enforce per-user isolation end-to-end (Req 5.1–5.5).
///
/// Resolution is lazy and cached per request: the first property access reads
/// <see cref="IHttpContextAccessor.HttpContext"/>'s <c>User</c> and maps it once. Unauthenticated
/// requests (null/anonymous principal, or no HttpContext at all) map to the empty, fail-closed
/// snapshot — the data layer then returns zero tenant-owned rows (never fail-open).
///
/// Stateless and scoped: no shared mutable state across requests, safe under high concurrency.
/// </summary>
public sealed class HttpContextTenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private ITenantContext? _resolved;

    private ITenantContext Resolved =>
        _resolved ??= ClaimsTenantContextMapper.Map(_httpContextAccessor.HttpContext?.User);

    public IReadOnlyCollection<Guid> AuthorizedCommunityIds => Resolved.AuthorizedCommunityIds;
    public bool IsPlatformSuperAdmin => Resolved.IsPlatformSuperAdmin;
    public bool IsResidentSelfScoped => Resolved.IsResidentSelfScoped;
    public Guid? ResidentUnitId => Resolved.ResidentUnitId;
    public Guid? HouseholdId => Resolved.HouseholdId;
    public string? UserId => Resolved.UserId;
}
