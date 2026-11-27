using App.Api.Security;
using App.Application.Config;
using App.Contracts.Envelope;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

/// <summary>
/// Public (no login) version check for the mobile app. The app calls this on start / resume and
/// compares its own versionCode with these values:
///   versionCode &lt; minVersionCode    → blocking "Update required" screen
///   versionCode &lt; latestVersionCode → dismissible "Update available" prompt
/// Values live in the Configuration table (global scope), editable from admin → Config:
///   app.{platform}.latestVersionCode, app.{platform}.latestVersionName, app.{platform}.minVersionCode,
///   app.{platform}.updateUrl, app.{platform}.updateMessage
/// </summary>
[ApiController]
[Route("api/v1/app-version")]
[EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
public sealed class AppVersionController(IConfigService config) : ControllerBase
{
    private static readonly HashSet<string> Platforms = new(StringComparer.OrdinalIgnoreCase) { "android", "ios" };

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string platform, CancellationToken ct)
    {
        var info = await ReadAsync(config, platform, ct);
        return Ok(ApiResult<AppVersionInfo>.Ok(info, string.Empty));
    }

    /// <summary>Reads the version settings; shared with GET /auth/me?include=bootstrap.</summary>
    public static async Task<AppVersionInfo> ReadAsync(IConfigService config, string? platform, CancellationToken ct)
    {
        var p = (platform ?? string.Empty).Trim().ToLowerInvariant();
        if (!Platforms.Contains(p))
        {
            p = "android";
        }

        var prefix = $"app.{p}.";
        return new AppVersionInfo(
            Platform: p,
            LatestVersionCode: await config.GetIntAsync(prefix + "latestVersionCode", 0, null, ct),
            LatestVersionName: await config.GetStringAsync(prefix + "latestVersionName", string.Empty, null, ct),
            MinVersionCode: await config.GetIntAsync(prefix + "minVersionCode", 0, null, ct),
            UpdateUrl: await config.GetStringAsync(prefix + "updateUrl", string.Empty, null, ct),
            UpdateMessage: await config.GetStringAsync(prefix + "updateMessage", string.Empty, null, ct));
    }

    public sealed record AppVersionInfo(
        string Platform,
        int LatestVersionCode,
        string LatestVersionName,
        int MinVersionCode,
        string UpdateUrl,
        string UpdateMessage);
}
