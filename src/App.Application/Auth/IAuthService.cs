namespace App.Application.Auth;

/// <summary>
/// Auth_Service (Req 1, 2). Authentication and credential flows: login (token issuance matching the
/// Task 2.2 claim contract), logout (session revocation), the 5-failure/15-min lockout, and the
/// single-use/10-min OTP password-reset flow. Transport-agnostic — controllers (Task 5.4) bind to
/// these models; this service never exposes EF entities and never returns or logs a secret.
///
/// <para><b>Authorization scope.</b> This is <i>authentication</i> only — it decides who the caller
/// is and mints their token. Per-request <i>authorization</i> (permission gate) is Task 5.3 and is
/// intentionally not implemented here; the token this service issues carries exactly the claims
/// that gate relies on.</para>
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Authenticate a credential and, on success, issue a session token with the user's roles
    /// (Req 1.1). Invalid credentials are rejected with an authentication error and NO token
    /// (Req 1.2). A locked account (Req 1.6) is rejected even when the password is correct. Each
    /// failed attempt advances the concurrency-safe failure counter; the Nth consecutive failure
    /// within the window locks the account and records a security event.
    /// </summary>
    Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<LoginResult> IssueSessionForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke the session identified by its token id (<c>jti</c>), so the bearer token stops
    /// authenticating immediately (Req 1.3). Idempotent: revoking an already-revoked or unknown
    /// session is a no-op.
    /// </summary>
    Task LogoutAsync(string tokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchange a valid refresh token for a FRESH access token + a rotated refresh token, with no
    /// password (silent re-auth, mobile). The refresh token is <c>&lt;sessionId&gt;.&lt;secret&gt;</c>;
    /// the session is looked up by id and the secret verified against its stored hash. Fails
    /// (UnauthenticatedException) when the session is unknown, revoked, the refresh token is expired,
    /// or the secret doesn't match. On success the old refresh secret is ROTATED (one-time use), so a
    /// replayed/old refresh token no longer works. The access token's claims are re-resolved from the
    /// user's current roles/scope.
    /// </summary>
    Task<LoginResult> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Begin a password reset: generate a single-use OTP for the registered identifier and deliver
    /// it to the user's registered contact (Req 2.1). The OTP expires within the configured window
    /// (Req 2.2). To avoid account enumeration, an unknown identifier completes without error and
    /// without sending anything.
    /// </summary>
    Task RequestPasswordResetAsync(PasswordResetRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// VERIFY a password-reset OTP WITHOUT consuming it or changing anything (Req 2.3 step 2). Returns
    /// true when the identifier has a valid, unexpired, unused OTP matching <paramref name="request"/>'s
    /// code; false otherwise. The OTP is NOT marked used — the user still completes the reset at
    /// <see cref="ResetPasswordAsync"/>. To avoid account enumeration, an unknown identifier returns
    /// false (same as a wrong code) rather than erroring.
    /// </summary>
    Task<bool> VerifyPasswordResetOtpAsync(PasswordResetOtpCheck request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Complete a password reset: given a valid, unexpired, unused OTP and a new password, update
    /// the password (stored as a salted hash) and invalidate the OTP (Req 2.3), then record a
    /// security event (Req 2.6). An expired or already-used OTP is rejected with a validation error
    /// (Req 2.4).
    /// </summary>
    Task ResetPasswordAsync(PasswordResetConfirm request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Register (upsert) a push target for an ALREADY-authenticated user, outside the login flow
    /// (Req 11.1). The mobile app calls this when the OneSignal subscription id becomes available
    /// after login or changes on the device. Idempotent: re-registering the same id refreshes its
    /// LastSeen; a player id previously owned by another user is re-pointed to this user (one target
    /// → one user, Req 11.3). The owning <paramref name="userId"/> comes from the authenticated
    /// principal (never a request body), so a caller can only register a device to themselves.
    /// </summary>
    Task RegisterDeviceAsync(
        Guid userId,
        string oneSignalPlayerId,
        string? platform,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Login credentials plus optional device registration (Req single-active-session). A registered
/// identifier (email or phone) and the password; optionally the signing-in device's push target
/// (<paramref name="OneSignalPlayerId"/>) and <paramref name="Platform"/> ("android"/"ios"/"web")
/// so the login can register/replace this device and sign the user out on their previous device.
/// Device fields are optional — a login without them still authenticates and still enforces the
/// single-active-session rule (it just has no new push target to register).
/// </summary>
public sealed record LoginRequest(
    string Identifier,
    string Password,
    string? OneSignalPlayerId = null,
    string? Platform = null);

/// <summary>
/// The outcome of a successful login (Req 1.1): the signed token, its lifetime, and the user's
/// assigned role names. Never carries the password hash or any secret beyond the token itself.
/// </summary>
public sealed record LoginResult(
    string AccessToken,
    DateTime ExpiresAtUtc,
    IReadOnlyCollection<string> Roles,
    string RefreshToken);

/// <summary>A silent re-auth request: the opaque refresh token (<c>&lt;sessionId&gt;.&lt;secret&gt;</c>) the client holds.</summary>
public sealed record RefreshRequest(string RefreshToken);

/// <summary>A password-reset request for a registered identifier (email or phone) (Req 2.1).</summary>
public sealed record PasswordResetRequest(string Identifier);

/// <summary>Checking (not consuming) a reset OTP: the identifier + the code the user entered (Req 2.3 step 2).</summary>
public sealed record PasswordResetOtpCheck(string Identifier, string Code);

/// <summary>Completing a reset: the identifier, the OTP code the user received, and the new password (Req 2.3).</summary>
public sealed record PasswordResetConfirm(string Identifier, string Code, string NewPassword);
