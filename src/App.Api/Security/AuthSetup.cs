using System.IdentityModel.Tokens.Jwt;
using App.Application.Abstractions.Security;
using App.Infrastructure.Auth;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace App.Api.Security;

/// <summary>
/// API login setup, called once from Program.cs:
///   1. Check the JWT on every request (signature, issuer, audience, expiry).
///   2. Reject the token if its session was logged out (LoginSessions.RevokedAtUtc is set).
///   3. Organization admins: load their organization's communities (access by role, not stored).
///   4. Register <see cref="CurrentUser"/> so controllers/services get the logged-in person.
/// Token creation lives in App.Infrastructure/Auth/JwtTokens.cs.
/// </summary>
public static class AuthSetup
{
    public static IServiceCollection AddJwtLogin(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddAuthorization();

        // Configure the JWT check from the "Jwt" settings (bound in DependencyInjection).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtSettings>>((options, jwt) =>
            {
                options.MapInboundClaims = false; // keep claim names exactly as issued (sub, communityos:*)
                options.TokenValidationParameters = jwt.Value.ValidationRules();
                options.Events = new JwtBearerEvents { OnTokenValidated = RejectLoggedOutSessionAsync };
            });

        // The logged-in person for this request (reads the token claims on first use).
        services.AddHttpContextAccessor();
        services.RemoveAll<CurrentUser>();
        services.AddScoped(sp =>
        {
            var http = sp.GetRequiredService<IHttpContextAccessor>();
            return new CurrentUser(() => http.HttpContext?.User);
        });

        return services;
    }

    /// <summary>A valid token still fails if logout revoked its session (one indexed DB read).</summary>
    private static async Task RejectLoggedOutSessionAsync(TokenValidatedContext context)
    {
        var tokenId = context.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (string.IsNullOrEmpty(tokenId))
        {
            context.Fail("Token is missing a session identifier.");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var active = await db.LoginSessions.AsNoTracking()
            .AnyAsync(s => s.TokenId == tokenId && s.RevokedAtUtc == null, context.HttpContext.RequestAborted);
        if (!active)
        {
            context.Fail("Session has been revoked.");
            return;
        }

        // Organization admin: can see every community of their organization (e.g. wing A + wing B).
        // Decided by role and read fresh here per request, so nothing extra is stored for the user.
        var principal = context.Principal!;
        var isSuperAdmin = principal.FindFirst(ClaimNames.IsSuperAdmin)?.Value == "true";
        var isOrgAdmin = principal.FindAll(System.Security.Claims.ClaimTypes.Role)
            .Any(r => string.Equals(r.Value, CurrentUser.OrganizationAdminRole, StringComparison.OrdinalIgnoreCase));
        if (!isSuperAdmin && isOrgAdmin
            && Guid.TryParse(principal.FindFirst(ClaimNames.OrganizationId)?.Value, out var organizationId))
        {
            var orgCommunityIds = await db.Communities.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.OrganizationId == organizationId && !c.IsDeleted)
                .Select(c => c.Id)
                .ToListAsync(context.HttpContext.RequestAborted);

            context.HttpContext.RequestServices.GetRequiredService<CurrentUser>()
                .UseOrganizationCommunities(orgCommunityIds);
        }
    }
}
