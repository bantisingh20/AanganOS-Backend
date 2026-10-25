using System.Security.Claims;
using UrbanNest.Application.Abstractions.Security;

namespace UrbanNest.UnitTests;

/// <summary>
/// Runnable check for Task 2.2's claim → tenant-context contract: the mapper derives the tenant
/// isolation inputs the data layer trusts purely from the validated principal's claims (Req 5.1),
/// and an unauthenticated principal maps to the fail-closed empty context (no community access).
/// </summary>
public sealed class ClaimsTenantContextMapperTests
{
    private static ClaimsPrincipal Authenticated(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "TestAuth"));

    [Fact]
    public void Authenticated_principal_maps_authorized_communities_and_user_from_claims()
    {
        var communityA = Guid.NewGuid();
        var communityB = Guid.NewGuid();

        var principal = Authenticated(
            new Claim(CommunityClaimTypes.UserId, "user-42"),
            new Claim(CommunityClaimTypes.CommunityId, communityA.ToString()),
            new Claim(CommunityClaimTypes.CommunityId, communityB.ToString()),
            new Claim(CommunityClaimTypes.CommunityId, communityA.ToString())); // duplicate ignored

        var ctx = ClaimsTenantContextMapper.Map(principal);

        Assert.Equal("user-42", ctx.UserId);
        Assert.Contains(communityA, ctx.AuthorizedCommunityIds);
        Assert.Contains(communityB, ctx.AuthorizedCommunityIds);
        Assert.Equal(2, ctx.AuthorizedCommunityIds.Count); // duplicate collapsed
        Assert.False(ctx.IsPlatformSuperAdmin);
        Assert.False(ctx.IsResidentSelfScoped);
    }

    [Fact]
    public void Super_admin_and_resident_self_scope_flags_are_read_from_claims()
    {
        var unit = Guid.NewGuid();
        var household = Guid.NewGuid();

        var principal = Authenticated(
            new Claim(CommunityClaimTypes.UserId, "resident-1"),
            new Claim(CommunityClaimTypes.IsPlatformSuperAdmin, "true"),
            new Claim(CommunityClaimTypes.IsResidentSelfScoped, "true"),
            new Claim(CommunityClaimTypes.ResidentUnitId, unit.ToString()),
            new Claim(CommunityClaimTypes.HouseholdId, household.ToString()));

        var ctx = ClaimsTenantContextMapper.Map(principal);

        Assert.True(ctx.IsPlatformSuperAdmin);
        Assert.True(ctx.IsResidentSelfScoped);
        Assert.Equal(unit, ctx.ResidentUnitId);
        Assert.Equal(household, ctx.HouseholdId);
    }

    [Fact]
    public void Unauthenticated_principal_is_fail_closed()
    {
        // Anonymous identity (no authenticationType) → not authenticated.
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var ctx = ClaimsTenantContextMapper.Map(anonymous);

        Assert.Empty(ctx.AuthorizedCommunityIds);
        Assert.False(ctx.IsPlatformSuperAdmin);
        Assert.Null(ctx.UserId);
    }

    [Fact]
    public void Null_principal_is_fail_closed()
    {
        var ctx = ClaimsTenantContextMapper.Map(null);

        Assert.Empty(ctx.AuthorizedCommunityIds);
        Assert.False(ctx.IsPlatformSuperAdmin);
    }

    [Fact]
    public void Malformed_community_claims_are_dropped_not_trusted()
    {
        var good = Guid.NewGuid();
        var principal = Authenticated(
            new Claim(CommunityClaimTypes.UserId, "user-1"),
            new Claim(CommunityClaimTypes.CommunityId, "not-a-guid"),
            new Claim(CommunityClaimTypes.CommunityId, good.ToString()));

        var ctx = ClaimsTenantContextMapper.Map(principal);

        Assert.Equal([good], ctx.AuthorizedCommunityIds.ToArray());
    }
}
