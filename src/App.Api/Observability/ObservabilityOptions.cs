using System.ComponentModel.DataAnnotations;

namespace App.Api.Observability;

/// <summary>
/// Config-driven observability policy (bound from the "Observability" section). Nothing here is
/// hardcoded: whether logs are emitted as JSON, the extra sensitive keys to redact, and the health
/// endpoint paths all come from configuration. Log <i>levels</i> remain governed by the standard
/// "Logging:LogLevel" section (so they are configurable too).
/// </summary>
public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary>
    /// Emit structured JSON console logs when true (operators/aggregators). When false the default
    /// human-readable console is used (handy locally). Either way the redaction decorator still runs.
    /// </summary>
    public bool UseJsonConsole { get; set; } = true;

    /// <summary>
    /// Field/key names whose values must never appear in logs (Req 10.4). The baseline below covers
    /// passwords, OTP values, session tokens and auth material; a deployment can append more via
    /// configuration without a code change. Matching is case-insensitive substring, so "token" also
    /// catches "sessionToken", "refreshToken", etc.
    /// </summary>
    [MinLength(1)]
    public string[] SensitiveKeys { get; set; } =
    [
        "password", "pwd", "passwd",
        "otp", "otpcode", "otp_value",
        "token", "accesstoken", "refreshtoken", "sessiontoken",
        "authorization", "bearer",
        "apikey", "api-key", "api_key",
        "secret", "signingkey", "clientsecret",
    ];

    /// <summary>Liveness path (app is up). Must be reachable without touching the database.</summary>
    public string LivenessPath { get; set; } = "/health/live";

    /// <summary>Readiness path (app + database connection reachable).</summary>
    public string ReadinessPath { get; set; } = "/health/ready";

    /// <summary>Aggregate path (reports every check — app + DB).</summary>
    public string HealthPath { get; set; } = "/health";
}
