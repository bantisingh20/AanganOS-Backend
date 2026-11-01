using System.ComponentModel.DataAnnotations;
using App.Api.Security;
using App.Application.Gate;
using App.Application.Onboarding;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
public sealed class ResidentOnboardingController(IResidentOnboardingService svc) : ControllerBase
{
    [HttpPost("api/v1/auth/check-identifier")]
    [EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    public async Task<IActionResult> CheckIdentifier([FromBody] CheckIdentifierBody body, CancellationToken ct)
    {
        var exists = await svc.IdentifierExistsAsync(body.Identifier!, ct);
        return Ok(ApiResult<CheckIdentifierResponse>.Ok(new CheckIdentifierResponse(exists), string.Empty));
    }

    [HttpGet("api/v1/public/communities")]
    [EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    public async Task<IActionResult> SearchCommunities([FromQuery] string? search, CancellationToken ct)
    {
        var items = await svc.SearchCommunitiesAsync(search, ct);
        return Ok(ApiResult<IReadOnlyList<PublicCommunityDto>>.Ok(items, string.Empty));
    }

    [HttpGet("api/v1/public/communities/{communityId:guid}/wings")]
    [EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    public async Task<IActionResult> ListWings(Guid communityId, CancellationToken ct)
    {
        var items = await svc.ListWingsAsync(communityId, ct);
        return Ok(ApiResult<IReadOnlyList<PublicWingDto>>.Ok(items, string.Empty));
    }

    [HttpGet("api/v1/public/communities/{communityId:guid}/units")]
    [EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    public async Task<IActionResult> ListUnits(Guid communityId, [FromQuery] Guid? wingId, [FromQuery] string? search, CancellationToken ct)
    {
        var items = await svc.ListUnitsAsync(communityId, wingId, search, ct);
        return Ok(ApiResult<IReadOnlyList<PublicUnitDto>>.Ok(items, string.Empty));
    }

    [HttpPost("api/v1/residents/join-requests")]
    [EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    public async Task<IActionResult> SubmitJoinRequest([FromBody] SubmitJoinRequestBody body, CancellationToken ct)
    {
        var result = await svc.SubmitJoinRequestAsync(
            new SubmitJoinRequest(body.CommunityId, body.UnitId, body.Name!, body.Phone!, body.Pin!), ct);
        return Ok(ApiResult<SubmitJoinRequestResult>.Ok(result, string.Empty));
    }

    [HttpGet("api/v1/public/onboarding-config")]
    [EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    public async Task<IActionResult> OnboardingConfig([FromServices] IGateService gate, CancellationToken ct)
    {
        var enabled = await svc.IsSelfRegistrationEnabledAsync(ct);
        var validity = await gate.ResolveDefaultValidityAsync(null, ct);
        var expiringSoonDays = await gate.GetRecurringExpiringSoonDaysAsync(null, ct);
        return Ok(ApiResult<OnboardingConfigResponse>.Ok(
            new OnboardingConfigResponse(enabled, validity.Amount, validity.UnitCode, validity.Describe(), expiringSoonDays), string.Empty));
    }

    [HttpPost("api/v1/public/register/request-otp")]
    [EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    public async Task<IActionResult> RequestOtp([FromBody] RegisterOtpRequestBody body, CancellationToken ct)
    {
        await svc.RequestRegistrationOtpAsync(body.Phone!, ct);
        return Ok(ApiResult<RegisterOtpVerifyResponse>.Ok(new RegisterOtpVerifyResponse(true), string.Empty));
    }

    [HttpPost("api/v1/public/register/verify-otp")]
    [EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    public async Task<IActionResult> VerifyOtp([FromBody] RegisterOtpVerifyBody body, CancellationToken ct)
    {
        var valid = await svc.VerifyRegistrationOtpAsync(body.Phone!, body.Code!, ct);
        return Ok(ApiResult<RegisterOtpVerifyResponse>.Ok(new RegisterOtpVerifyResponse(valid), string.Empty));
    }

    [HttpPost("api/v1/residents/self-register")]
    [EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    public async Task<IActionResult> SelfRegister([FromBody] SelfRegisterBody body, CancellationToken ct)
    {
        var result = await svc.SelfRegisterAsync(
            new SelfRegisterRequest(
                body.CommunityId, body.UnitId, body.Name!, body.Phone!, body.Email, body.Password!,
                body.Code, body.PhotoBase64, body.ResidentType, body.DateOfBirth, body.Gender,
                body.MoveInDate, body.GovtIdType, body.SelfieBase64, body.GovtIdFrontBase64, body.GovtIdBackBase64), ct);
        return Ok(ApiResult<SelfRegisterResult>.Ok(result, string.Empty));
    }

    [HttpGet("api/v1/residents/join-requests")]
    [RequirePermission(PermissionAction.View, "JoinRequest")]
    public async Task<IActionResult> ListPendingJoinRequests(CancellationToken ct)
    {
        var items = await svc.ListPendingJoinRequestsAsync(ct);
        return Ok(ApiResult<IReadOnlyList<JoinRequestDto>>.Ok(items, string.Empty));
    }

    [HttpPost("api/v1/residents/join-requests/{id:guid}/approve")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Approve, "JoinRequest")]
    public async Task<IActionResult> ApproveJoinRequest(Guid id, CancellationToken ct)
    {
        var dto = await svc.ApproveJoinRequestAsync(id, ct);
        return Ok(ApiResult<JoinRequestDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/residents/join-requests/{id:guid}/reject")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Approve, "JoinRequest")]
    public async Task<IActionResult> RejectJoinRequest(Guid id, [FromBody] RejectJoinRequestBody body, CancellationToken ct)
    {
        var dto = await svc.RejectJoinRequestAsync(id, body.Reason, ct);
        return Ok(ApiResult<JoinRequestDto>.Ok(dto, string.Empty));
    }

    public sealed class CheckIdentifierBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "identifier is required")]
        [MaxLength(256)]
        public string? Identifier { get; init; }
    }

    public sealed record CheckIdentifierResponse(bool Exists);

    public sealed class SubmitJoinRequestBody
    {
        [Required(ErrorMessage = "communityId is required")]
        public Guid CommunityId { get; init; }

        [Required(ErrorMessage = "unitId is required")]
        public Guid UnitId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "name is required")]
        [MaxLength(256)]
        public string? Name { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "phone is required")]
        [MaxLength(32)]
        public string? Phone { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "pin is required")]
        [RegularExpression(@"^\d{4}$", ErrorMessage = "pin must be exactly 4 digits")]
        public string? Pin { get; init; }
    }

    public sealed class RejectJoinRequestBody
    {
        [MaxLength(500)]
        public string? Reason { get; init; }
    }

    public sealed record OnboardingConfigResponse(
        bool SelfRegistrationEnabled,
        int VisitPassValidityAmount,
        string VisitPassValidityUnit,
        string VisitPassValidityText,
        int RecurringExpiringSoonDays);

    public sealed class SelfRegisterBody
    {
        [Required(ErrorMessage = "communityId is required")]
        public Guid CommunityId { get; init; }

        [Required(ErrorMessage = "unitId is required")]
        public Guid UnitId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "name is required")]
        [MaxLength(256)]
        public string? Name { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "phone is required")]
        [MaxLength(32)]
        public string? Phone { get; init; }

        [MaxLength(256)]
        public string? Email { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "password is required")]
        [MinLength(6, ErrorMessage = "password must be at least 6 characters")]
        [MaxLength(128)]
        public string? Password { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "code is required")]
        [MaxLength(10)]
        public string? Code { get; init; }

        public string? PhotoBase64 { get; init; }

        [MaxLength(100)]
        public string? ResidentType { get; init; }

        public DateOnly? DateOfBirth { get; init; }

        [MaxLength(16)]
        public string? Gender { get; init; }

        public DateOnly? MoveInDate { get; init; }

        [MaxLength(32)]
        public string? GovtIdType { get; init; }

        public string? SelfieBase64 { get; init; }

        public string? GovtIdFrontBase64 { get; init; }

        public string? GovtIdBackBase64 { get; init; }
    }

    public sealed class RegisterOtpRequestBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "phone is required")]
        [MaxLength(32)]
        public string? Phone { get; init; }
    }

    public sealed class RegisterOtpVerifyBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "phone is required")]
        [MaxLength(32)]
        public string? Phone { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "code is required")]
        [MaxLength(10)]
        public string? Code { get; init; }
    }

    public sealed record RegisterOtpVerifyResponse(bool Valid);
}
