using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using App.Application.Abstractions;
using App.Application.Abstractions.Security;
using App.Application.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace App.Infrastructure.Auth;

// =====================================================================================
// Everything about the JWT login token lives in this one file:
//   1. JwtSettings     - the "Jwt" section of appsettings.json
//   2. JwtTokenIssuer  - CREATES the token at login / refresh
//   3. ValidationRules - how the API CHECKS the token (used by App.Api/Security/AuthSetup.cs)
//
// The claims written here are read back by CurrentUser (same ClaimNames).
// =====================================================================================

/// <summary>"Jwt" section of appsettings.json. The signing key comes from env / user-secrets, never source.</summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    [Required(AllowEmptyStrings = false)]
    public string Issuer { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string Audience { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [MinLength(32, ErrorMessage = "The JWT signing key must be at least 32 characters.")]
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>Allowed clock difference (seconds) when checking expiry.</summary>
    [Range(0, 300)]
    public int ClockSkewSeconds { get; init; } = 30;

    public SymmetricSecurityKey Key() => new(Encoding.UTF8.GetBytes(SigningKey));

    /// <summary>The rules the API uses to accept a token. Same settings as issuing, so they always match.</summary>
    public TokenValidationParameters ValidationRules() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = string.IsNullOrEmpty(SigningKey) ? null : Key(),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(ClockSkewSeconds),
        NameClaimType = ClaimNames.UserId,
    };
}

/// <summary>What goes into the token: the logged-in person's details resolved by AuthService.</summary>
public sealed record TokenIssueRequest(
    Guid UserId,
    Guid? CommunityId,
    bool IsPlatformSuperAdmin,
    IReadOnlyCollection<string> RoleNames,
    bool IsResidentSelfScoped = false,
    Guid? ResidentUnitId = null,
    Guid? HouseholdId = null,
    IReadOnlyCollection<string>? Permissions = null,
    Guid? OrganizationId = null,
    Guid? ResidentId = null,
    string? ResidentType = null);

/// <summary>The signed token. <see cref="TokenId"/> (jti) is saved in LoginSessions so logout can revoke it.</summary>
public sealed record IssuedToken(string AccessToken, string TokenId, DateTime IssuedAtUtc, DateTime ExpiresAtUtc);

/// <summary>Creates the signed login token (HMAC-SHA256).</summary>
public sealed class JwtTokenIssuer(IOptions<JwtSettings> jwt, IOptions<AuthOptions> auth, IClock clock)
{
    public IssuedToken Issue(TokenIssueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = jwt.Value;
        var issuedAt = clock.UtcNow;
        var expiresAt = issuedAt.AddMinutes(auth.Value.TokenLifetimeMinutes);
        var tokenId = Guid.NewGuid().ToString("N");

        var claims = new List<Claim>
        {
            new(ClaimNames.UserId, request.UserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, tokenId),
            new(ClaimNames.IsSuperAdmin, request.IsPlatformSuperAdmin ? "true" : "false"),
            new(ClaimNames.IsResident, request.IsResidentSelfScoped ? "true" : "false"),
        };

        Add(claims, ClaimNames.CommunityId, request.CommunityId); // single default community
        claims.AddRange(request.RoleNames.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange((request.Permissions ?? []).Select(p => new Claim(ClaimNames.Permission, p)));

        Add(claims, ClaimNames.OrganizationId, request.OrganizationId);
        Add(claims, ClaimNames.ResidentId, request.ResidentId);
        if (!string.IsNullOrWhiteSpace(request.ResidentType))
        {
            claims.Add(new Claim(ClaimNames.ResidentType, request.ResidentType));
        }

        // Unit / household only for residents limited to their own unit.
        if (request.IsResidentSelfScoped)
        {
            Add(claims, ClaimNames.UnitId, request.ResidentUnitId);
            Add(claims, ClaimNames.HouseholdId, request.HouseholdId);
        }

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            notBefore: issuedAt,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(settings.Key(), SecurityAlgorithms.HmacSha256));

        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), tokenId, issuedAt, expiresAt);
    }

    private static void Add(List<Claim> claims, string type, Guid? value)
    {
        if (value is { } v)
        {
            claims.Add(new Claim(type, v.ToString()));
        }
    }
}
