using System.Security.Claims;

namespace App.Application.Abstractions.Security;

/// <summary>
/// The logged-in person for the current request. This is the ONE place to get login details:
/// inject <see cref="CurrentUser"/> into any controller or service.
///
/// <code>
/// public sealed class MyService(AppDbContext db, CurrentUser currentUser)
/// {
///     var userId      = currentUser.RequireUserId();
///     var communityId = currentUser.CommunityId;   // single community
///     var unitId      = currentUser.UnitId;        // single unit (residents)
///     var type        = currentUser.ResidentType;  // owner / tenant / security ...
///     if (currentUser.IsInRole("CommunityAdmin")) { ... }
/// }
/// </code>
///
/// Values come only from the signed JWT issued at login (never from the request body, query or
/// headers), so the client cannot spoof them. The global query filters in AppDbContext also read
/// this class to keep every query inside the user's communities (and own unit for residents).
/// </summary>
public sealed class CurrentUser
{
    /// <summary>Not logged in. Sees no community data (fail-closed).</summary>
    public static CurrentUser Anonymous => new();

    /// <summary>Background jobs (outbox, SLA, expiry workers): cross-community, stamped as "system".</summary>
    public static CurrentUser System => new() { IsSuperAdmin = true, Actor = "system:worker" };

    private readonly Func<ClaimsPrincipal?>? _principalSource;
    private bool _loaded;

    private Guid? _userId;
    private string? _actor;
    private Guid? _communityId;
    private IReadOnlyCollection<Guid> _organizationCommunityIds = [];
    private Guid? _unitId;
    private Guid? _householdId;
    private Guid? _organizationId;
    private Guid? _residentId;
    private string? _residentType;
    private IReadOnlyList<string> _roles = [];
    private IReadOnlyList<string> _permissions = [];
    private bool _isSuperAdmin;
    private bool _isResident;

    /// <summary>Fixed values (tests, background jobs): <c>new CurrentUser { UserId = ..., CommunityId = ... }</c>.</summary>
    public CurrentUser() => _loaded = true;

    /// <summary>
    /// Reads the token claims lazily. The principal is attached to the request only AFTER JWT
    /// validation, but the DbContext (and so this object) is created during that validation, so
    /// the claims must be read on first use, not at construction.
    /// </summary>
    public CurrentUser(Func<ClaimsPrincipal?> principalSource) => _principalSource = principalSource;

    /// <summary>Build directly from a principal (e.g. <c>CurrentUser.From(User)</c> in a controller).</summary>
    public static CurrentUser From(ClaimsPrincipal? principal) => new(() => principal);

    // ---------------------------------------------------------------- login details

    /// <summary>True when the request carries a valid token.</summary>
    public bool IsAuthenticated => UserId is not null;

    /// <summary>Logged-in user id. Null when anonymous.</summary>
    public Guid? UserId { get { Load(); return _userId; } init => _userId = value; }

    /// <summary>Who to stamp in audit columns (CreatedBy / ActingUserId): the user id, or "system:..." for jobs.</summary>
    public string? Actor { get { Load(); return _actor ?? _userId?.ToString(); } init => _actor = value; }

    /// <summary>The user's single default community (Users.CommunityId). Use this in your code.</summary>
    public Guid? CommunityId { get { Load(); return _communityId; } init => _communityId = value; }

    /// <summary>
    /// Communities this user can SEE, decided by role (nothing extra is stored for the user):
    /// <list type="bullet">
    ///   <item>Super admin: all (check <see cref="IsSuperAdmin"/>; this list is not used).</item>
    ///   <item>Organization admin: every community of <see cref="OrganizationId"/> (e.g. Apna Ghar wing A + wing B),
    ///         loaded fresh per request by AuthSetup.</item>
    ///   <item>Everyone else: only <see cref="CommunityId"/>.</item>
    /// </list>
    /// Used by the data filters in AppDbContext and by <see cref="CanAccessCommunity"/>.
    /// </summary>
    public IReadOnlyCollection<Guid> AccessibleCommunityIds
    {
        get
        {
            Load();
            var own = _communityId is { } c ? new[] { c } : [];
            return IsOrganizationAdmin ? own.Union(_organizationCommunityIds).ToArray() : own;
        }
    }

    /// <summary>Organization's communities for an organization admin (set per request; tests may init it).</summary>
    public IReadOnlyCollection<Guid> OrganizationCommunityIds { get => _organizationCommunityIds; init => _organizationCommunityIds = value; }

    /// <summary>Called once per request by AuthSetup for organization admins. Not stored anywhere.</summary>
    public void UseOrganizationCommunities(IReadOnlyCollection<Guid> communityIds) => _organizationCommunityIds = communityIds;

    /// <summary>The resident's single (own) unit. Null for admins / staff.</summary>
    public Guid? UnitId { get { Load(); return _unitId; } init => _unitId = value; }

    /// <summary>The resident's household. Null when not set.</summary>
    public Guid? HouseholdId { get { Load(); return _householdId; } init => _householdId = value; }

    /// <summary>Organization of the user's community.</summary>
    public Guid? OrganizationId { get { Load(); return _organizationId; } init => _organizationId = value; }

    /// <summary>The user's Resident record id (residents, tenants, security staff). Null for admins.</summary>
    public Guid? ResidentId { get { Load(); return _residentId; } init => _residentId = value; }

    /// <summary>Resident type code: owner / tenant / security ... Null when not a resident.</summary>
    public string? ResidentType { get { Load(); return _residentType; } init => _residentType = value; }

    /// <summary>The first role name (convenience). Use <see cref="Roles"/> / <see cref="IsInRole"/> for several.</summary>
    public string? Role => Roles.Count > 0 ? Roles[0] : null;

    /// <summary>All role names (SuperAdmin, CommunityAdmin, SocietyAdmin, Security, Resident, ...).</summary>
    public IReadOnlyList<string> Roles { get { Load(); return _roles; } init => _roles = value; }

    /// <summary>Granted permissions as "ResourceType:actionInt".</summary>
    public IReadOnlyList<string> Permissions { get { Load(); return _permissions; } init => _permissions = value; }

    /// <summary>Platform super admin: sees all communities.</summary>
    public bool IsSuperAdmin { get { Load(); return _isSuperAdmin; } init => _isSuperAdmin = value; }

    /// <summary>Resident limited to their own unit (data filters add UnitId == <see cref="UnitId"/>).</summary>
    public bool IsResident { get { Load(); return _isResident; } init => _isResident = value; }

    // ---------------------------------------------------------------- helpers

    public bool IsInRole(string role) =>
        Roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));

    public bool IsResidentType(string residentType) =>
        string.Equals(ResidentType, residentType, StringComparison.OrdinalIgnoreCase);

    public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.Ordinal);

    /// <summary>Role name that unlocks every community of the organization.</summary>
    public const string OrganizationAdminRole = "OrganizationAdmin";

    /// <summary>Organization admin: sees all communities of their organization.</summary>
    public bool IsOrganizationAdmin => !IsSuperAdmin && IsInRole(OrganizationAdminRole);

    /// <summary>Super admin, or the community is one this user can see by role.</summary>
    public bool CanAccessCommunity(Guid communityId) => IsSuperAdmin || AccessibleCommunityIds.Contains(communityId);

    public Guid RequireUserId() =>
        UserId ?? throw new UnauthorizedAccessException("You must be signed in.");

    public Guid RequireCommunityId() =>
        CommunityId ?? throw new InvalidOperationException("The current user is not linked to a community.");

    public Guid RequireUnitId() =>
        UnitId ?? throw new InvalidOperationException("The current user is not linked to a unit.");

    // ---------------------------------------------------------------- claims -> values

    private void Load()
    {
        if (_loaded)
        {
            return;
        }

        var principal = _principalSource?.Invoke();
        if (principal?.Identity?.IsAuthenticated != true)
        {
            // Not authenticated (yet). Stay empty and re-check on next access: during JWT
            // validation the principal is not attached to the request yet.
            return;
        }

        _loaded = true;
        _userId = ReadGuid(principal, ClaimNames.UserId) ?? ReadGuid(principal, ClaimTypes.NameIdentifier);
        // Single default community. (Older tokens may still carry several; the first is the default.)
        _communityId = principal.FindAll(ClaimNames.CommunityId)
            .Select(c => Guid.TryParse(c.Value, out var id) ? id : (Guid?)null)
            .FirstOrDefault(id => id.HasValue);
        _unitId = ReadGuid(principal, ClaimNames.UnitId);
        _householdId = ReadGuid(principal, ClaimNames.HouseholdId);
        _organizationId = ReadGuid(principal, ClaimNames.OrganizationId);
        _residentId = ReadGuid(principal, ClaimNames.ResidentId);
        _residentType = principal.FindFirst(ClaimNames.ResidentType)?.Value;
        _roles = principal.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _permissions = principal.FindAll(ClaimNames.Permission).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList();
        _isSuperAdmin = ReadBool(principal, ClaimNames.IsSuperAdmin);
        _isResident = ReadBool(principal, ClaimNames.IsResident);
    }

    private static Guid? ReadGuid(ClaimsPrincipal principal, string type) =>
        Guid.TryParse(principal.FindFirst(type)?.Value, out var id) ? id : null;

    private static bool ReadBool(ClaimsPrincipal principal, string type) =>
        bool.TryParse(principal.FindFirst(type)?.Value, out var value) && value;
}

/// <summary>
/// JWT claim names. Written at login by JwtTokenIssuer (App.Infrastructure/Auth/JwtTokens.cs), read back by <see cref="CurrentUser"/>.
/// The string values are unchanged, so tokens issued before this change still work.
/// </summary>
public static class ClaimNames
{
    public const string UserId = "sub";
    public const string CommunityId = "communityos:community_id";
    public const string IsSuperAdmin = "communityos:is_platform_super_admin";
    public const string IsResident = "communityos:is_resident_self_scoped";
    public const string UnitId = "communityos:resident_unit_id";
    public const string HouseholdId = "communityos:household_id";
    public const string Permission = "communityos:perm";
    public const string OrganizationId = "communityos:organization_id";
    public const string ResidentId = "communityos:resident_id";
    public const string ResidentType = "communityos:resident_type";
}
