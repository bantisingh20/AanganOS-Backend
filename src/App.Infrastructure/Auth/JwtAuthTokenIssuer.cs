using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using App.Application.Abstractions;
using App.Application.Abstractions.Security;
using App.Application.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace App.Infrastructure.Auth;

/// <summary>
/// <see cref="IAuthTokenIssuer"/> that signs a JWT carrying exactly the Task 2.2 claim contract
/// (<see cref="CommunityClaimTypes"/>). This is the issuance counterpart to the pipeline's bearer
/// validation: it emits <c>sub</c>, one <c>communityos:community_id</c> per authorized community,
/// the super-admin / resident self-scope flags, the user's role names, and a <c>jti</c> used as the
/// <c>LoginSession.TokenId</c> so logout can revoke the token (Req 1.1, 1.3).
///
/// <para>HMAC-SHA256 over the shared signing key; issuer/audience/key come from
/// <see cref="JwtIssuerOptions"/> (same <c>Jwt</c> section as validation) and the lifetime from
/// <see cref="AuthOptions"/> (config-driven, no hardcoding).</para>
/// </summary>
public sealed class JwtAuthTokenIssuer(
    IOptions<JwtIssuerOptions> jwtOptions,
    IOptions<AuthOptions> authOptions,
    IClock clock) : IAuthTokenIssuer
{
    private readonly JwtIssuerOptions _jwt = jwtOptions.Value;
    private readonly AuthOptions _auth = authOptions.Value;

    public IssuedToken Issue(TokenIssueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issuedAt = clock.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_auth.TokenLifetimeMinutes);
        var tokenId = Guid.NewGuid().ToString("N");

        var claims = new List<Claim>
        {
            new(CommunityClaimTypes.UserId, request.UserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, tokenId),
            new(CommunityClaimTypes.IsPlatformSuperAdmin, request.IsPlatformSuperAdmin ? "true" : "false"),
            new(CommunityClaimTypes.IsResidentSelfScoped, request.IsResidentSelfScoped ? "true" : "false"),
        };

        // One repeated community claim per authorized community (what ClaimsTenantContextMapper reads).
        foreach (var communityId in request.AuthorizedCommunityIds)
        {
            claims.Add(new Claim(CommunityClaimTypes.CommunityId, communityId.ToString()));
        }

        // The user's assigned roles travel as role claims (Req 1.1 returns roles with the token).
        foreach (var role in request.RoleNames)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        // The user's granted permissions travel as repeated "ResourceType:actionInt" claims so the
        // mobile app can show/hide features by permission (not just role).
        if (request.Permissions is { } permissions)
        {
            foreach (var perm in permissions)
            {
                claims.Add(new Claim(CommunityClaimTypes.Permission, perm));
            }
        }

        if (request.IsResidentSelfScoped && request.ResidentUnitId is { } unitId)
        {
            claims.Add(new Claim(CommunityClaimTypes.ResidentUnitId, unitId.ToString()));
        }

        if (request.IsResidentSelfScoped && request.HouseholdId is { } householdId)
        {
            claims.Add(new Claim(CommunityClaimTypes.HouseholdId, householdId.ToString()));
        }

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            notBefore: issuedAt,
            expires: expiresAt,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        return new IssuedToken(accessToken, tokenId, issuedAt, expiresAt);
    }
}
