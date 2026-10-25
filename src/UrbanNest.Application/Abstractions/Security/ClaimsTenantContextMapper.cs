using System.Security.Claims;

namespace UrbanNest.Application.Abstractions.Security;

/// <summary>
/// Pure mapping from a validated <see cref="ClaimsPrincipal"/> to an <see cref="ITenantContext"/>
/// snapshot, using the <see cref="CommunityClaimTypes"/> contract. This is the single seam where
/// token claims become the tenant isolation inputs the data layer trusts (Req 5.1). Kept pure and
/// framework-free so it is unit-testable and reused by whatever hosts the principal (the API
/// request-scoped context today; background jobs replaying a principal later).
///
/// Security: authorized communities come exclusively from signed claims. A principal that is not
/// authenticated maps to the fail-closed empty context (no communities, not super admin), so the
/// global query filters return zero tenant-owned rows.
/// </summary>
public static class ClaimsTenantContextMapper
{
    /// <summary>Builds a tenant-context snapshot from the principal's claims.</summary>
    public static TenantContextSnapshot Map(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not { IsAuthenticated: true })
        {
            return TenantContextSnapshot.Empty;
        }

        var communityIds = principal.FindAll(CommunityClaimTypes.CommunityId)
            .Select(c => Guid.TryParse(c.Value, out var id) ? id : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

        return new TenantContextSnapshot
        {
            UserId = principal.FindFirst(CommunityClaimTypes.UserId)?.Value
                     ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            AuthorizedCommunityIds = communityIds,
            IsPlatformSuperAdmin = ReadBool(principal, CommunityClaimTypes.IsPlatformSuperAdmin),
            IsResidentSelfScoped = ReadBool(principal, CommunityClaimTypes.IsResidentSelfScoped),
            ResidentUnitId = ReadGuid(principal, CommunityClaimTypes.ResidentUnitId),
            HouseholdId = ReadGuid(principal, CommunityClaimTypes.HouseholdId),
        };
    }

    private static bool ReadBool(ClaimsPrincipal principal, string claimType)
        => bool.TryParse(principal.FindFirst(claimType)?.Value, out var value) && value;

    private static Guid? ReadGuid(ClaimsPrincipal principal, string claimType)
        => Guid.TryParse(principal.FindFirst(claimType)?.Value, out var value) ? value : null;
}

/// <summary>
/// An immutable <see cref="ITenantContext"/> value produced by <see cref="ClaimsTenantContextMapper"/>.
/// </summary>
public sealed class TenantContextSnapshot : ITenantContext
{
    /// <summary>The fail-closed snapshot: no community access, not super admin, no user.</summary>
    public static TenantContextSnapshot Empty { get; } = new();

    public IReadOnlyCollection<Guid> AuthorizedCommunityIds { get; init; } = [];
    public bool IsPlatformSuperAdmin { get; init; }
    public bool IsResidentSelfScoped { get; init; }
    public Guid? ResidentUnitId { get; init; }
    public Guid? HouseholdId { get; init; }
    public string? UserId { get; init; }
}
