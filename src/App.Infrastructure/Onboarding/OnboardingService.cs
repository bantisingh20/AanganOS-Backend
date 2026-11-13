using System.Globalization;
using App.Application.Abstractions;
using App.Application.Auth;
using App.Application.Community;
using App.Application.Exceptions;
using App.Application.Identity;
using App.Application.Onboarding;
using App.Application.Residents;
using App.Domain.Community;
using App.Domain.Identity;
using App.Domain.Subscription;
using App.Infrastructure.Persistence;
using App.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Onboarding;


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

        // 1) Organization = the SOCIETY. Each WING becomes its own Community under it (the community
        //    id is the tenant discriminator). Wing names come from the request; fall back to the
        //    single community name for the legacy single-wing shape.
        var org = await orgs.CreateAsync(new CreateOrganizationRequest(orgName), cancellationToken);

        var wingNames = NormalizeWings(request.Wings, communityName);
        if (wingNames.Count == 0)
        {
            throw new ValidationException("wings", "At least one wing is required.");
        }

        // 2) Create a Community per wing, and generate its floors × rooms-per-floor units. Unit
        //    numbers are prefixed by the wing (e.g. A-101) so they read clearly. Units use the default
        //    apartment/available master-data codes (seeded platform-wide). Bounds are enforced per
        //    wing against the configured bulk ceiling so a registration can never create an unbounded
        //    set.
        var createdCommunities = new List<(Guid Id, string Name)>();
        foreach (var wing in wingNames)
        {
            var wingCommunityName = FormatWingName(wing);
            var wingCommunity = await communities.CreateAsync(
                new CreateCommunityRequest(org.Id, wingCommunityName, communityType), cancellationToken);
            createdCommunities.Add((wingCommunity.Id, wingCommunity.Name));

            await GenerateUnitsAsync(sp, wingCommunity.Id, wing, request.Floors, request.RoomsPerFloor, cancellationToken);
        }

        var firstCommunity = createdCommunities[0];

        // 3) The admin user credential (password hashed here; the service stores the hash only).
        var user = await users.CreateAsync(
            new CreateUserRequest(email, phone, passwordHasher.Hash(request.AdminPassword)), cancellationToken);

        // 4) Assign CommunityAdmin SCOPED to EVERY wing community → once ACTIVATED the society admin's
        //    JWT carries all their wings and the global tenant filter isolates them to those.
        foreach (var c in createdCommunities)
        {
            await userRoles.AssignAsync(user.Id, roleId, c.Id, cancellationToken);
        }

        // 4b) Create a Resident row for the society admin, linked to their user id, in the first wing,
        //     and associate them to a unit there (if generated). This makes residents.me() resolve for
        //     the admin and surfaces them with a unit (they are a real person in the society, not just
        //     a login). ResidentType "owner" (no admin-specific master code exists).
        await CreateAdminResidentAsync(sp, firstCommunity.Id, user.Id, adminName, email, phone, cancellationToken);

        var planMonths = OrgSubscriptionPlans.IsValid(request.PlanMonths) ? request.PlanMonths : 12;
        var provisionedAtUtc = DateTime.UtcNow;
        db.OrgSubscriptions.Add(new OrgSubscription
        {
            OrganizationId = org.Id,
            PlanMonths = planMonths,
            StartsAtUtc = provisionedAtUtc,
            ExpiresAtUtc = provisionedAtUtc,
            Status = OrgSubscriptionStatuses.Expired,
        });
        await db.SaveChangesAsync(cancellationToken);

        // 5) APPROVAL GATE: the account is created PENDING — suspend the login so it CANNOT sign in
        //    until a platform PowerUser activates it (the sales-qualified gate). We do NOT issue a
        //    login/token here. Login already rejects a non-Active user, so this is the whole gate.
        await db.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(u => u.Status, UserStatus.Suspended),
                cancellationToken);

        return new OnboardingResult(firstCommunity.Id, firstCommunity.Name);
    }

    // Wing names: trimmed, de-duplicated (case-insensitive), capped at 50. Falls back to the single
    // community name when no wings were supplied (legacy single-wing registration).
    private static IReadOnlyList<string> NormalizeWings(IReadOnlyList<string>? wings, string fallbackName)
    {
        var source = (wings is { Count: > 0 })
            ? wings
            : (string.IsNullOrWhiteSpace(fallbackName) ? Array.Empty<string>() : new[] { fallbackName });

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in source)
        {
            var name = (raw ?? string.Empty).Trim();
            if (name.Length == 0 || name.Length > 100) continue;
            if (seen.Add(name)) result.Add(name);
            if (result.Count >= 50) break;
        }
        return result;
    }

    // A wing named "A" becomes community "A Wing"; a wing already containing "wing"/"tower"/"block"
    // (e.g. "Tower 1") is kept as-is.
    private static string FormatWingName(string wing)
    {
        var lower = wing.ToLowerInvariant();
        if (lower.Contains("wing") || lower.Contains("tower") || lower.Contains("block"))
        {
            return wing;
        }
        return $"{wing} Wing";
    }

    private static async Task CreateAdminResidentAsync(
        IServiceProvider sp, Guid communityId, Guid userId, string name, string? email, string? phone, CancellationToken cancellationToken)
    {
        var db = sp.GetRequiredService<AppDbContext>();

        // Direct insert (not via IResidentService) so the admin resident is created verified and
        // linked to the user id, consistent with AdminUserService. An admin is pre-trusted, not a
        // KYC-pending resident.
        var resident = new App.Domain.Residents.Resident
        {
            CommunityId = communityId,
            UserId = userId,
            Name = name,
            Email = email,
            Phone = phone,
            ResidentType = "owner",
            VerificationStatus = App.Domain.Residents.ResidentVerificationStatuses.Verified,
        };
        db.Set<App.Domain.Residents.Resident>().Add(resident);
        await db.SaveChangesAsync(cancellationToken);

        // Attach to the first generated unit in this wing, if any.
        var firstUnitId = await db.Units.IgnoreQueryFilters()
            .Where(u => u.CommunityId == communityId && !u.IsDeleted)
            .OrderBy(u => u.UnitNumber)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (firstUnitId is { } unitId)
        {
            db.Set<App.Domain.Residents.HouseholdMember>().Add(new App.Domain.Residents.HouseholdMember
            {
                CommunityId = communityId,
                ResidentId = resident.Id,
                UnitId = unitId,
                Relationship = "owner",
                StartDate = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task GenerateUnitsAsync(
        IServiceProvider sp, Guid communityId, string wing, int floors, int roomsPerFloor, CancellationToken cancellationToken)
    {
        if (floors <= 0 || roomsPerFloor <= 0)
        {
            return;
        }

        // Defensive bounds (the request DataAnnotations also cap each at 200). Cap the total against
        // the configured bulk ceiling so a registration can never create an unbounded unit set.
        if (floors > 200) floors = 200;
        if (roomsPerFloor > 200) roomsPerFloor = 200;

        var maxUnits = sp.GetRequiredService<IOptions<BulkOptions>>().Value.MaxRowsPerOperation;
        if ((long)floors * roomsPerFloor > maxUnits)
        {
            throw new ValidationException(
                "floors",
                $"Floors × rooms-per-floor ({floors * roomsPerFloor}) exceeds the maximum of {maxUnits} units.");
        }

        var units = sp.GetRequiredService<IUnitService>();
        var pad = roomsPerFloor >= 100 ? 3 : 2;
        var prefix = string.IsNullOrWhiteSpace(wing) ? string.Empty : $"{wing.Trim()}-";

        for (var f = 1; f <= floors; f++)
        {
            var floorLabel = f.ToString(CultureInfo.InvariantCulture);
            for (var r = 1; r <= roomsPerFloor; r++)
            {
                var unitNumber = $"{prefix}{floorLabel}{r.ToString(CultureInfo.InvariantCulture).PadLeft(pad, '0')}";
                await units.CreateAsync(
                    new CreateUnitRequest(
                        communityId,
                        HierarchyNodeId: null,
                        UnitNumber: unitNumber,
                        UnitType: "apartment",
                        Area: null,
                        Floor: floorLabel,
                        Bedrooms: null,
                        UnitStatus: UnitStatuses.Available,
                        CustomAttributes: null),
                    cancellationToken);
            }
        }
    }
}
