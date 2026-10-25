using System.ComponentModel.DataAnnotations;
using CommunityOS.Api.Security;
using CommunityOS.Api.Validation;
using CommunityOS.Application.Onboarding;
using CommunityOS.Contracts.Envelope;

namespace CommunityOS.Api.Endpoints;

/// <summary>
/// Public self-service onboarding (Req: let anyone create their own community + admin login).
/// ANONYMOUS, like login — no <c>RequirePermission</c> metadata, so the permission middleware passes
/// it through. It is placed under the strict "auth" rate-limit policy because it is an unauthenticated
/// write surface (brute-force / spam target). The service provisions org + community + user + a
/// community-SCOPED CommunityAdmin role, then returns a ready session so the portal logs the new admin
/// straight in — scoped to only their own community.
/// </summary>
public static class OnboardingEndpoints
{
    public static IEndpointRouteBuilder MapOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/onboarding")
            .RequireRateLimiting(RateLimitingExtensions.AuthPolicy);

        // POST /api/v1/onboarding/community — anonymous. Creates a community and its admin; returns
        // the created community + a login session (access + refresh tokens) for immediate sign-in.
        group.MapPost("/community", async (
                CreateCommunityBody body,
                IOnboardingService onboarding,
                CancellationToken ct) =>
            {
                var result = await onboarding.CreateCommunityAsync(
                    new CreateCommunityOnboardingRequest(
                        body.OrganizationName,
                        body.CommunityName,
                        body.CommunityType,
                        body.AdminName,
                        body.AdminEmail,
                        body.AdminPhone,
                        body.AdminPassword),
                    ct);

                // Approval gate: the account is created PENDING (no login session). The portal shows a
                // "we'll be in touch" message; a PowerUser must activate the admin before they can sign in.
                var response = new OnboardingResponse(
                    result.CommunityId,
                    result.CommunityName,
                    "Thanks for registering. Our team will review your request and activate your account shortly.");

                return Results.Ok(ApiResult<OnboardingResponse>.Ok(response, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>();

        return app;
    }

    public sealed class CreateCommunityBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "organizationName is required")]
        [MaxLength(256)]
        public string OrganizationName { get; init; } = string.Empty;

        [Required(AllowEmptyStrings = false, ErrorMessage = "communityName is required")]
        [MaxLength(256)]
        public string CommunityName { get; init; } = string.Empty;

        [Required(AllowEmptyStrings = false, ErrorMessage = "communityType is required")]
        [MaxLength(100)]
        public string CommunityType { get; init; } = string.Empty;

        [Required(AllowEmptyStrings = false, ErrorMessage = "adminName is required")]
        [MaxLength(256)]
        public string AdminName { get; init; } = string.Empty;

        [MaxLength(256)]
        [EmailAddress(ErrorMessage = "adminEmail must be a valid email")]
        public string? AdminEmail { get; init; }

        [MaxLength(32)]
        public string? AdminPhone { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "adminPassword is required")]
        [MinLength(6, ErrorMessage = "adminPassword must be at least 6 characters")]
        [MaxLength(256)]
        public string AdminPassword { get; init; } = string.Empty;
    }

    /// <summary>
    /// The created community's identity + a status message. The account is PENDING approval (no login
    /// session is returned) — the admin can sign in only after a PowerUser activates them.
    /// </summary>
    public sealed record OnboardingResponse(
        Guid CommunityId,
        string CommunityName,
        string Message);
}
