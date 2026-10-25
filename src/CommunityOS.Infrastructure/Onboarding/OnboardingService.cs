using CommunityOS.Application.Abstractions;
using CommunityOS.Application.Auth;
using CommunityOS.Application.Community;
using CommunityOS.Application.Exceptions;
using CommunityOS.Application.Identity;
using CommunityOS.Application.Onboarding;
using CommunityOS.Domain.Identity;
using CommunityOS.Infrastructure.Persistence;
using CommunityOS.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CommunityOS.Infrastructure.Onboarding;

/// <summary>
/// Public self-service onboarding. Creating an organization/community is a PLATFORM action, but the
/// caller is anonymous (no HTTP principal → the request-scoped tenant context is fail-closed). So the
/// provisioning writes run inside a short-lived isolated DI scope with a platform
/// <see cref="SuperAdminTenantContext"/> (same pattern the dev seeder uses) — this never touches the
/// request pipeline wiring, so normal request handling can't accidentally run cross-tenant.
///
/// <para>The new admin is assigned the "CommunityAdmin" role SCOPED to the new community
/// (CommunityId = the new community's id), so their JWT carries only that community and the global
/// tenant filter isolates them to it. They are deliberately NOT a platform super-admin.</para>
///
/// <para>ponytail: builds a one-off infrastructure provider per onboarding call. Onboarding is rare
/// (a human creating a community), so the allocation cost is acceptable; the alternative (a
/// per-request swappable tenant context) would complicate the whole pipeline. Upgrade path: a
/// pooled privileged scope if onboarding ever becomes high-volume.</para>
/// </summary>
public sealed class OnboardingService(
    IConfiguration configuration,
    IPasswordHasher passwordHasher) : IOnboardingService
{
    private const string CommunityAdminRoleName = "CommunityAdmin";

    public async Task<OnboardingResult> CreateCommunityAsync(
        CreateCommunityOnboardingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // --- Trust-boundary validation (anonymous input). ---
        var orgName = (request.OrganizationName ?? string.Empty).Trim();
        var communityName = (request.CommunityName ?? string.Empty).Trim();
        var communityType = (request.CommunityType ?? string.Empty).Trim();
        var adminName = (request.AdminName ?? string.Empty).Trim();
        var email = string.IsNullOrWhiteSpace(request.AdminEmail) ? null : request.AdminEmail.Trim();
        var phone = string.IsNullOrWhiteSpace(request.AdminPhone) ? null : request.AdminPhone.Trim();

        // Organization name is now explicit input (falls back to the community name if omitted).
        if (orgName.Length is 0) orgName = communityName;
        if (orgName.Length is 0 or > 256) throw new ValidationException("organizationName", "Organization name is required (max 256).");
        if (communityName.Length is 0 or > 256) throw new ValidationException("communityName", "Community name is required (max 256).");
        if (communityType.Length is 0 or > 100) throw new ValidationException("communityType", "Community type is required.");
        if (adminName.Length is 0 or > 256) throw new ValidationException("adminName", "Your name is required (max 256).");
        if (email is null && phone is null) throw new ValidationException("adminIdentifier", "Provide an email or a phone number to sign in with.");
        if (string.IsNullOrEmpty(request.AdminPassword) || request.AdminPassword.Length < 6)
            throw new ValidationException("adminPassword", "Password must be at least 6 characters.");

        // --- Privileged, isolated provisioning scope (platform super-admin tenant context). ---
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.RemoveAll<ITenantContext>();
        services.AddScoped<ITenantContext, SuperAdminTenantContext>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();

        // Reject a duplicate login up front (clear 409) before we create an org/community we'd orphan.
        if (await db.Users.IgnoreQueryFilters().AnyAsync(
                u => (email != null && u.Email == email) || (phone != null && u.Phone == phone), cancellationToken))
        {
            throw new ConflictException("An account with that email or phone already exists.");
        }

        var orgs = sp.GetRequiredService<IOrganizationService>();
        var communities = sp.GetRequiredService<ICommunityService>();
        var users = sp.GetRequiredService<IUserService>();
        var userRoles = sp.GetRequiredService<IUserRoleService>();

        // The CommunityAdmin role is seeded at startup (DevDataSeeder.EnsureCommunityAdminRoleAsync).
        var roleId = await db.Roles.IgnoreQueryFilters()
            .Where(r => r.Name == CommunityAdminRoleName)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ConflictException("Onboarding is not available: the CommunityAdmin role is not provisioned.");

        // 1) Organization + 2) Community (community's own id becomes the tenant discriminator).
        var org = await orgs.CreateAsync(new CreateOrganizationRequest(orgName), cancellationToken);
        var community = await communities.CreateAsync(
            new CreateCommunityRequest(org.Id, communityName, communityType), cancellationToken);

        // 3) The admin user credential (password hashed here; the service stores the hash only).
        var user = await users.CreateAsync(
            new CreateUserRequest(email, phone, passwordHasher.Hash(request.AdminPassword)), cancellationToken);

        // 4) Assign CommunityAdmin SCOPED to the new community → once ACTIVATED their JWT carries only
        //    this community and the global tenant filter isolates them to it (never platform super-admin).
        await userRoles.AssignAsync(user.Id, roleId, community.Id, cancellationToken);

        // 5) APPROVAL GATE: the account is created PENDING — suspend the login so it CANNOT sign in
        //    until a platform PowerUser activates it (the sales-qualified gate). We do NOT issue a
        //    login/token here. Login already rejects a non-Active user, so this is the whole gate.
        await db.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(u => u.Status, UserStatus.Suspended),
                cancellationToken);

        return new OnboardingResult(community.Id, community.Name);
    }
}
