using System.ComponentModel.DataAnnotations;

namespace CommunityOS.Api.Security;

/// <summary>
/// JWT bearer validation options bound from the <c>Jwt</c> configuration section (options
/// pattern — nothing hardcoded). The signing key, issuer and audience come from configuration
/// (appsettings / user-secrets / environment), never from source. A real secret lives only in
/// the deployment environment; appsettings.Development.json carries a non-production placeholder.
///
/// This is token VALIDATION config for Task 2.2. Token ISSUANCE (Auth_Service, Task 5.2) will
/// bind the same section so issuer/audience/key agree on both sides.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Expected token issuer (<c>iss</c>). Required.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Issuer { get; init; } = string.Empty;

    /// <summary>Expected token audience (<c>aud</c>). Required.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Audience { get; init; } = string.Empty;

    /// <summary>Symmetric signing key (HMAC-SHA256). Required; keep out of source control.</summary>
    [Required(AllowEmptyStrings = false)]
    [MinLength(32, ErrorMessage = "The JWT signing key must be at least 32 characters.")]
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>Allowed clock skew (seconds) when validating token lifetime. Default 30s.</summary>
    [Range(0, 300)]
    public int ClockSkewSeconds { get; init; } = 30;
}
