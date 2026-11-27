namespace App.Api.Security;

/// <summary>
/// Server-side RBAC switch bound from the <c>Security</c> configuration section
/// (<c>Security:EnforcePermissions</c>, env var <c>Security__EnforcePermissions</c>).
/// <list type="bullet">
/// <item><b>true</b> — every <see cref="RequirePermissionAttribute"/> endpoint is checked against the
/// caller's Role→Permission grants; a missing grant returns 403 FORBIDDEN.</item>
/// <item><b>false</b> — audit only: the check still runs and a would-be denial is logged as a warning
/// ("[RBAC audit] would deny …"), but the request is let through. Use it to confirm real grants
/// before turning enforcement on.</item>
/// </list>
/// When the key is not set: enforced everywhere except the Development environment.
/// The effective value is logged once at startup. See docs/PERMISSIONS.md.
/// </summary>
public sealed class PermissionEnforcementOptions
{
    public const string SectionName = "Security";

    public bool EnforcePermissions { get; set; } = true;

    /// <summary>Reads the switch; an unset key means "enforce unless Development".</summary>
    public static PermissionEnforcementOptions FromConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        var raw = configuration.GetValue<bool?>($"{SectionName}:{nameof(EnforcePermissions)}");
        return new PermissionEnforcementOptions { EnforcePermissions = raw ?? !environment.IsDevelopment() };
    }
}
