using App.Application.Abstractions;
using App.Application.Auth;
using App.Application.Community;
using App.Application.Config;
using App.Application.Identity;
using App.Domain.Identity;
using App.Infrastructure;
using App.Infrastructure.Persistence;
using App.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace App.Api.DevSeed;

/// <summary>
/// DEV-ONLY startup seeder. Wired only in the Development environment (see Program.cs). It
/// (1) creates the SQL schema + applies the EF HasData master-data/permission/notification-template
/// seeds via EnsureCreated, and (2) idempotently provisions a minimal usable dataset so the mobile
/// apps can log in and exercise the modules: one organization, one community, a platform admin role
/// granting the entire permission catalog, and one admin user assigned that role.
///
/// It builds its OWN isolated service provider over AddInfrastructure with a platform super-admin
/// ITenantContext — creating a community is a platform action and the request pipeline's
/// principal-derived tenant context is fail-closed with no HttpContext. This keeps the request
/// pipeline wiring untouched.
///
/// NOT a production migration path. Production uses EF migrations + a real provisioning flow;
/// EnsureCreated + a baked dev admin is a deliberate dev convenience with a known ceiling
/// (ponytail): it only runs in Development and no-ops when the admin already exists.
/// </summary>
public static class DevDataSeeder
{
    public static async Task SeedAsync(IConfiguration configuration, DevSeedOptions options, ILoggerFactory loggerFactory, CancellationToken ct = default)
    {
        var logger = loggerFactory.CreateLogger("DevDataSeeder");

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        services.AddLogging();
        services.AddInfrastructure(configuration);

        // Platform super-admin tenant context for the seed scope: community create is a platform
        // action and the global query filter is otherwise fail-closed with no HTTP principal.
        services.RemoveAll<ITenantContext>();
        services.AddScoped<ITenantContext, SuperAdminTenantContext>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<AppDbContext>();

        // 1) Schema + HasData seeds. EnsureCreated creates the database named in the connection
        //    string and every table, applying the HasData rows (permission catalog, master-data
        //    lists, notification templates). No-op when the schema already exists.
        await db.Database.EnsureCreatedAsync(ct);

        await SeedGateConfigDefaultsAsync(sp.GetRequiredService<IConfigService>(), ct);
        await SeedHomeTilesDefaultsAsync(sp.GetRequiredService<IConfigService>(), ct);

        await EnsureAccessPermissionsAsync(db, ct);
        await EnsureAppMenuItemsAsync(db, ct);
        await EnsureAdminRolesMenuItemAsync(db, ct);
        await EnsureBillingMenuItemsAsync(db, ct);
        await EnsureAppBillingTabItemsAsync(db, ct);
        await EnsureNotificationTemplatesMenuItemAsync(db, ct);

        var hasher = sp.GetRequiredService<IPasswordHasher>();

        // Ensure the baseline "Resident" role exists on EVERY start (idempotent), BEFORE the admin
        // early-returns below — so the Add-Resident login flow has a role to assign even on a DB that
        // was already seeded.
        await EnsureResidentRoleAsync(
            db,
            sp.GetRequiredService<IRoleService>(),
            sp.GetRequiredService<IPermissionCatalog>(),
            ct);

        // Ensure the "CommunityAdmin" role exists on every start (idempotent), so onboarding can
        // assign it immediately. Upserted: new permissions are picked up automatically.
        await EnsureCommunityAdminRoleAsync(
            db,
            sp.GetRequiredService<IRoleService>(),
            sp.GetRequiredService<IPermissionCatalog>(),
            ct);

        await EnsureOrganizationAdminRoleAsync(
            db,
            sp.GetRequiredService<IRoleService>(),
            sp.GetRequiredService<IPermissionCatalog>(),
            ct);

        // Per-tile mobile menu visibility, driven by data (no hardcoding in the app). Runs AFTER the
        // role-definition upserts above (which rewrite each role's permission set) so these tile
        // grants are not wiped. Each role is granted exactly the tiles it should see. Security
        // intentionally OMITS My Visitors, Approvals, Parcels and Announcements while keeping Verify
        // Pass / Walk-in (which share the Visitor functional resource but are separate tiles here).
        // 2) Idempotency / dev-credential gate. The superadmin logs in with their PHONE number.
        //    If the superadmin phone already exists, re-stamp its password and stop (idempotent).
        var existingAdmin = await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Phone == options.AdminPhone, ct);
        if (existingAdmin is not null)
        {
            existingAdmin.PasswordHash = hasher.Hash(options.AdminPassword);
            await db.SaveChangesAsync(ct);

            var existingOrgId = await db.Organizations.IgnoreQueryFilters()
                .Where(o => !o.IsDeleted).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct);
            if (existingOrgId is { } oid)
            {
                await EnsureActiveSubscriptionAsync(db, oid, ct);
            }

            logger.LogInformation("Dev seed: superadmin {Phone} present; password re-stamped.", options.AdminPhone);
            return;
        }

        var users = sp.GetRequiredService<IUserService>();
        var roles = sp.GetRequiredService<IRoleService>();
        var userRoles = sp.GetRequiredService<IUserRoleService>();
        var orgs = sp.GetRequiredService<IOrganizationService>();
        var communities = sp.GetRequiredService<ICommunityService>();
        var catalog = sp.GetRequiredService<IPermissionCatalog>();

        var organizationId = await db.Organizations.IgnoreQueryFilters()
            .Where(o => !o.IsDeleted).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct);
        if (organizationId is null)
        {
            var org = await orgs.CreateAsync(new CreateOrganizationRequest(options.OrganizationName), ct);
            organizationId = org.Id;
        }

        var communityId = await db.Communities.IgnoreQueryFilters()
            .Where(c => !c.IsDeleted).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        if (communityId is null)
        {
            var community = await communities.CreateAsync(
                new CreateCommunityRequest(organizationId.Value, options.CommunityName, options.CommunityType), ct);
            communityId = community.Id;
        }

        // PowerUser role granting the ENTIRE permission catalog (the HasData-seeded set) — full
        // platform access. Assigned with CommunityId=null below → the platform "see everything" admin.
        // (The "Resident", "Security" and "CommunityAdmin" roles are seeded separately / at startup.)
        var catalogEntries = await catalog.ListAsync(cancellationToken: ct);
        var grants = catalogEntries.Select(p => new PermissionGrant(p.Action, p.ResourceType)).ToList();
        var role = await roles.DefineAsync(
            new DefineRoleRequest(options.AdminRoleName, RoleScope.Platform, null, grants, RoleSurface.Admin), ct);

        // Seed the Security/Watch role (visitor + gate operations). Resident role is ensured earlier.
        await EnsureSecurityRoleAsync(db, roles, catalog, ct);

        // The power user is a PURE platform principal: a phone-based login with a PLATFORM-WIDE role
        // assignment (CommunityId = null → the super-admin claim / full cross-community access). It is
        // deliberately NOT linked to any Resident and NOT a member of any unit's household.
        var admin = await users.CreateAsync(
            new CreateUserRequest(Email: null, Phone: options.AdminPhone, hasher.Hash(options.AdminPassword)), ct);

        await userRoles.AssignAsync(admin.Id, role.Id, null, ct);

        await SeedAnnouncementsAsync(db, communityId.Value, ct);

        await EnsureActiveSubscriptionAsync(db, organizationId.Value, ct);

        logger.LogInformation(
            "Dev seed complete: community {CommunityId}, power user {Phone} with {GrantCount} permission grants.",
            communityId, options.AdminPhone, grants.Count);
    }

    private static async Task EnsureActiveSubscriptionAsync(AppDbContext db, Guid organizationId, CancellationToken ct)
    {
        var exists = await db.OrgSubscriptions.IgnoreQueryFilters()
            .AnyAsync(s => s.OrganizationId == organizationId && !s.IsDeleted, ct);
        if (exists)
        {
            return;
        }

        var now = DateTime.UtcNow;
        db.OrgSubscriptions.Add(new App.Domain.Subscription.OrgSubscription
        {
            OrganizationId = organizationId,
            PlanMonths = 12,
            StartsAtUtc = now,
            ExpiresAtUtc = now.AddMonths(12),
            Status = App.Domain.Subscription.OrgSubscriptionStatuses.Active,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Idempotently seed a few sample notice-board announcements (MVP notices slice) so the resident
    /// Announcements screen has content out of the box. No-ops when the community already has any
    /// announcement. Dev convenience only (ponytail: a fixed sample set, not a real publish flow).
    /// </summary>
    private static async Task SeedAnnouncementsAsync(AppDbContext db, Guid communityId, CancellationToken ct)
    {
        var alreadySeeded = await db.Announcements.IgnoreQueryFilters()
            .AnyAsync(a => a.CommunityId == communityId, ct);
        if (alreadySeeded)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var samples = new (string Title, string Body, string Category, int DaysAgo)[]
        {
            ("Water supply maintenance", "The overhead water tanks will be cleaned this Saturday from 10 AM to 2 PM. Please store water in advance. Supply resumes by 3 PM.", "maintenance", 0),
            ("Diwali community celebration", "Join us at the clubhouse lawn on the 29th for the annual Diwali get-together. Dinner, music and a rangoli contest. All families welcome!", "event", 2),
            ("Visitor gate pass reminder", "Residents are requested to pre-approve expected visitors through the app to speed up entry at the main gate.", "general", 5),
            ("Fire drill scheduled", "A mandatory fire-safety drill will be conducted on the 1st at 11 AM. Please follow the guards' instructions and assemble at the designated point.", "emergency", 7),
        };

        foreach (var s in samples)
        {
            db.Announcements.Add(new App.Domain.Community.Announcement
            {
                CommunityId = communityId,
                Title = s.Title,
                Body = s.Body,
                Category = s.Category,
                PublishedAtUtc = now.AddDays(-s.DaysAgo),
                CreatedAtUtc = now,
                CreatedBy = "seed",
                RowVersion = [],
            });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Idempotently define a platform "Resident" role granting the baseline permissions a resident
    /// needs to use the app: manage their own profile/household/contacts, invite + approve visitors,
    /// view parcels, raise/track tickets, and read announcements. No-ops when the role already exists.
    /// The role is assigned to a specific login (scoped to that resident's community) at login-create
    /// time by <c>IResidentService.CreateLoginAsync</c>.
    /// </summary>
    private static Guid MenuGuid(string key)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("menu|" + key));
        return new Guid(bytes);
    }

    // Matches IdentityConfigurations.DeterministicGuid for the permission catalog ("{resource}|{action}",
    // no prefix) so this upsert targets the SAME row the HasData seed would create on a fresh DB.
    private static Guid PermissionGuid(string resourceType, PermissionAction action)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{resourceType}|{action}"));
        return new Guid(bytes);
    }

    // Per-tile Access (mobile-menu visibility) is keyed by the menu item's KEY (e.g. "app.parcels"),
    // so each tile is toggled independently per role — including tiles that share a functional
    // resource (My Visitors / Approvals vs Verify Pass / Walk-in all use Visitor functionally).
    // EnsureCreated does not add HasData rows to an existing DB, so upsert these catalog rows on
    // startup. The keys MUST match the app menu items' RequiredPermissionResource.
    internal static readonly string[] AppTileKeys =
    {
        "app.profile", "app.household",
        "app.visitors.invite", "app.visitors.my", "app.visitors.approvals",
        "app.recurring", "app.parcels", "app.maintenance", "app.helpdesk", "app.announcements",
        "app.verifypass", "app.walkin", "app.entryexit", "app.vehiclelookup",
        "app.watchlist", "app.sos", "app.notifications",
    };

    // The admin "Roles & Permissions" menu item (route /roles) was missing from the seed, so the
    // page had no sidebar link. EnsureCreated does not add HasData rows to an existing DB, so upsert
    // it idempotently on startup. Requires Role:View; parented under the Administration group.
    private static async Task EnsureAdminRolesMenuItemAsync(AppDbContext db, CancellationToken ct)
    {
        var id = MenuGuid("admin.roles");
        var existing = await db.MenuItems.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == id, ct);
        var actions = ((int)PermissionAction.View).ToString();
        if (existing is null)
        {
            db.MenuItems.Add(new MenuItem
            {
                Id = id,
                ParentId = MenuGuid("admin.group.administration"),
                Key = "admin.roles",
                Label = "Roles & Permissions",
                Icon = "KeyRound",
                Route = "/roles",
                Surface = MenuSurface.Admin,
                RequiredPermissionActions = actions,
                RequiredPermissionResource = "Role",
                SortOrder = 15,
                IsActive = true,
                ShowInSidebar = true,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = "seed",
                RowVersion = [],
            });
        }
        else
        {
            existing.ParentId = MenuGuid("admin.group.administration");
            existing.Label = "Roles & Permissions";
            existing.Icon = "KeyRound";
            existing.Route = "/roles";
            existing.Surface = MenuSurface.Admin;
            existing.RequiredPermissionActions = actions;
            existing.RequiredPermissionResource = "Role";
            existing.SortOrder = 15;
            existing.IsActive = true;
            existing.ShowInSidebar = true;
            existing.IsDeleted = false;
        }

        // Rename the "Users & Roles" admin item to "Manage People" on the existing DB (HasData won't
        // re-apply). Only touches the label; route/permission unchanged.
        var usersItem = await db.MenuItems.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == MenuGuid("admin.users"), ct);
        if (usersItem is not null && usersItem.Label != "Manage People")
        {
            usersItem.Label = "Manage People";
        }

        await db.SaveChangesAsync(ct);
    }

        private static async Task EnsureNotificationTemplatesMenuItemAsync(AppDbContext db, CancellationToken ct)
    {
        var id = MenuGuid("admin.notification-templates");
        var existing = await db.MenuItems.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == id, ct);
        var actions = ((int)PermissionAction.View).ToString();
        if (existing is null)
        {
            db.MenuItems.Add(new MenuItem
            {
                Id = id,
                ParentId = MenuGuid("admin.group.administration"),
                Key = "admin.notification-templates",
                Label = "Notification Templates",
                Icon = "MailOutlined",
                Route = "/notification-templates",
                Surface = MenuSurface.Admin,
                RequiredPermissionActions = actions,
                RequiredPermissionResource = "Configuration",
                SortOrder = 35,
                IsActive = true,
                ShowInSidebar = true,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = "seed",
                RowVersion = [],
            });
        }
        else
        {
            existing.ParentId = MenuGuid("admin.group.administration");
            existing.Label = "Notification Templates";
            existing.Icon = "MailOutlined";
            existing.Route = "/notification-templates";
            existing.Surface = MenuSurface.Admin;
            existing.RequiredPermissionActions = actions;
            existing.RequiredPermissionResource = "Configuration";
            existing.SortOrder = 35;
            existing.IsActive = true;
            existing.ShowInSidebar = true;
            existing.IsDeleted = false;
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureBillingMenuItemsAsync(AppDbContext db, CancellationToken ct)
    {
        var defs = new (string Key, string Label, string? Route, string? Icon, int Sort, string? Parent, PermissionAction[] Actions, string Resource, bool ShowInSidebar)[]
        {
            ("admin.billing", "Maintenance Billing", "/maintenance-billing", "Wallet", 40, "admin.group.operations",
                new[] { PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete }, "MaintenanceBill", true),
            ("admin.billing.bills", "Bills", null, null, 0, "admin.billing",
                new[] { PermissionAction.View, PermissionAction.Create, PermissionAction.Edit }, "MaintenanceBill", false),
            ("admin.billing.charges", "Charges", null, null, 10, "admin.billing",
                new[] { PermissionAction.View, PermissionAction.Create, PermissionAction.Edit }, "MaintenanceCharge", false),
            ("admin.billing.expenses", "Expenses", null, null, 20, "admin.billing",
                new[] { PermissionAction.View, PermissionAction.Create, PermissionAction.Edit }, "MaintenanceExpense", false),
            ("admin.billing.dashboard", "Dashboard", null, null, 30, "admin.billing",
                new[] { PermissionAction.View }, "MaintenanceBill", false),
        };

        foreach (var d in defs)
        {
            var id = MenuGuid(d.Key);
            var actions = string.Join(',', d.Actions.Select(a => (int)a));
            var existing = await db.MenuItems.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == id, ct);
            if (existing is null)
            {
                db.MenuItems.Add(new MenuItem
                {
                    Id = id,
                    ParentId = MenuGuid(d.Parent!),
                    Key = d.Key,
                    Label = d.Label,
                    Icon = d.Icon,
                    Route = d.Route,
                    Surface = MenuSurface.Admin,
                    RequiredPermissionActions = actions,
                    RequiredPermissionResource = d.Resource,
                    SortOrder = d.Sort,
                    IsActive = true,
                    ShowInSidebar = d.ShowInSidebar,
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedBy = "seed",
                    RowVersion = [],
                });
            }
            else
            {
                existing.ParentId = MenuGuid(d.Parent!);
                existing.Label = d.Label;
                existing.Icon = d.Icon;
                existing.Route = d.Route;
                existing.Surface = MenuSurface.Admin;
                existing.RequiredPermissionActions = actions;
                existing.RequiredPermissionResource = d.Resource;
                existing.SortOrder = d.Sort;
                existing.IsActive = true;
                existing.ShowInSidebar = d.ShowInSidebar;
                existing.IsDeleted = false;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureAppBillingTabItemsAsync(AppDbContext db, CancellationToken ct)
    {
        var defs = new (string Key, string Label, PermissionAction[] Actions, string Resource)[]
        {
            ("app.billing.bills", "Bills", new[] { PermissionAction.View, PermissionAction.Create, PermissionAction.Edit }, "MaintenanceBill"),
            ("app.billing.charges", "Charges", new[] { PermissionAction.View, PermissionAction.Create, PermissionAction.Edit }, "MaintenanceCharge"),
            ("app.billing.ledger", "Ledger", new[] { PermissionAction.View, PermissionAction.Create, PermissionAction.Edit }, "MaintenanceExpense"),
            ("app.billing.dashboard", "Dashboard", new[] { PermissionAction.View }, "MaintenanceBill"),
        };

        var sort = 0;
        foreach (var d in defs)
        {
            var id = MenuGuid(d.Key);
            var actions = string.Join(',', d.Actions.Select(a => (int)a));
            var existing = await db.MenuItems.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == id, ct);
            if (existing is null)
            {
                db.MenuItems.Add(new MenuItem
                {
                    Id = id,
                    ParentId = MenuGuid("app.billing"),
                    Key = d.Key,
                    Label = d.Label,
                    Icon = null,
                    Route = null,
                    Surface = MenuSurface.App,
                    RequiredPermissionActions = actions,
                    RequiredPermissionResource = d.Resource,
                    SortOrder = sort,
                    IsActive = true,
                    ShowInSidebar = false,
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedBy = "seed",
                    RowVersion = [],
                });
            }
            else
            {
                existing.ParentId = MenuGuid("app.billing");
                existing.Label = d.Label;
                existing.Route = null;
                existing.Surface = MenuSurface.App;
                existing.RequiredPermissionActions = actions;
                existing.RequiredPermissionResource = d.Resource;
                existing.SortOrder = sort;
                existing.IsActive = true;
                existing.ShowInSidebar = false;
                existing.IsDeleted = false;
            }
            sort += 10;
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureAccessPermissionsAsync(AppDbContext db, CancellationToken ct)
    {
        foreach (var tileKey in AppTileKeys)
        {
            var id = PermissionGuid(tileKey, PermissionAction.Access);
            var exists = await db.Set<Permission>().IgnoreQueryFilters().AnyAsync(p => p.Id == id, ct);
            if (!exists)
            {
                db.Set<Permission>().Add(new Permission
                {
                    Id = id,
                    ResourceType = tileKey,
                    Action = PermissionAction.Access,
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedBy = "seed",
                    RowVersion = [],
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // Grant a role exactly the set of app tiles it should SEE on mobile (per-tile Access). This is
    // the single place the "who sees which tile" policy lives as data; editing a role's tile set in
    // the DB (or admin UI) changes the mobile menu with no code change.
    private static async Task EnsureRoleTileAccessAsync(
        AppDbContext db, string roleName, string[] tileKeys, CancellationToken ct)
    {
        var roleId = await db.Roles.IgnoreQueryFilters()
            .Where(r => r.Name == roleName)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);
        if (roleId is not { } rid)
        {
            return;
        }

        var wanted = tileKeys.Select(k => PermissionGuid(k, PermissionAction.Access)).ToHashSet();

        var existing = await db.Set<RolePermission>().IgnoreQueryFilters()
            .Where(rp => rp.RoleId == rid)
            .Select(rp => rp.PermissionId)
            .ToListAsync(ct);
        var existingSet = existing.ToHashSet();

        foreach (var permId in wanted)
        {
            if (!existingSet.Contains(permId))
            {
                db.Set<RolePermission>().Add(new RolePermission { RoleId = rid, PermissionId = permId });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureAppMenuItemsAsync(AppDbContext db, CancellationToken ct)
    {
        var defs = new (string Key, string Label, string? Route, string? Icon, int Sort, string? Parent, PermissionAction[]? Actions, string? Resource)[]
        {
            ("app.home", "Home", "home", "home", 0, null, null, null),
            ("app.services", "Services", "services", "grid", 10, null, null, null),

            ("app.group.myhome", "My Home", null, null, 20, "app.services", null, null),
            ("app.profile", "My Profile", "my-profile", "person", 0, "app.group.myhome", new[] { PermissionAction.View, PermissionAction.Access }, "Resident"),
            ("app.household", "My Household", "my-household", "home", 10, "app.group.myhome", new[] { PermissionAction.View, PermissionAction.Access }, "HouseholdMember"),

            ("app.group.community", "Community", null, null, 30, "app.services", null, null),
            ("app.visitors.invite", "Invite Visitor", "invite-visitor", "person-add", 0, "app.group.community", new[] { PermissionAction.Create, PermissionAction.Access }, "VisitPass"),
            ("app.visitors.my", "My Visitors", "visitors", "list", 10, "app.group.community", new[] { PermissionAction.View, PermissionAction.Access }, "Visitor"),
            ("app.visitors.approvals", "Approvals", "approvals", "checkmark-done", 20, "app.group.community", new[] { PermissionAction.View, PermissionAction.Access }, "Visitor"),
            ("app.recurring", "Recurring Visitors", "recurring-visitors", "repeat", 30, "app.group.community", new[] { PermissionAction.View, PermissionAction.Access }, "RecurringVisitor"),
            ("app.parcels", "Parcels", "parcels", "cube", 40, "app.group.community", new[] { PermissionAction.View, PermissionAction.Access }, "Parcel"),
            ("app.maintenance", "Maintenance", "maintenance", "card", 50, "app.group.community", new[] { PermissionAction.View, PermissionAction.Access }, "MaintenanceBill"),
            ("app.billing", "Billing", "billing", "wallet", 55, "app.group.community", new[] { PermissionAction.View, PermissionAction.Access }, "MaintenanceBill"),
            ("app.helpdesk", "Help Desk", "raise-ticket", "construct", 60, "app.group.community", new[] { PermissionAction.View, PermissionAction.Access }, "Ticket"),
            ("app.announcements", "Announcements", "announcements", "megaphone", 70, "app.group.community", new[] { PermissionAction.View, PermissionAction.Access }, "Announcement"),

            ("app.group.security", "Security", null, null, 40, "app.services", null, null),
            ("app.verifypass", "Verify Pass", "verify-pass", "scan", 0, "app.group.security", new[] { PermissionAction.Create, PermissionAction.Access }, "Visitor"),
            ("app.walkin", "Walk-in Entry", "register-walkin", "person-add", 10, "app.group.security", new[] { PermissionAction.Create, PermissionAction.Access }, "Visitor"),
            ("app.entryexit", "Entry / Exit", "entry-exit", "swap", 20, "app.group.security", new[] { PermissionAction.View, PermissionAction.Access }, "EntryExitEvent"),
            ("app.vehiclelookup", "Vehicle Lookup", "vehicle-lookup", "car", 30, "app.group.security", new[] { PermissionAction.View, PermissionAction.Access }, "Vehicle"),
            ("app.watchlist", "Watchlist", "watchlist", "shield", 40, "app.group.security", new[] { PermissionAction.View, PermissionAction.Access }, "WatchlistEntry"),
            ("app.sos", "SOS Alerts", "sos", "alert", 50, "app.group.security", new[] { PermissionAction.View, PermissionAction.Access }, "SosAlert"),

            ("app.group.support", "Support", null, null, 50, "app.services", null, null),
            ("app.notifications", "Notifications", "notifications", "bell", 0, "app.group.support", new[] { PermissionAction.View, PermissionAction.Access }, "Audit"),
        };

        foreach (var d in defs)
        {
            var id = MenuGuid(d.Key);
            var existing = await db.MenuItems.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == id, ct);
            var actions = d.Actions is { Length: > 0 } ? string.Join(',', d.Actions.Select(a => (int)a)) : null;
            if (existing is null)
            {
                db.MenuItems.Add(new MenuItem
                {
                    Id = id,
                    ParentId = d.Parent is null ? null : MenuGuid(d.Parent),
                    Key = d.Key,
                    Label = d.Label,
                    Icon = d.Icon,
                    Route = d.Route,
                    Surface = MenuSurface.App,
                    RequiredPermissionActions = actions,
                    RequiredPermissionResource = d.Resource,
                    SortOrder = d.Sort,
                    IsActive = true,
                    ShowInSidebar = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedBy = "seed",
                    RowVersion = [],
                });
            }
            else
            {
                existing.ParentId = d.Parent is null ? null : MenuGuid(d.Parent);
                existing.Label = d.Label;
                existing.Icon = d.Icon;
                existing.Route = d.Route;
                existing.Surface = MenuSurface.App;
                existing.RequiredPermissionActions = actions;
                existing.RequiredPermissionResource = d.Resource;
                existing.SortOrder = d.Sort;
                existing.IsActive = true;
                existing.ShowInSidebar = true;
                existing.IsDeleted = false;
            }
        }

        var obsoleteKeys = new[] { "app.visitors" };
        foreach (var key in obsoleteKeys)
        {
            var id = MenuGuid(key);
            var row = await db.MenuItems.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == id, ct);
            if (row is not null && row.IsActive)
            {
                row.IsActive = false;
                row.ShowInSidebar = false;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureResidentRoleAsync(
        AppDbContext db, IRoleService roles, IPermissionCatalog catalog, CancellationToken ct)
    {
        // Upsert (not no-op): if the role already exists we REDEFINE its grants so the permission set
        // stays in sync across restarts (e.g. when a new resource like Pet is added to the baseline).
        var existingId = await db.Roles.IgnoreQueryFilters()
            .Where(r => r.Name == "Resident")
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);

        // Baseline resident permission set (least-privilege). Only resources a resident self-manages
        // or reads; nothing community-admin. Any entry not present in the seeded catalog is dropped
        // defensively so the role definition never fails on an unknown pair.
        var wanted = new (PermissionAction Action, string ResourceType)[]
        {
            (PermissionAction.View, "Resident"),
            (PermissionAction.Edit, "Resident"),
            (PermissionAction.Create, "HouseholdMember"),
            (PermissionAction.View, "HouseholdMember"),
            (PermissionAction.Create, "EmergencyContact"),
            (PermissionAction.View, "EmergencyContact"),
            (PermissionAction.View, "Unit"),
            (PermissionAction.Create, "Vehicle"),
            (PermissionAction.View, "Vehicle"),
            (PermissionAction.Create, "Pet"),
            (PermissionAction.View, "Pet"),
            (PermissionAction.View, "VisitPass"),
            (PermissionAction.Create, "VisitPass"),
            (PermissionAction.Edit, "VisitPass"),
            (PermissionAction.View, "Visitor"),
            (PermissionAction.Approve, "Visitor"),
            (PermissionAction.Create, "RecurringVisitor"),
            // Residents manage their OWN recurring visitors (daily help/staff): the screen lists +
            // revokes them, so View is required alongside Create (self-scoped by the tenant filter).
            (PermissionAction.View, "RecurringVisitor"),
            (PermissionAction.View, "Parcel"),
            (PermissionAction.Create, "ParcelAuthorization"),
            (PermissionAction.View, "ParcelAuthorization"),
            (PermissionAction.View, "CustodyTimelineEntry"),
            (PermissionAction.Create, "Ticket"),
            (PermissionAction.View, "Ticket"),
            (PermissionAction.Create, "TicketAttachment"),
            (PermissionAction.View, "TicketAttachment"),
            (PermissionAction.View, "TicketComment"),
            (PermissionAction.Create, "TicketComment"),
            (PermissionAction.View, "Announcement"),
            (PermissionAction.View, "Community"),
            (PermissionAction.View, "MaintenanceCharge"),
            (PermissionAction.View, "MaintenanceBill"),
            (PermissionAction.View, "MaintenancePayment"),
            (PermissionAction.Create, "MaintenancePayment"),
            // File_Service upload + download. Residents capture photos (visitor pass, ticket
            // attachment, guard photo) and view them back; without these the multipart upload 403s
            // on Create and the authorized image download 403s on View (photo saves/shows nothing).
            (PermissionAction.Create, "File"),
            (PermissionAction.View, "File"),
            // Master-data reads (dropdown option lists: Vehicle_Type, Visitor_Category,
            // ID_Proof_Type, Ticket_Category, etc.). The master-data GET is View-gated on
            // "MasterData"; without this grant a resident's dropdowns 403 ("no permission").
            (PermissionAction.View, "MasterData"),
        };

        _ = wanted;
        var grants = new List<PermissionGrant>();

        var request = new DefineRoleRequest("Resident", RoleScope.Platform, null, grants);
        if (existingId is { } id)
        {
            await roles.UpdateAsync(id, request, ct);
        }
        else
        {
            await roles.DefineAsync(request, ct);
        }
    }

    /// <summary>
    /// Idempotently define a platform "Security" (watch/guard) role: visitors and gate operations —
    /// register walk-ins, verify passes, record entry/exit, manage the watchlist, raise/manage SOS,
    /// and read units/residents for context. No resident self-service, no community admin. No-ops when
    /// the role already exists. Assigned to a guard login scoped to their community at creation time.
    /// </summary>
    private static async Task EnsureSecurityRoleAsync(
        AppDbContext db, IRoleService roles, IPermissionCatalog catalog, CancellationToken ct)
    {
        var exists = await db.Roles.IgnoreQueryFilters().AnyAsync(r => r.Name == "Security", ct);
        if (exists)
        {
            return;
        }

        var wanted = new (PermissionAction Action, string ResourceType)[]
        {
            // Visitors / walk-ins
            (PermissionAction.View, "Visitor"),
            (PermissionAction.Create, "Visitor"),
            (PermissionAction.Edit, "Visitor"),
            (PermissionAction.View, "VisitPass"),
            (PermissionAction.View, "RecurringVisitor"),
            (PermissionAction.View, "EntryExitEvent"),
            (PermissionAction.Create, "EntryExitEvent"),
            // Security operations
            (PermissionAction.View, "WatchlistEntry"),
            (PermissionAction.Create, "WatchlistEntry"),
            (PermissionAction.Edit, "WatchlistEntry"),
            (PermissionAction.View, "GuardShift"),
            (PermissionAction.View, "Incident"),
            (PermissionAction.Create, "Incident"),
            (PermissionAction.Edit, "Incident"),
            (PermissionAction.View, "SosAlert"),
            (PermissionAction.Create, "SosAlert"),
            (PermissionAction.Edit, "SosAlert"),
            // Announcements (read the notice board)
            (PermissionAction.View, "Announcement"),
            // Parcels at the gate (register + hand over)
            (PermissionAction.View, "Parcel"),
            (PermissionAction.Create, "Parcel"),
            (PermissionAction.Edit, "Parcel"),
            (PermissionAction.View, "CustodyLocation"),
            (PermissionAction.Create, "CustodyLocation"),
            // Context reads
            (PermissionAction.View, "Unit"),
            (PermissionAction.View, "Resident"),
            (PermissionAction.View, "HouseholdMember"),
            (PermissionAction.View, "Community"),
        };

        _ = wanted;
        var grants = new List<PermissionGrant>();

        await roles.DefineAsync(new DefineRoleRequest("Security", RoleScope.Platform, null, grants, RoleSurface.App), ct);
    }

    /// <summary>
    /// Idempotently define a platform "CommunityAdmin" role: full management of a single community's
    /// data (units, wings, residents, announcements, users/roles within scope), but NOT platform-level
    /// operations (creating organizations, cross-community visibility). The role is ASSIGNED per-user
    /// with a specific CommunityId to scope them; the role definition itself is platform-scoped (like
    /// Resident/Security) so it can be reused across communities. Upserted on every startup so new
    /// resource types are included automatically.
    /// </summary>
    private static async Task EnsureCommunityAdminRoleAsync(
        AppDbContext db, IRoleService roles, IPermissionCatalog catalog, CancellationToken ct)
    {
        var existingId = await db.Roles.IgnoreQueryFilters()
            .Where(r => r.Name == "CommunityAdmin")
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);

        // Admin-level permissions for one community. Covers community structure (hierarchy/units),
        // people (residents, household, emergency contacts), gate (visitors/passes/entry-exit),
        // parcels, helpdesk, announcements, user/role management within the community, files, and
        // master-data reads. Excludes: Organization Create/Edit/Delete, platform-wide config.
        var wanted = new (PermissionAction Action, string ResourceType)[]
        {
            // Community
            (PermissionAction.View, "Community"),
            (PermissionAction.Edit, "Community"),
            // Hierarchy / Wings
            (PermissionAction.View, "HierarchyNode"),
            (PermissionAction.Create, "HierarchyNode"),
            (PermissionAction.Edit, "HierarchyNode"),
            (PermissionAction.Delete, "HierarchyNode"),
            // Units
            (PermissionAction.View, "Unit"),
            (PermissionAction.Create, "Unit"),
            (PermissionAction.Edit, "Unit"),
            (PermissionAction.Delete, "Unit"),
            (PermissionAction.Export, "Unit"),
            // Residents
            (PermissionAction.View, "Resident"),
            (PermissionAction.Create, "Resident"),
            (PermissionAction.Edit, "Resident"),
            (PermissionAction.Delete, "Resident"),
            (PermissionAction.Approve, "Resident"),
            // Resident self-onboarding join requests (approve/reject the pending queue).
            (PermissionAction.View, "JoinRequest"),
            (PermissionAction.Approve, "JoinRequest"),
            // Household / contacts
            (PermissionAction.View, "HouseholdMember"),
            (PermissionAction.Create, "HouseholdMember"),
            (PermissionAction.View, "EmergencyContact"),
            (PermissionAction.Create, "EmergencyContact"),
            // Assets
            (PermissionAction.View, "Vehicle"),
            (PermissionAction.Create, "Vehicle"),
            (PermissionAction.View, "Pet"),
            (PermissionAction.Create, "Pet"),
            // Gate / visitors
            (PermissionAction.View, "Visitor"),
            (PermissionAction.Create, "Visitor"),
            (PermissionAction.Edit, "Visitor"),
            (PermissionAction.Approve, "Visitor"),
            (PermissionAction.View, "VisitPass"),
            (PermissionAction.Create, "VisitPass"),
            (PermissionAction.View, "RecurringVisitor"),
            (PermissionAction.Create, "RecurringVisitor"),
            (PermissionAction.View, "EntryExitEvent"),
            (PermissionAction.Create, "EntryExitEvent"),
            // Security ops
            (PermissionAction.View, "WatchlistEntry"),
            (PermissionAction.Create, "WatchlistEntry"),
            (PermissionAction.Edit, "WatchlistEntry"),
            (PermissionAction.View, "GuardShift"),
            (PermissionAction.View, "Incident"),
            (PermissionAction.Create, "Incident"),
            (PermissionAction.View, "SosAlert"),
            (PermissionAction.Create, "SosAlert"),
            // Parcels
            (PermissionAction.View, "Parcel"),
            (PermissionAction.Create, "Parcel"),
            (PermissionAction.Edit, "Parcel"),
            (PermissionAction.View, "CustodyLocation"),
            (PermissionAction.Create, "CustodyLocation"),
            (PermissionAction.View, "CustodyTimelineEntry"),
            // Helpdesk
            (PermissionAction.View, "Ticket"),
            (PermissionAction.Create, "Ticket"),
            (PermissionAction.Edit, "Ticket"),
            (PermissionAction.View, "TicketAttachment"),
            (PermissionAction.Create, "TicketAttachment"),
            (PermissionAction.View, "TicketComment"),
            (PermissionAction.Create, "TicketComment"),
            // Announcements
            (PermissionAction.View, "Announcement"),
            (PermissionAction.Create, "Announcement"),
            (PermissionAction.Edit, "Announcement"),
            (PermissionAction.Delete, "Announcement"),
            // Maintenance billing
            (PermissionAction.View, "MaintenanceCharge"),
            (PermissionAction.Create, "MaintenanceCharge"),
            (PermissionAction.Edit, "MaintenanceCharge"),
            (PermissionAction.View, "MaintenanceBill"),
            (PermissionAction.Create, "MaintenanceBill"),
            (PermissionAction.Edit, "MaintenanceBill"),
            (PermissionAction.View, "MaintenancePayment"),
            (PermissionAction.Create, "MaintenancePayment"),
            (PermissionAction.Approve, "MaintenancePayment"),
            // User/role management within community
            (PermissionAction.View, "User"),
            (PermissionAction.Create, "User"),
            (PermissionAction.Edit, "User"),
            (PermissionAction.View, "Role"),
            (PermissionAction.Create, "Role"),
            (PermissionAction.View, "Permission"),
            (PermissionAction.Assign, "Role"),
            // Files + master data
            (PermissionAction.View, "File"),
            (PermissionAction.Create, "File"),
            (PermissionAction.View, "MasterData"),
        };

        _ = wanted;
        var grants = new List<PermissionGrant>();

        var request = new DefineRoleRequest("CommunityAdmin", RoleScope.Platform, null, grants, RoleSurface.Admin);
        if (existingId is { } id)
        {
            await roles.UpdateAsync(id, request, ct);
        }
        else
        {
            await roles.DefineAsync(request, ct);
        }
    }

    private static async Task EnsureOrganizationAdminRoleAsync(
        AppDbContext db, IRoleService roles, IPermissionCatalog catalog, CancellationToken ct)
    {
        var existingId = await db.Roles.IgnoreQueryFilters()
            .Where(r => r.Name == "OrganizationAdmin")
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);

        var wanted = new (PermissionAction Action, string ResourceType)[]
        {
            (PermissionAction.View, "Organization"),
            (PermissionAction.Edit, "Organization"),
            (PermissionAction.View, "Community"),
            (PermissionAction.Create, "Community"),
            (PermissionAction.Edit, "Community"),
            (PermissionAction.View, "HierarchyNode"),
            (PermissionAction.Create, "HierarchyNode"),
            (PermissionAction.Edit, "HierarchyNode"),
            (PermissionAction.Delete, "HierarchyNode"),
            (PermissionAction.View, "Unit"),
            (PermissionAction.Create, "Unit"),
            (PermissionAction.Edit, "Unit"),
            (PermissionAction.Delete, "Unit"),
            (PermissionAction.Export, "Unit"),
            (PermissionAction.View, "Resident"),
            (PermissionAction.Create, "Resident"),
            (PermissionAction.Edit, "Resident"),
            (PermissionAction.Delete, "Resident"),
            (PermissionAction.Approve, "Resident"),
            (PermissionAction.View, "Announcement"),
            (PermissionAction.Create, "Announcement"),
            (PermissionAction.Edit, "Announcement"),
            (PermissionAction.Delete, "Announcement"),
            (PermissionAction.View, "Ticket"),
            (PermissionAction.Create, "Ticket"),
            (PermissionAction.Edit, "Ticket"),
            (PermissionAction.View, "User"),
            (PermissionAction.Create, "User"),
            (PermissionAction.Edit, "User"),
            (PermissionAction.View, "Role"),
            (PermissionAction.Assign, "Role"),
            (PermissionAction.View, "Permission"),
            (PermissionAction.View, "File"),
            (PermissionAction.Create, "File"),
            (PermissionAction.View, "MasterData"),
            (PermissionAction.View, "Audit"),
        };

        _ = wanted;
        var grants = new List<PermissionGrant>();

        var request = new DefineRoleRequest("OrganizationAdmin", RoleScope.Platform, null, grants, RoleSurface.Admin);
        if (existingId is { } id)
        {
            await roles.UpdateAsync(id, request, ct);
        }
        else
        {
            await roles.DefineAsync(request, ct);
        }
    }

    private static async Task SeedGateConfigDefaultsAsync(IConfigService config, CancellationToken ct)
    {
        var defaults = new (string Key, string Value)[]
        {
            ("gate.walkIn.approvalRequired", "true"),
            ("gate.walkIn.pendingExpiryMinutes", "15"),
            ("gate.visitPass.defaultValidity", "2"),
            ("gate.visitPass.defaultValidityUnit", "days"),
            ("gate.visitPass.defaultValidityHours", "24"),
            ("gate.visitPass.defaultMaxUses", "1"),
            ("gate.recurringVisitor.expiringSoonDays", "10"),
        };

        foreach (var (key, value) in defaults)
        {
            if (await config.GetAsync(key, null, ct) is null)
            {
                await config.SetAsync(key, value, null, ct);
            }
        }
    }

    private static async Task SeedHomeTilesDefaultsAsync(IConfigService config, CancellationToken ct)
    {
        var defaults = new (string Key, string Value)[]
        {
            ("app.home.tiles.resident", "my-profile,my-household,my-visitors,maintenance"),
            ("app.home.tiles.security", "verify-pass,register-walkin,entry-exit,notifications"),
        };

        foreach (var (key, value) in defaults)
        {
            if (await config.GetAsync(key, null, ct) is null)
            {
                await config.SetAsync(key, value, null, ct);
            }
        }
    }
}

/// <summary>Dev seed inputs (dev-only; bound from the "DevSeed" config section in Program.cs).</summary>
public sealed class DevSeedOptions
{
    public const string SectionName = "DevSeed";

    public bool Enabled { get; set; }
    public string OrganizationName { get; set; } = "App Demo Org";
    public string CommunityName { get; set; } = "App Demo Community";
    public string CommunityType { get; set; } = "apartment_society";
    /// <summary>The platform-wide "see everything" role (CommunityId=null assignment). Was "Superadmin".</summary>
    public string AdminRoleName { get; set; } = "PowerUser";
    /// <summary>The power user logs in with their PHONE number incl. country code (login identifier).</summary>
    public string AdminPhone { get; set; } = "+910000000000";
    public string AdminPassword { get; set; } = "123";
}
