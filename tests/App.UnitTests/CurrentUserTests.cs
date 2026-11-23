using System.Security.Claims;
using App.Application.Abstractions.Security;

namespace App.UnitTests;

/// <summary>Token claims -> <see cref="CurrentUser"/> mapping (replaces ClaimsTenantContextMapperTests).</summary>
public sealed class CurrentUserTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Bearer"));

    [Fact]
    public void Maps_all_login_details_from_claims()
    {
        var userId = Guid.NewGuid();
        var communityA = Guid.NewGuid();
        var communityB = Guid.NewGuid();
        var unit = Guid.NewGuid();
        var org = Guid.NewGuid();
        var residentId = Guid.NewGuid();

        var user = CurrentUser.From(Principal(
            new Claim(ClaimNames.UserId, userId.ToString()),
            new Claim(ClaimNames.CommunityId, communityA.ToString()),
            new Claim(ClaimNames.CommunityId, communityB.ToString()),
            new Claim(ClaimNames.CommunityId, communityA.ToString()), // duplicate ignored
            new Claim(ClaimNames.UnitId, unit.ToString()),
            new Claim(ClaimNames.OrganizationId, org.ToString()),
            new Claim(ClaimNames.ResidentId, residentId.ToString()),
            new Claim(ClaimNames.ResidentType, "tenant"),
            new Claim(ClaimNames.IsResident, "true"),
            new Claim(ClaimTypes.Role, "Resident")));

        Assert.True(user.IsAuthenticated);
        Assert.Equal(userId, user.UserId);
        Assert.Equal(userId.ToString(), user.Actor);
        Assert.Equal(communityA, user.CommunityId);              // first = the single default
        Assert.Equal([communityA], user.AccessibleCommunityIds);  // not an org admin -> own community only
        Assert.Equal(unit, user.UnitId);
        Assert.Equal(org, user.OrganizationId);
        Assert.Equal(residentId, user.ResidentId);
        Assert.True(user.IsResidentType("TENANT"));
        Assert.True(user.IsInRole("resident"));
        Assert.Equal("Resident", user.Role);
        Assert.True(user.IsResident);
        Assert.False(user.IsSuperAdmin);
        Assert.True(user.CanAccessCommunity(communityA));
        Assert.False(user.CanAccessCommunity(communityB));
    }

    [Fact]
    public void Anonymous_or_null_principal_is_fail_closed()
    {
        foreach (var user in new[] { CurrentUser.From(null), CurrentUser.From(new ClaimsPrincipal(new ClaimsIdentity())), CurrentUser.Anonymous })
        {
            Assert.False(user.IsAuthenticated);
            Assert.Null(user.UserId);
            Assert.Empty(user.AccessibleCommunityIds);
            Assert.Null(user.CommunityId);
            Assert.False(user.IsSuperAdmin);
            Assert.Throws<UnauthorizedAccessException>(() => user.RequireUserId());
        }
    }

    [Fact]
    public void Reads_claims_lazily_so_a_principal_attached_after_creation_is_seen()
    {
        // Mirrors the API: the object is created during JWT validation, before HttpContext.User is set.
        ClaimsPrincipal? current = null;
        var user = new CurrentUser(() => current);

        Assert.False(user.IsAuthenticated);

        var userId = Guid.NewGuid();
        current = Principal(new Claim(ClaimNames.UserId, userId.ToString()));

        Assert.Equal(userId, user.UserId);
    }

    [Fact]
    public void Malformed_community_ids_are_skipped()
    {
        var good = Guid.NewGuid();
        var user = CurrentUser.From(Principal(
            new Claim(ClaimNames.UserId, Guid.NewGuid().ToString()),
            new Claim(ClaimNames.CommunityId, "not-a-guid"),
            new Claim(ClaimNames.CommunityId, good.ToString())));

        Assert.Equal(good, user.CommunityId);
    }

    [Fact]
    public void Organization_admin_sees_all_communities_of_the_organization_by_role()
    {
        // Apna Ghar (organization) has wing A and wing B (communities). Default community = wing A.
        var wingA = Guid.NewGuid();
        var wingB = Guid.NewGuid();
        var orgAdmin = new CurrentUser
        {
            UserId = Guid.NewGuid(),
            CommunityId = wingA,
            OrganizationId = Guid.NewGuid(),
            Roles = [CurrentUser.OrganizationAdminRole],
        };

        // Before the request loads the organization's communities: only the default.
        Assert.Equal([wingA], orgAdmin.AccessibleCommunityIds);

        orgAdmin.UseOrganizationCommunities([wingA, wingB]); // AuthSetup does this per request

        Assert.True(orgAdmin.IsOrganizationAdmin);
        Assert.Equal(wingA, orgAdmin.CommunityId); // still ONE default community
        Assert.True(orgAdmin.CanAccessCommunity(wingB));

        // A community admin with the same data does NOT get wing B.
        var communityAdmin = new CurrentUser { UserId = Guid.NewGuid(), CommunityId = wingA, Roles = ["CommunityAdmin"] };
        communityAdmin.UseOrganizationCommunities([wingA, wingB]);
        Assert.False(communityAdmin.CanAccessCommunity(wingB));
    }

    [Fact]
    public void System_user_is_cross_community_and_stamped_as_system()
    {
        var user = CurrentUser.System;

        Assert.True(user.IsSuperAdmin);
        Assert.Equal("system:worker", user.Actor);
        Assert.True(user.CanAccessCommunity(Guid.NewGuid()));
    }
}
