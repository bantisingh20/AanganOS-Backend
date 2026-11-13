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
public sealed class AuthController(IAuthService auth) : ControllerBase
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
        if (HttpContext.User.Identity?.IsAuthenticated != true)
        {
            throw new UnauthenticatedException();
        }

        var sub = HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? HttpContext.User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(sub, out var userId))
        {
            throw new UnauthenticatedException();
        }

        await auth.ChangePasswordAsync(userId, new ChangePasswordRequest(body.CurrentPassword ?? string.Empty, body.NewPassword ?? string.Empty), ct);
        return Ok(ApiResult<ChangePasswordResponse>.Ok(new ChangePasswordResponse(true), string.Empty));
    }

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
public sealed class DevicesController(ITenantContext tenant, IAuthService auth) : ControllerBase
{
    [HttpPost("api/v1/devices")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceBody body, CancellationToken ct)
    {
        if (!Guid.TryParse(tenant.UserId, out var userId))
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
