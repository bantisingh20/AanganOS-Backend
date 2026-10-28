using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using App.Api.Security;
using App.Api.Validation;
using App.Application.Abstractions;
using App.Application.Auth;
using App.Application.Exceptions;
using App.Contracts.Envelope;

namespace App.Api.Endpoints;


public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth")
            .RequireRateLimiting(RateLimitingExtensions.AuthPolicy);

        group.MapPost("/login", async (
                LoginBody body,
                IAuthService auth,
                CancellationToken ct) =>
            {
                var result = await auth.LoginAsync(
                    new LoginRequest(body.Identifier, body.Password, body.OneSignalPlayerId, body.Platform), ct);
                var response = new LoginResponse(result.AccessToken, result.ExpiresAtUtc, result.Roles, result.RefreshToken, result.User);
                return Results.Ok(ApiResult<LoginResponse>.Ok(response, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>();

        group.MapPost("/refresh", async (
                RefreshBody body,
                IAuthService auth,
                CancellationToken ct) =>
            {
                var result = await auth.RefreshAsync(new RefreshRequest(body.RefreshToken), ct);
                var response = new LoginResponse(result.AccessToken, result.ExpiresAtUtc, result.Roles, result.RefreshToken, result.User);
                return Results.Ok(ApiResult<LoginResponse>.Ok(response, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>();

        group.MapPost("/logout", async (
                HttpContext http,
                IAuthService auth,
                CancellationToken ct) =>
            {
                if (http.User.Identity?.IsAuthenticated != true)
                {
                    throw new UnauthenticatedException();
                }

                var tokenId = http.User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
                if (string.IsNullOrEmpty(tokenId))
                {
                    // A validated token always carries a jti (the revocation hook enforces it), so
                    // a missing one means the principal isn't a real session token.
                    throw new UnauthenticatedException();
                }

                await auth.LogoutAsync(tokenId, ct);
                return Results.Ok(ApiResult<LogoutResponse>.Ok(new LogoutResponse(true), string.Empty));
            });

        group.MapPost("/password/forgot", async (
                ForgotPasswordBody body,
                IAuthService auth,
                CancellationToken ct) =>
            {
                await auth.RequestPasswordResetAsync(new PasswordResetRequest(body.Identifier), ct);
                return Results.Ok(ApiResult<ForgotPasswordResponse>.Ok(
                    new ForgotPasswordResponse("If the identifier is registered, a one-time code has been sent."),
                    string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>();

        group.MapPost("/password/verify-otp", async (
                VerifyOtpBody body,
                IAuthService auth,
                CancellationToken ct) =>
            {
                var valid = await auth.VerifyPasswordResetOtpAsync(
                    new PasswordResetOtpCheck(body.Identifier, body.Code), ct);
                return Results.Ok(ApiResult<VerifyOtpResponse>.Ok(new VerifyOtpResponse(valid), string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>();

        // POST /api/v1/auth/password/reset — anonymous. Valid, unexpired, unused OTP + new password
        // → password updated and OTP invalidated (Req 2.3); an expired/used OTP surfaces as the
        // service's validation error through the exception middleware (Req 2.4).
        group.MapPost("/password/reset", async (
                ResetPasswordBody body,
                IAuthService auth,
                CancellationToken ct) =>
            {
                await auth.ResetPasswordAsync(
                    new PasswordResetConfirm(body.Identifier, body.Code, body.NewPassword), ct);
                return Results.Ok(ApiResult<ResetPasswordResponse>.Ok(
                    new ResetPasswordResponse(true), string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>();

        app.MapPost("/api/v1/devices", async (
                RegisterDeviceBody body,
                ITenantContext tenant,
                IAuthService auth,
                CancellationToken ct) =>
            {
                if (!Guid.TryParse(tenant.UserId, out var userId))
                {
                    throw new UnauthenticatedException();
                }

                await auth.RegisterDeviceAsync(userId, body.OneSignalPlayerId, body.Platform, ct);
                return Results.Ok(ApiResult<RegisterDeviceResponse>.Ok(new RegisterDeviceResponse(true), string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy);

        return app;
    }

    public sealed class LoginBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "identifier is required")]
        [MaxLength(256)]
        public string Identifier { get; init; } = string.Empty;

        [Required(AllowEmptyStrings = false, ErrorMessage = "password is required")]
        [MaxLength(256)]
        public string Password { get; init; } = string.Empty;

        /// <summary>Optional OneSignal player id of the signing-in device (single-active-session device registration).</summary>
        [MaxLength(128)]
        public string? OneSignalPlayerId { get; init; }

        /// <summary>Optional device platform: "android" / "ios" / "web".</summary>
        [MaxLength(32)]
        public string? Platform { get; init; }
    }

    /// <summary>Body for <c>POST /api/v1/devices</c> — the device's OneSignal push target + platform.</summary>
    public sealed class RegisterDeviceBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "oneSignalPlayerId is required")]
        [MaxLength(128)]
        public string OneSignalPlayerId { get; init; } = string.Empty;

        /// <summary>Optional device platform: "android" / "ios" / "web".</summary>
        [MaxLength(32)]
        public string? Platform { get; init; }
    }

    /// <summary>Result of registering a device push target.</summary>
    public sealed record RegisterDeviceResponse(bool Registered);


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
}
