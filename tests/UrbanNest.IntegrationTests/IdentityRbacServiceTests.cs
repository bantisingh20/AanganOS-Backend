using UrbanNest.Application.Abstractions;
using UrbanNest.Application.Audit;
using UrbanNest.Application.Exceptions;
using UrbanNest.Application.Identity;
using UrbanNest.Application.Querying;
using UrbanNest.Domain.Identity;
using UrbanNest.Infrastructure.Audit;
using UrbanNest.Infrastructure.Identity;
using UrbanNest.Infrastructure.Persistence;
using UrbanNest.Infrastructure.Persistence.Interceptors;
using UrbanNest.Infrastructure.Querying;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace UrbanNest.IntegrationTests;

/// <summary>
/// Runnable check for Task 5.1 (Identity/RBAC entities + management services) against a relational
/// provider (SQLite), proving the behaviors the task requires:
/// <list type="bullet">
///   <item>create user / role / permission (Req 3.1, 3.2, 3.5);</item>
///   <item>assign role → scoped <c>UserRole</c>; remove role → revokes effective permissions
///   (Req 3.3, 3.4);</item>
///   <item>duplicate role name in the same scope → conflict via the DB unique index, not a
///   read-then-write pre-check (Req 3.6);</item>
///   <item>effective-permissions = union across the user's roles (Req 4.1);</item>
///   <item>soft-deleted role/user excluded from normal queries but retrievable via
///   <c>IgnoreQueryFilters</c>/<c>includeArchived</c> (Req 3.7);</item>
///   <item>foundation permission catalog seeded as editable data (Req 3.2);</item>
///   <item>mutations are audited (Req 6.1).</item>
/// </list>
/// Properties 3/4 (API-level authorization gate / export view) are sibling Tasks 5.5/5.6 and are
/// intentionally not implemented here.
/// </summary>
public sealed class IdentityRbacServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Guid _communityA = Guid.NewGuid();
    private readonly Guid _communityB = Guid.NewGuid();
    private readonly IAsyncQueryExecutor _executor = new EfAsyncQueryExecutor();

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    }

    private sealed class StubPageSizePolicy : IPageSizePolicy
    {
        public int DefaultPageSize => 50;
        public int MaxPageSize => 200;
    }

    public IdentityRbacServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var seedTenant = new TestTenantContext { IsPlatformSuperAdmin = true, UserId = "seed" };
        using var ctx = NewContext(seedTenant, new FixedClock());
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewContext(ITenantContext tenant, IClock clock)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(tenant, clock))
            .Options;
        return new AppDbContext(options, tenant);
    }

    // Super-admin context so platform-scoped entities aren't hidden by any filter and audit stamps a user.
    private TestTenantContext Admin(string user) =>
        new() { IsPlatformSuperAdmin = true, UserId = user, AuthorizedCommunityIds = [_communityA, _communityB] };

    private AuditService Audit(AppDbContext ctx, ITenantContext tenant, IClock clock) =>
        new(ctx, tenant, clock, new StubPageSizePolicy(), _executor);

    private UserService Users(AppDbContext ctx, ITenantContext tenant, IClock clock) =>
        new(ctx, Audit(ctx, tenant, clock), new StubPageSizePolicy(), _executor);

    private RoleService Roles(AppDbContext ctx, ITenantContext tenant, IClock clock) =>
        new(ctx, Audit(ctx, tenant, clock), new StubPageSizePolicy(), _executor);

    private UserRoleService Assignments(AppDbContext ctx, ITenantContext tenant, IClock clock) =>
        new(ctx, Audit(ctx, tenant, clock));

    private PermissionCatalog Catalog(AppDbContext ctx, ITenantContext tenant, IClock clock) =>
        new(ctx, Audit(ctx, tenant, clock));

    private static EffectivePermissionQuery Effective(AppDbContext ctx) => new(ctx);

    [Fact]
    public async Task Foundation_permission_catalog_is_seeded_as_data()
    {
        var clock = new FixedClock();
        var tenant = Admin("root");
        await using var ctx = NewContext(tenant, clock);

        var all = await Catalog(ctx, tenant, clock).ListAsync();

        // The seven foundation resource types from the task are present as editable rows (Req 3.2).
        var resourceTypes = all.Select(p => p.ResourceType).Distinct().ToList();
        Assert.Contains("Configuration", resourceTypes);
        Assert.Contains("MasterData", resourceTypes);
        Assert.Contains("Audit", resourceTypes);
        Assert.Contains("User", resourceTypes);
        Assert.Contains("Role", resourceTypes);
        Assert.Contains("Permission", resourceTypes);
        Assert.Contains("UserRole", resourceTypes);
    }

    [Fact]
    public async Task Register_permission_is_idempotent()
    {
        var clock = new FixedClock();
        var tenant = Admin("root");
        await using var ctx = NewContext(tenant, clock);
        var catalog = Catalog(ctx, tenant, clock);

        var first = await catalog.RegisterAsync("Unit", PermissionAction.View);
        var second = await catalog.RegisterAsync("Unit", PermissionAction.View);

        Assert.Equal(first.Id, second.Id); // same row returned, no duplicate (Req 3.2)
    }

    [Fact]
    public async Task Create_user_role_and_assign_scoped_user_role()
    {
        var clock = new FixedClock();
        var tenant = Admin("admin");
        await using var ctx = NewContext(tenant, clock);

        var user = await Users(ctx, tenant, clock).CreateAsync(new CreateUserRequest("a@x.com", null));
        var role = await Roles(ctx, tenant, clock).DefineAsync(new DefineRoleRequest(
            "Manager", RoleScope.Community, _communityA,
            [new PermissionGrant(PermissionAction.View, "User")]));

        var assignmentId = await Assignments(ctx, tenant, clock).AssignAsync(user.Id, role.Id, _communityA);

        var ur = await ctx.UserRoles.AsNoTracking().SingleAsync(x => x.Id == assignmentId);
        Assert.Equal(user.Id, ur.UserId);
        Assert.Equal(role.Id, ur.RoleId);
        Assert.Equal(_communityA, ur.CommunityId); // scoped assignment (Req 3.3)
    }

    [Fact]
    public async Task Duplicate_role_name_in_same_scope_is_a_conflict()
    {
        var clock = new FixedClock();
        var tenant = Admin("admin");
        await using var ctx = NewContext(tenant, clock);
        var roles = Roles(ctx, tenant, clock);

        await roles.DefineAsync(new DefineRoleRequest("Guard", RoleScope.Community, _communityA, []));

        // Same name, same scope (community A) → conflict from the DB unique index (Req 3.6).
        await Assert.ThrowsAsync<ConflictException>(() =>
            roles.DefineAsync(new DefineRoleRequest("Guard", RoleScope.Community, _communityA, [])));

        // Same name but a different community scope is allowed. Use a fresh context/service, as a
        // real request would — the failed insert above left its unsaved entity tracked in `ctx`.
        await using var ctx2 = NewContext(tenant, clock);
        var other = await Roles(ctx2, tenant, clock)
            .DefineAsync(new DefineRoleRequest("Guard", RoleScope.Community, _communityB, []));
        Assert.Equal("Guard", other.Name);
    }

    [Fact]
    public async Task Effective_permissions_is_the_union_across_a_users_roles()
    {
        var clock = new FixedClock();
        var tenant = Admin("admin");
        await using var ctx = NewContext(tenant, clock);

        var user = await Users(ctx, tenant, clock).CreateAsync(new CreateUserRequest("u@x.com", null));
        var roleA = await Roles(ctx, tenant, clock).DefineAsync(new DefineRoleRequest(
            "RoleA", RoleScope.Community, _communityA,
            [new PermissionGrant(PermissionAction.View, "User")]));
        var roleB = await Roles(ctx, tenant, clock).DefineAsync(new DefineRoleRequest(
            "RoleB", RoleScope.Community, _communityA,
            [new PermissionGrant(PermissionAction.Edit, "Role")]));

        await Assignments(ctx, tenant, clock).AssignAsync(user.Id, roleA.Id, _communityA);
        await Assignments(ctx, tenant, clock).AssignAsync(user.Id, roleB.Id, _communityA);

        var effective = await Effective(ctx).GetEffectivePermissionsAsync(user.Id, [_communityA]);

        Assert.Contains(effective, p => p is { ResourceType: "User", Action: PermissionAction.View });
        Assert.Contains(effective, p => p is { ResourceType: "Role", Action: PermissionAction.Edit });
        Assert.Equal(2, effective.Count); // union, de-duplicated (Req 4.1)
    }

    [Fact]
    public async Task Revoking_a_role_drops_its_effective_permissions()
    {
        var clock = new FixedClock();
        var tenant = Admin("admin");
        await using var ctx = NewContext(tenant, clock);

        var user = await Users(ctx, tenant, clock).CreateAsync(new CreateUserRequest("r@x.com", null));
        var role = await Roles(ctx, tenant, clock).DefineAsync(new DefineRoleRequest(
            "Temp", RoleScope.Community, _communityA,
            [new PermissionGrant(PermissionAction.View, "User")]));
        await Assignments(ctx, tenant, clock).AssignAsync(user.Id, role.Id, _communityA);

        var before = await Effective(ctx).GetEffectivePermissionsAsync(user.Id, [_communityA]);
        Assert.Single(before);

        await Assignments(ctx, tenant, clock).RevokeAsync(user.Id, role.Id, _communityA);

        // After revoke the role's permissions no longer apply (Req 3.4) — soft-deleted UserRole is
        // excluded from the join by the global filter.
        var after = await Effective(ctx).GetEffectivePermissionsAsync(user.Id, [_communityA]);
        Assert.Empty(after);

        // The revoked assignment row is retained for audit (soft-delete, Req 3.7).
        var archived = await ctx.UserRoles.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.UserId == user.Id && x.RoleId == role.Id);
        Assert.Equal(1, archived);
    }

    [Fact]
    public async Task Soft_deleted_role_and_user_are_excluded_from_lists_but_retrievable()
    {
        var clock = new FixedClock();
        var tenant = Admin("admin");
        await using var ctx = NewContext(tenant, clock);
        var users = Users(ctx, tenant, clock);
        var roles = Roles(ctx, tenant, clock);

        var user = await users.CreateAsync(new CreateUserRequest("d@x.com", null));
        var role = await roles.DefineAsync(new DefineRoleRequest("Gone", RoleScope.Platform, null, []));

        await users.DeleteAsync(user.Id);
        await roles.DeleteAsync(role.Id);

        // Excluded from ordinary reads (Req 3.7).
        Assert.Null(await users.GetAsync(user.Id));
        Assert.Null(await roles.GetAsync(role.Id));
        var userList = await users.ListAsync(new ListQueryParameters());
        Assert.DoesNotContain(userList.Items, u => u.Id == user.Id);

        // Still retrievable with includeArchived for audit (Req 3.7).
        var archivedUser = await users.GetAsync(user.Id, includeArchived: true);
        var archivedRole = await roles.GetAsync(role.Id, includeArchived: true);
        Assert.NotNull(archivedUser);
        Assert.True(archivedUser!.IsDeleted);
        Assert.NotNull(archivedRole);
        Assert.True(archivedRole!.IsDeleted);
    }

    [Fact]
    public async Task Defining_a_role_with_an_unknown_permission_is_a_validation_error()
    {
        var clock = new FixedClock();
        var tenant = Admin("admin");
        await using var ctx = NewContext(tenant, clock);

        await Assert.ThrowsAsync<ValidationException>(() =>
            Roles(ctx, tenant, clock).DefineAsync(new DefineRoleRequest(
                "Bad", RoleScope.Platform, null,
                [new PermissionGrant(PermissionAction.Approve, "DoesNotExist")])));
    }

    [Fact]
    public async Task Mutations_are_audited()
    {
        var clock = new FixedClock();
        var tenant = Admin("auditor");
        await using var ctx = NewContext(tenant, clock);

        var user = await Users(ctx, tenant, clock).CreateAsync(new CreateUserRequest("m@x.com", null));
        var role = await Roles(ctx, tenant, clock).DefineAsync(new DefineRoleRequest(
            "Audited", RoleScope.Community, _communityA, []));
        await Assignments(ctx, tenant, clock).AssignAsync(user.Id, role.Id, _communityA);

        var entries = await ctx.AuditLogs.AsNoTracking().ToListAsync();
        Assert.Contains(entries, e => e is { Action: "Create", ResourceType: "User" });
        Assert.Contains(entries, e => e is { Action: "Create", ResourceType: "Role" });
        Assert.Contains(entries, e => e is { Action: "Assign", ResourceType: "UserRole" });
        Assert.All(entries, e => Assert.Equal("auditor", e.ActingUserId)); // actor from context (Req 6.1)
    }

    [Fact]
    public async Task Duplicate_user_email_is_a_conflict()
    {
        var clock = new FixedClock();
        var tenant = Admin("admin");
        await using var ctx = NewContext(tenant, clock);
        var users = Users(ctx, tenant, clock);

        await users.CreateAsync(new CreateUserRequest("dup@x.com", null));
        await Assert.ThrowsAsync<ConflictException>(() =>
            users.CreateAsync(new CreateUserRequest("dup@x.com", null)));
    }
}
