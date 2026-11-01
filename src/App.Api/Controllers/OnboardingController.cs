using System.ComponentModel.DataAnnotations;
using App.Api.Security;
using App.Application.Onboarding;
using App.Contracts.Envelope;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
[Route("api/v1/onboarding")]
[EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
public sealed class OnboardingController(IOnboardingService onboarding) : ControllerBase
{
    [HttpPost("community")]
    public async Task<IActionResult> CreateCommunity([FromBody] CreateCommunityBody body, CancellationToken ct)
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

        var response = new OnboardingResponse(
            result.CommunityId,
            result.CommunityName,
            "Thanks for registering. Our team will review your request and activate your account shortly.");

        return Ok(ApiResult<OnboardingResponse>.Ok(response, string.Empty));
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

    public sealed record OnboardingResponse(
        Guid CommunityId,
        string CommunityName,
        string Message);
}
