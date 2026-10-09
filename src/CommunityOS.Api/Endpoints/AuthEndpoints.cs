using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using CommunityOS.Api.Security;
using CommunityOS.Api.Validation;
using CommunityOS.Application.Auth;
using CommunityOS.Application.Exceptions;
using CommunityOS.Contracts.Envelope;

namespace CommunityOS.Api.Endpoints;

/// <summary>
/// HTTP surface over Auth_Service (Task 5.2) — Req 1.1, 1.3, 2.1, 2.3. Every response uses the
/// <see cref="ApiResult{T}"/> envelope (Req 9.1) and request bodies validate at the trust boundary
/// (Req 9.2).
///
/// <para><b>Authorization.</b> login / forgot / reset are pre-authentication flows: they carry NO
/// <c>RequirePermission</c> metadata, so the permission-authorization middleware passes them
/// through untouched and anonymous callers reach them. Logout is authenticated-only: it has no
/// permission gate either (any authenticated user may end their own session) but it rejects an
/// anonymous caller and derives the session id (<c>jti</c>) from the validated token's claims —
/// never from the request body, so one user can't revoke another's session.</para>
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // The strict "auth" rate-limit policy (Task 8.2, Req 65.5) covers the whole auth group —
        // login / forgot / reset are the brute-force & account-enumeration targets, and logout is
        // throttled on the same partition. Partitioned by client IP for these pre-auth flows, so a
        // credential-stuffing source is capped regardless of which identifier it tries.
        var group = app.MapGroup("/api/v1/auth")
            .RequireRateLimiting(RateLimitingExtensions.AuthPolicy);

        // POST /api/v1/auth/login — anonymous. Valid credentials → token + roles (Req 1.1);
        // invalid credentials / locked account surface as the service's AppException, which the
        // exception middleware renders as the UNAUTHENTICATED envelope (Req 1.2). No token leaks.
        group.MapPost("/login", async (
                LoginBody body,
                IAuthService auth,
                CancellationToken ct) =>
            {
                var result = await auth.LoginAsync(
                    new LoginRequest(body.Identifier, body.Password, body.OneSignalPlayerId, body.Platform), ct);
                var response = new LoginResponse(result.AccessToken, result.ExpiresAtUtc, result.Roles, result.RefreshToken);
                return Results.Ok(ApiResult<LoginResponse>.Ok(response, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>();

        // POST /api/v1/auth/refresh — anonymous (pre-auth, like login). Swaps a valid refresh token
        // for a fresh access token + rotated refresh token (silent re-auth, no password). An
        // invalid/expired/revoked token surfaces as UNAUTHENTICATED → the client clears + routes to
        // login. Shares the auth rate-limit.
        group.MapPost("/refresh", async (
                RefreshBody body,
                IAuthService auth,
                CancellationToken ct) =>
            {
                var result = await auth.RefreshAsync(new RefreshRequest(body.RefreshToken), ct);
                var response = new LoginResponse(result.AccessToken, result.ExpiresAtUtc, result.Roles, result.RefreshToken);
                return Results.Ok(ApiResult<LoginResponse>.Ok(response, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>();

        // POST /api/v1/auth/logout — authenticated-only, no permission gate. Revokes the current
        // session using the jti from the authenticated principal (Req 1.3). Idempotent in the
        // service, so a repeat is a no-op.
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

        // POST /api/v1/auth/password/forgot — anonymous. Always succeeds (no account enumeration,
        // Req 2.1): an unknown identifier returns the same 200 without sending anything.
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

    /// <summary>
    /// Login/refresh success payload: the access token + its expiry + roles + the refresh token for
    /// silent re-auth. Never the password hash. The refresh token is a one-time-use opaque string
    /// the client stores securely and swaps at <c>/auth/refresh</c> when the access token expires.
    /// </summary>
    public sealed record LoginResponse(
        string AccessToken,
        DateTime ExpiresAtUtc,
        IReadOnlyCollection<string> Roles,
        string RefreshToken);

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
