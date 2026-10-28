using System.ComponentModel.DataAnnotations;
using App.Api.Security;
using App.Api.Validation;
using App.Application.Onboarding;
using App.Contracts.Envelope;
using App.Domain.Identity;

namespace App.Api.Endpoints;


public static class ResidentOnboardingEndpoints
{
    public static IEndpointRouteBuilder MapResidentOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        // --- Anonymous (auth rate-limit) ---------------------------------------------------------
        var anon = app.MapGroup("/api/v1")
            .RequireRateLimiting(RateLimitingExtensions.AuthPolicy);

        // POST /auth/check-identifier — does a login already exist for this phone/email?
        anon.MapPost("/auth/check-identifier", async (
                CheckIdentifierBody body,
                IResidentOnboardingService svc,
                CancellationToken ct) =>
            {
                var exists = await svc.IdentifierExistsAsync(body.Identifier!, ct);
                return Results.Ok(ApiResult<CheckIdentifierResponse>.Ok(new CheckIdentifierResponse(exists), string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>();

        // GET /public/communities?search= — search communities by name for the registration picker.
        anon.MapGet("/public/communities", async (
                string? search,
                IResidentOnboardingService svc,
                CancellationToken ct) =>
            {
                var items = await svc.SearchCommunitiesAsync(search, ct);
                return Results.Ok(ApiResult<IReadOnlyList<PublicCommunityDto>>.Ok(items, string.Empty));
            });

        // GET /public/communities/{id}/wings — the community's wing/block nodes.
        anon.MapGet("/public/communities/{communityId:guid}/wings", async (
                Guid communityId,
                IResidentOnboardingService svc,
                CancellationToken ct) =>
            {
                var items = await svc.ListWingsAsync(communityId, ct);
                return Results.Ok(ApiResult<IReadOnlyList<PublicWingDto>>.Ok(items, string.Empty));
            });

        // GET /public/communities/{id}/units?wingId=&search= — units for the registration picker.
        anon.MapGet("/public/communities/{communityId:guid}/units", async (
                Guid communityId,
                Guid? wingId,
                string? search,
                IResidentOnboardingService svc,
                CancellationToken ct) =>
            {
                var items = await svc.ListUnitsAsync(communityId, wingId, search, ct);
                return Results.Ok(ApiResult<IReadOnlyList<PublicUnitDto>>.Ok(items, string.Empty));
            });

        // POST /residents/join-requests — submit a join request (anonymous).
        anon.MapPost("/residents/join-requests", async (
                SubmitJoinRequestBody body,
                IResidentOnboardingService svc,
                CancellationToken ct) =>
            {
                var result = await svc.SubmitJoinRequestAsync(
                    new SubmitJoinRequest(body.CommunityId, body.UnitId, body.Name!, body.Phone!, body.Pin!), ct);
                return Results.Ok(ApiResult<SubmitJoinRequestResult>.Ok(result, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>();

        // GET /public/onboarding-config — whether resident self-registration is enabled.
        anon.MapGet("/public/onboarding-config", async (
                IResidentOnboardingService svc,
                CancellationToken ct) =>
            {
                var enabled = await svc.IsSelfRegistrationEnabledAsync(ct);
                return Results.Ok(ApiResult<OnboardingConfigResponse>.Ok(new OnboardingConfigResponse(enabled), string.Empty));
            });

        // POST /public/register/request-otp — issue a 6-digit OTP to a phone for registration.
        anon.MapPost("/public/register/request-otp", async (
                RegisterOtpRequestBody body,
                IResidentOnboardingService svc,
                CancellationToken ct) =>
            {
                await svc.RequestRegistrationOtpAsync(body.Phone!, ct);
                return Results.Ok(ApiResult<RegisterOtpVerifyResponse>.Ok(new RegisterOtpVerifyResponse(true), string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.AuthPolicy);

        // POST /public/register/verify-otp — check a registration OTP without consuming it.
        anon.MapPost("/public/register/verify-otp", async (
                RegisterOtpVerifyBody body,
                IResidentOnboardingService svc,
                CancellationToken ct) =>
            {
                var valid = await svc.VerifyRegistrationOtpAsync(body.Phone!, body.Code!, ct);
                return Results.Ok(ApiResult<RegisterOtpVerifyResponse>.Ok(new RegisterOtpVerifyResponse(valid), string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.AuthPolicy);

        // POST /residents/self-register — create a pending resident + login (anonymous).
        anon.MapPost("/residents/self-register", async (
                SelfRegisterBody body,
                IResidentOnboardingService svc,
                CancellationToken ct) =>
            {
                var result = await svc.SelfRegisterAsync(
                    new SelfRegisterRequest(
                        body.CommunityId, body.UnitId, body.Name!, body.Phone!, body.Email, body.Password!,
                        body.Code, body.PhotoBase64, body.ResidentType, body.DateOfBirth, body.Gender,
                        body.MoveInDate, body.GovtIdType, body.SelfieBase64, body.GovtIdFrontBase64, body.GovtIdBackBase64), ct);
                return Results.Ok(ApiResult<SelfRegisterResult>.Ok(result, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>();

        // --- Admin (authenticated, permission-gated on JoinRequest) ------------------------------
        var admin = app.MapGroup("/api/v1/residents/join-requests");

        // GET — pending requests for the admin's authorized communities.
        admin.MapGet("/", async (
                IResidentOnboardingService svc,
                CancellationToken ct) =>
            {
                var items = await svc.ListPendingJoinRequestsAsync(ct);
                return Results.Ok(ApiResult<IReadOnlyList<JoinRequestDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "JoinRequest"));

        // POST /{id}/approve — provision the resident + login + household.
        admin.MapPost("/{id:guid}/approve", async (
                Guid id,
                IResidentOnboardingService svc,
                CancellationToken ct) =>
            {
                var dto = await svc.ApproveJoinRequestAsync(id, ct);
                return Results.Ok(ApiResult<JoinRequestDto>.Ok(dto, string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Approve, "JoinRequest"));

        // POST /{id}/reject — reject with an optional reason.
        admin.MapPost("/{id:guid}/reject", async (
                Guid id,
                RejectJoinRequestBody body,
                IResidentOnboardingService svc,
                CancellationToken ct) =>
            {
                var dto = await svc.RejectJoinRequestAsync(id, body.Reason, ct);
                return Results.Ok(ApiResult<JoinRequestDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Approve, "JoinRequest"));

        return app;
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

    public sealed record OnboardingConfigResponse(bool SelfRegistrationEnabled);

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
