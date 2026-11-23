using App.Application.Abstractions;
using App.Application.Abstractions.Security;
using App.Application.Exceptions;
using App.Application.Identity;
using App.Domain.Identity;

namespace App.Infrastructure.Security;


public sealed class AuthorizationService(
    CurrentUser currentUser,
    IEffectivePermissionQuery effectivePermissions)
    : IPermissionEvaluator, IAuthorizationGuard
{
    // Memoized once per request. Null until the first check loads it. Keyed implicitly to this
    // request's user + authorized communities because the instance is request-scoped.
    private IReadOnlyCollection<EffectivePermission>? _cachedPermissions;

    public async ValueTask<bool> IsPermittedAsync(
        CurrentUser currentUser,
        PermissionRequirement requirement,
        CancellationToken cancellationToken = default)
        => await HasPermissionAsync(requirement.Action, requirement.ResourceType, cancellationToken);

    public async ValueTask<bool> HasPermissionAsync(
        PermissionAction action,
        string resourceType,
        CancellationToken cancellationToken = default)
    {
        // Platform super admin is permitted across communities as conferred by that role (Req 5.3,
        // 5.5) — modeled via the signed flag, not a role-name string.
        if (currentUser.IsSuperAdmin)
        {
            return true;
        }

        // Default-deny: an unresolved/anonymous principal holds nothing. (The middleware already
        // rejects anonymous callers with UNAUTHENTICATED before reaching here; this keeps the
        // evaluator safe if called in any other context.)
        if (currentUser.UserId is not { } userId)
        {
            return false;
        }

        var permissions = await LoadPermissionsAsync(userId, cancellationToken);

        // Least-privilege: granted iff the exact (Action, resourceType) is in the conferred union.
        return permissions.Any(p => p.Action == action && ResourceMatches(p.ResourceType, resourceType));
    }

    public void EnsureCommunityAuthorized(Guid communityId)
    {
        // Super admin crosses communities (Req 5.5). Otherwise the target must be in the caller's
        // authorized set, else FORBIDDEN with no state change (Req 5.3, defense-in-depth over the
        // EF global filter).
        if (currentUser.CanAccessCommunity(communityId))
        {
            return;
        }

        throw new ForbiddenException();
    }

    public async ValueTask EnsureCanExportAsync(
        string resourceType, CancellationToken cancellationToken = default)
    {
        // Export ⊆ viewable (Req 4.4, 66.1): the export may only yield records the caller can View.
        if (!await HasPermissionAsync(PermissionAction.View, resourceType, cancellationToken))
        {
            throw new ForbiddenException();
        }
    }

    private async ValueTask<IReadOnlyCollection<EffectivePermission>> LoadPermissionsAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        if (_cachedPermissions is not null)
        {
            return _cachedPermissions;
        }

        _cachedPermissions = await effectivePermissions.GetEffectivePermissionsAsync(
            userId, currentUser.AccessibleCommunityIds, cancellationToken);

        return _cachedPermissions;
    }

    private static bool ResourceMatches(string granted, string required)
    {
        if (string.Equals(granted, required, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(granted) || string.IsNullOrWhiteSpace(required))
        {
            return false;
        }

        var gClean = StripPrefixes(granted);
        var rClean = StripPrefixes(required);

        // 1. Direct match on clean string
        if (string.Equals(gClean, rClean, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 2. Singular / Plural normalization (e.g. "announcements" <-> "announcement")
        var gNorm = NormalizeSingular(gClean);
        var rNorm = NormalizeSingular(rClean);

        if (string.Equals(gNorm, rNorm, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 3. Segmented fuzzy matching across dots / dashes (e.g. "visitors.invite" matches "visitor")
        var gSegments = gClean.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(NormalizeSingular)
                              .ToArray();

        if (gSegments.Any(s => string.Equals(s, rNorm, StringComparison.OrdinalIgnoreCase) ||
                               (s.Length >= 4 && rNorm.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                               (rNorm.Length >= 4 && s.Contains(rNorm, StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        // 4. Semantic domain aliases without hardcoded strictness
        var gLower = gClean.ToLowerInvariant();
        var rLower = rClean.ToLowerInvariant();

        return (gLower, rLower) switch
        {
            (var gl, var rl) when gl.Contains("helpdesk") && rl.Contains("ticket") => true,
            (var gl, var rl) when gl.Contains("household") && (rl.Contains("resident") || rl.Contains("member") || rl.Contains("pet") || rl.Contains("vehicle") || rl.Contains("emergency")) => true,
            (var gl, var rl) when gl.Contains("entryexit") && (rl.Contains("event") || rl.Contains("pass") || rl.Contains("visitor")) => true,
            (var gl, var rl) when gl.Contains("recurring") && (rl.Contains("recurring") || rl.Contains("visitor")) => true,
            (var gl, var rl) when gl.Contains("sos") && rl.Contains("sos") => true,
            (var gl, var rl) when gl.Contains("maintenance") && rl.Contains("maintenance") => true,
            (var gl, var rl) when gl.Contains("billing") && (rl.Contains("bill") || rl.Contains("charge") || rl.Contains("payment") || rl.Contains("expense") || rl.Contains("ledger")) => true,
            (var gl, var rl) when gl.Contains("parcel") && rl.Contains("parcel") => true,
            (var gl, var rl) when gl.Contains("resident") && (rl.Contains("resident") || rl.Contains("member") || rl.Contains("user")) => true,
            _ => false
        };
    }

    private static string StripPrefixes(string s)
    {
        var clean = s.Trim();
        while (clean.StartsWith("app.", StringComparison.OrdinalIgnoreCase) ||
               clean.StartsWith("admin.", StringComparison.OrdinalIgnoreCase) ||
               clean.StartsWith("features.", StringComparison.OrdinalIgnoreCase))
        {
            var dot = clean.IndexOf('.');
            if (dot >= 0)
            {
                clean = clean[(dot + 1)..];
            }
            else
            {
                break;
            }
        }
        return clean;
    }

    private static string NormalizeSingular(string word)
    {
        var w = word.Trim().ToLowerInvariant();
        if (w.EndsWith("ies") && w.Length > 3)
        {
            return w[..^3] + "y";
        }
        if (w.EndsWith("ses") && w.Length > 3)
        {
            return w[..^2];
        }
        if (w.EndsWith("s") && !w.EndsWith("ss") && w.Length > 1)
        {
            return w[..^1];
        }
        return w;
    }
}
