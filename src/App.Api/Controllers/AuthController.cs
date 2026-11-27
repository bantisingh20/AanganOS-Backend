using App.Application.Abstractions.Security;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using App.Api.Security;
using App.Application.Abstractions;
using App.Application.Auth;
using App.Application.Exceptions;
using App.Contracts.Envelope;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
public sealed class AuthController(IAuthService auth, CurrentUser currentUser) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginBody body, CancellationToken ct)
    {
        var result = await auth.LoginAsync(
            new LoginRequest(body.Identifier, body.Password, body.OneSignalPlayerId, body.Platform), ct);
        var response = new LoginResponse(result.AccessToken, result.ExpiresAtUtc, result.Roles, result.RefreshToken, result.User);
        return Ok(ApiResult<LoginResponse>.Ok(response, string.Empty));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshBody body, CancellationToken ct)
    {
        var result = await auth.RefreshAsync(new RefreshRequest(body.RefreshToken), ct);
        var response = new LoginResponse(result.AccessToken, result.ExpiresAtUtc, result.Roles, result.RefreshToken, result.User);
        return Ok(ApiResult<LoginResponse>.Ok(response, string.Empty));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (HttpContext.User.Identity?.IsAuthenticated != true)
        {
            throw new UnauthenticatedException();
        }

        var tokenId = HttpContext.User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (string.IsNullOrEmpty(tokenId))
        {
            throw new UnauthenticatedException();
        }

        await auth.LogoutAsync(tokenId, ct);
        return Ok(ApiResult<LogoutResponse>.Ok(new LogoutResponse(true), string.Empty));
    }

    [HttpPost("password/forgot")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordBody body, CancellationToken ct)
    {
        await auth.RequestPasswordResetAsync(new PasswordResetRequest(body.Identifier), ct);
        return Ok(ApiResult<ForgotPasswordResponse>.Ok(
            new ForgotPasswordResponse("If the identifier is registered, a one-time code has been sent."),
            string.Empty));
    }

    [HttpPost("password/verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpBody body, CancellationToken ct)
    {
        var valid = await auth.VerifyPasswordResetOtpAsync(new PasswordResetOtpCheck(body.Identifier, body.Code), ct);
        return Ok(ApiResult<VerifyOtpResponse>.Ok(new VerifyOtpResponse(valid), string.Empty));
    }

    [HttpPost("password/reset")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordBody body, CancellationToken ct)
    {
        await auth.ResetPasswordAsync(new PasswordResetConfirm(body.Identifier, body.Code, body.NewPassword), ct);
        return Ok(ApiResult<ResetPasswordResponse>.Ok(new ResetPasswordResponse(true), string.Empty));
    }

    [HttpPost("password/change")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordBody body, CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthenticatedException();
        }

        await auth.ChangePasswordAsync(userId, new ChangePasswordRequest(body.CurrentPassword ?? string.Empty, body.NewPassword ?? string.Empty), ct);
        return Ok(ApiResult<ChangePasswordResponse>.Ok(new ChangePasswordResponse(true), string.Empty));
    }

    /// <summary>
    /// The logged-in person's details, straight from <see cref="CurrentUser"/>.
    /// With <c>?include=bootstrap</c> it also returns, in ONE call, what both apps need right after
    /// login / on app start: menu, unread count, task summary, resident profile, app version and the
    /// requested master-data lists (<c>&amp;masterData=Ticket_Status,Ticket_Category</c>, max 10).
    /// Each section is loaded independently — if one fails it comes back null and the rest still return.
    /// Only data about the caller is included (no lists guarded by per-feature permissions).
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me(
        [FromQuery] string? include,
        [FromQuery] string? platform,
        [FromQuery] string? masterData,
        [FromServices] IServiceProvider services,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
        {
            throw new UnauthenticatedException();
        }

        var me = BuildMe();
        if (!string.Equals(include, "bootstrap", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(ApiResult<MeResponse>.Ok(me, string.Empty));
        }

        var log = services.GetRequiredService<ILogger<AuthController>>();
        async Task<T?> Section<T>(string name, Func<Task<T>> load) where T : class
        {
            try { return await load(); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Bootstrap section {Section} failed", name);
                return null;
            }
        }

        // Sequential on purpose: these services share one scoped DbContext.
        var menu = await Section("menu", () => services.GetRequiredService<App.Application.Identity.IMenuService>().GetMenuAsync(ct));
        var unread = await Section("unread", async () => (object)await services.GetRequiredService<App.Application.Notifications.INotificationInboxService>().UnreadCountAsync(ct));
        var tasks = await Section("tasks", () => services.GetRequiredService<App.Application.MyTasks.IMyTasksService>().GetSummaryAsync(ct));

        App.Application.Residents.ResidentMeDto? resident = null;
        var guard = services.GetRequiredService<IAuthorizationGuard>();
        if (currentUser.ResidentId is not null
            && await guard.HasPermissionAsync(App.Domain.Identity.PermissionAction.View, "Resident", ct))
        {
            resident = await Section("resident", () => services.GetRequiredService<App.Application.Residents.IResidentService>().GetMeAsync(ct));
        }

        var config = services.GetRequiredService<App.Application.Config.IConfigService>();
        var appVersion = string.IsNullOrWhiteSpace(platform)
            ? null
            : await Section("appVersion", () => AppVersionController.ReadAsync(config, platform, ct));

        var lists = new Dictionary<string, IReadOnlyList<App.Application.Config.MasterDataEntry>>(StringComparer.Ordinal);
        foreach (var key in (masterData ?? string.Empty)
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Distinct(StringComparer.Ordinal)
                     .Take(10))
        {
            var entries = await Section($"masterData:{key}", () => config.GetMasterDataAsync(key, null, false, ct));
            if (entries is not null) lists[key] = entries;
        }

        var bootstrap = new BootstrapResponse(
            me,
            menu,
            unread is int n ? n : null,
            tasks,
            resident,
            appVersion,
            lists);
        return Ok(ApiResult<BootstrapResponse>.Ok(bootstrap, string.Empty));
    }

    public sealed record BootstrapResponse(
        MeResponse Me,
        IReadOnlyList<App.Application.Identity.MenuNodeDto>? Menu,
        int? UnreadCount,
        IReadOnlyList<App.Application.MyTasks.MyTaskSummaryItem>? Tasks,
        App.Application.Residents.ResidentMeDto? Resident,
        AppVersionController.AppVersionInfo? AppVersion,
        IReadOnlyDictionary<string, IReadOnlyList<App.Application.Config.MasterDataEntry>> MasterData);

    private MeResponse BuildMe()
    {
        return new MeResponse(
            currentUser.UserId,
            currentUser.CommunityId,
            currentUser.UnitId,
            currentUser.OrganizationId,
            currentUser.ResidentId,
            currentUser.ResidentType,
            currentUser.Role,
            currentUser.Roles,
            currentUser.IsSuperAdmin,
            currentUser.IsResident);
    }

    public sealed record MeResponse(
        Guid? UserId,
        Guid? CommunityId,
        Guid? UnitId,
        Guid? OrganizationId,
        Guid? ResidentId,
        string? ResidentType,
        string? Role,
        IReadOnlyList<string> Roles,
        bool IsSuperAdmin,
        bool IsResident);

    public sealed class LoginBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "identifier is required")]
        [MaxLength(256)]
        public string Identifier { get; init; } = string.Empty;

        [Required(AllowEmptyStrings = false, ErrorMessage = "password is required")]
        [MaxLength(256)]
        public string Password { get; init; } = string.Empty;

        [MaxLength(128)]
        public string? OneSignalPlayerId { get; init; }

        [MaxLength(32)]
        public string? Platform { get; init; }
    }

    public sealed record LoginResponse(
        string AccessToken,
        DateTime ExpiresAtUtc,
        IReadOnlyCollection<string> Roles,
        string RefreshToken,
        LoginUserDto User);

    public sealed class RefreshBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "refreshToken is required")]
        [MaxLength(256)]
        public string RefreshToken { get; init; } = string.Empty;
    }

    public sealed record LogoutResponse(bool LoggedOut);

    public sealed class ForgotPasswordBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "identifier is required")]
        [MaxLength(256)]
        public string Identifier { get; init; } = string.Empty;
    }

    public sealed record ForgotPasswordResponse(string Message);

    public sealed class VerifyOtpBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "identifier is required")]
        [MaxLength(256)]
        public string Identifier { get; init; } = string.Empty;

        [Required(AllowEmptyStrings = false, ErrorMessage = "code is required")]
        [MaxLength(32)]
        public string Code { get; init; } = string.Empty;
    }

    public sealed record VerifyOtpResponse(bool Valid);

    public sealed class ResetPasswordBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "identifier is required")]
        [MaxLength(256)]
        public string Identifier { get; init; } = string.Empty;

        [Required(AllowEmptyStrings = false, ErrorMessage = "code is required")]
        [MaxLength(32)]
        public string Code { get; init; } = string.Empty;

        [Required(AllowEmptyStrings = false, ErrorMessage = "newPassword is required")]
        [MaxLength(256)]
        public string NewPassword { get; init; } = string.Empty;
    }

    public sealed record ResetPasswordResponse(bool PasswordReset);

    public sealed class ChangePasswordBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "currentPassword is required")]
        [MaxLength(256)]
        public string? CurrentPassword { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "newPassword is required")]
        [MaxLength(256)]
        public string? NewPassword { get; init; }
    }

    public sealed record ChangePasswordResponse(bool PasswordChanged);
}

[ApiController]
public sealed class DevicesController(CurrentUser currentUser, IAuthService auth) : ControllerBase
{
    [HttpPost("api/v1/devices")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceBody body, CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthenticatedException();
        }

        await auth.RegisterDeviceAsync(userId, body.OneSignalPlayerId, body.Platform, ct);
        return Ok(ApiResult<RegisterDeviceResponse>.Ok(new RegisterDeviceResponse(true), string.Empty));
    }

    public sealed class RegisterDeviceBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "oneSignalPlayerId is required")]
        [MaxLength(128)]
        public string OneSignalPlayerId { get; init; } = string.Empty;

        [MaxLength(32)]
        public string? Platform { get; init; }
    }

    public sealed record RegisterDeviceResponse(bool Registered);
}
