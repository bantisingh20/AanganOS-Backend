using System.ComponentModel.DataAnnotations;

namespace CommunityOS.Infrastructure.Auth;

/// <summary>
/// Token <b>issuance</b> settings, bound from the SAME <c>Jwt</c> configuration section the API's
/// JwtOptions binds for <b>validation</b> (Task 2.2). Binding one section on both sides is what
/// guarantees the issuer/audience/signing-key agree, so a token this service signs validates in the
/// request pipeline. Lives in Infrastructure (where issuance happens) to keep the layering clean —
/// Infrastructure must not reference the Api project — rather than being shared from Api.
///
/// <para><b>Security.</b> The signing key never lives in source; it comes from configuration /
/// user-secrets / environment, exactly like the validation side.</para>
/// </summary>
public sealed class JwtIssuerOptions
{
    public const string SectionName = "Jwt";

    [Required(AllowEmptyStrings = false)]
    public string Issuer { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string Audience { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [MinLength(32, ErrorMessage = "The JWT signing key must be at least 32 characters.")]
    public string SigningKey { get; init; } = string.Empty;
}
