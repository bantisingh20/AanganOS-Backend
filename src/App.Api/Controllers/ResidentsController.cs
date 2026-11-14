using System.ComponentModel.DataAnnotations;
using App.Api.Endpoints;
using App.Api.Security;
using App.Application.Exceptions;
using App.Application.Querying;
using App.Application.Residents;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
public sealed class ResidentsController(IResidentService residents) : ControllerBase
{
    [HttpPost("api/v1/residents")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "Resident")]
    public async Task<IActionResult> Create([FromBody] CreateResidentBody body, CancellationToken ct)
    {
        var dto = await residents.CreateAsync(new CreateResidentRequest(
            body.CommunityId ?? Guid.Empty,
            body.UserId,
            body.Name!,
            body.Email,
            body.Phone,
            body.ResidentType!,
            body.PhotoFileId,
            body.OrganizationId), ct);
        return Ok(ApiResult<ResidentDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/residents")]
    [RequirePermission(PermissionAction.View, "Resident")]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] string? verificationStatus,
        [FromQuery] string? residentType,
        [FromQuery] Guid? communityId,
        [FromQuery] Guid? organizationId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        if (!string.IsNullOrWhiteSpace(verificationStatus))
        {
            filters.Add(new FilterTerm("verificationStatus", FilterOperator.Equals, verificationStatus));
        }
        if (!string.IsNullOrWhiteSpace(residentType))
        {
            filters.Add(new FilterTerm("residentType", FilterOperator.Equals, residentType));
        }
        if (communityId is { } cid)
        {
            filters.Add(new FilterTerm("communityId", FilterOperator.Equals, cid.ToString()));
        }

        var parameters = new ListQueryParameters
        {
            Page = page,
            PageSize = pageSize,
            Search = string.IsNullOrWhiteSpace(search) ? null : search,
            Filters = filters,
            Sort = EndpointSort.Parse(sort),
        };

        var data = await residents.ListAsync(parameters, organizationId, ct);
        return Ok(ApiResult<PagedData<ResidentDto>>.Ok(data, string.Empty));
    }

    [HttpGet("api/v1/residents/me")]
    [RequirePermission(PermissionAction.View, "Resident")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var dto = await residents.GetMeAsync(ct);
        return Ok(ApiResult<ResidentMeDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/residents/me/presence")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.View, "Resident")]
    public async Task<IActionResult> SetMyPresence([FromBody] SetPresenceBody body, CancellationToken ct)
    {
        var dto = await residents.SetMyPresenceAsync(body.PresenceStatus!, ct);
        return Ok(ApiResult<ResidentDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/residents/me/submit-kyc")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.View, "Resident")]
    public async Task<IActionResult> SubmitMyKyc([FromBody] SubmitKycBody body, CancellationToken ct)
    {
        var dto = await residents.SubmitMyKycAsync(new SubmitKycRequest(
            body.ResidentType, body.DateOfBirth, body.Gender, body.MoveInDate,
            body.GovtIdType, body.SelfieBase64, body.GovtIdFrontBase64, body.GovtIdBackBase64), ct);
        return Ok(ApiResult<ResidentDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/residents/{id:guid}")]
    [RequirePermission(PermissionAction.View, "Resident")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var dto = await residents.GetAsync(id, includeArchived: false, ct)
            ?? throw new NotFoundException("Resident not found.");
        return Ok(ApiResult<ResidentDto>.Ok(dto, string.Empty));
    }

    [HttpPut("api/v1/residents/{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Resident")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateResidentBody body, CancellationToken ct)
    {
        var dto = await residents.UpdateAsync(id, new UpdateResidentRequest(
            body.Name,
            body.ResidentType,
            body.Email,
            body.UpdateEmail,
            body.Phone,
            body.UpdatePhone,
            body.UserId,
            body.UpdateUserId,
            body.PhotoFileId,
            body.UpdatePhotoFileId), ct);
        return Ok(ApiResult<ResidentDto>.Ok(dto, string.Empty));
    }

    [HttpDelete("api/v1/residents/{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Delete, "Resident")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await residents.DeleteAsync(id, ct);
        return Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
    }

    [HttpPost("api/v1/residents/{id:guid}/verify")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Approve, "Resident")]
    public async Task<IActionResult> Verify(Guid id, CancellationToken ct)
    {
        var dto = await residents.ApproveAsync(id, ct);
        return Ok(ApiResult<ResidentDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/residents/{id:guid}/reject")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Approve, "Resident")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectResidentBody body, CancellationToken ct)
    {
        var dto = await residents.RejectAsync(id, new RejectResidentRequest(body.Reason!), ct);
        return Ok(ApiResult<ResidentDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/residents/{id:guid}/resubmit")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Approve, "Resident")]
    public async Task<IActionResult> Resubmit(Guid id, CancellationToken ct)
    {
        var dto = await residents.ResubmitForVerificationAsync(id, ct);
        return Ok(ApiResult<ResidentDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/residents/{id:guid}/move-in")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "Resident")]
    public async Task<IActionResult> MoveIn(Guid id, [FromBody] MoveInBody body, CancellationToken ct)
    {
        var dto = await residents.MoveInAsync(id, new MoveInRequest(
            body.UnitId, body.MoveInDate, body.Relationship), ct);
        return Ok(ApiResult<HouseholdMemberDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/residents/{id:guid}/move-out")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Resident")]
    public async Task<IActionResult> MoveOut(Guid id, [FromBody] MoveOutBody body, CancellationToken ct)
    {
        var dto = await residents.MoveOutAsync(id, new MoveOutRequest(body.UnitId, body.MoveOutDate), ct);
        return Ok(ApiResult<HouseholdMemberDto>.Ok(dto, string.Empty));
    }

    [HttpPost("api/v1/residents/{id:guid}/household")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "HouseholdMember")]
    public async Task<IActionResult> AssociateHousehold(Guid id, [FromBody] AssociateHouseholdBody body, CancellationToken ct)
    {
        var dto = await residents.AssociateHouseholdAsync(id, new AssociateHouseholdRequest(
            body.UnitId, body.Relationship!, body.StartDate), ct);
        return Ok(ApiResult<HouseholdMemberDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/residents/{id:guid}/household")]
    [RequirePermission(PermissionAction.View, "HouseholdMember")]
    public async Task<IActionResult> GetHousehold(Guid id, CancellationToken ct)
    {
        var data = await residents.GetResidentHouseholdAsync(id, ct);
        return Ok(ApiResult<IReadOnlyList<ResidentHouseholdUnitDto>>.Ok(data, string.Empty));
    }

    [HttpPost("api/v1/residents/{id:guid}/login")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "User")]
    public async Task<IActionResult> CreateLogin(Guid id, [FromBody] CreateResidentLoginBody body, CancellationToken ct)
    {
        var result = await residents.CreateLoginAsync(id,
            new CreateResidentLoginRequest(body.Identifier!, body.Password!, body.RoleName), ct);
        return Ok(ApiResult<ResidentLoginResult>.Ok(result, string.Empty));
    }

    [HttpPost("api/v1/residents/{id:guid}/emergency-contacts")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "EmergencyContact")]
    public async Task<IActionResult> AddEmergencyContact(Guid id, [FromBody] AddEmergencyContactBody body, CancellationToken ct)
    {
        var dto = await residents.AddEmergencyContactAsync(id, new AddEmergencyContactRequest(
            body.Name!, body.Phone!, body.Relationship!), ct);
        return Ok(ApiResult<EmergencyContactDto>.Ok(dto, string.Empty));
    }

    [HttpGet("api/v1/residents/{id:guid}/emergency-contacts")]
    [RequirePermission(PermissionAction.View, "EmergencyContact")]
    public async Task<IActionResult> ListEmergencyContacts(Guid id, CancellationToken ct)
    {
        var data = await residents.ListEmergencyContactsAsync(id, ct);
        return Ok(ApiResult<IReadOnlyList<EmergencyContactDto>>.Ok(data, string.Empty));
    }

    [HttpGet("api/v1/residents/{id:guid}/communication-preferences")]
    [RequirePermission(PermissionAction.View, "Resident")]
    public async Task<IActionResult> GetCommunicationPreferences(
        Guid id, [FromServices] ICommunicationPreferenceService preferences, CancellationToken ct)
    {
        var data = await preferences.GetAsync(id, ct);
        return Ok(ApiResult<IReadOnlyList<CommunicationPreferenceDto>>.Ok(data, string.Empty));
    }

    [HttpPut("api/v1/residents/{id:guid}/communication-preferences")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Resident")]
    public async Task<IActionResult> SetCommunicationPreferences(
        Guid id, [FromBody] SetCommunicationPreferencesBody body,
        [FromServices] ICommunicationPreferenceService preferences, CancellationToken ct)
    {
        var request = new SetCommunicationPreferencesRequest(
            (body.Preferences ?? [])
                .Select(p => new CommunicationPreferenceUpdate(p.Category!, p.OptedIn))
                .ToList());
        var data = await preferences.SetAsync(id, request, ct);
        return Ok(ApiResult<IReadOnlyList<CommunicationPreferenceDto>>.Ok(data, string.Empty));
    }

    public sealed class CreateResidentBody
    {
        public Guid? OrganizationId { get; init; }
        public Guid? CommunityId { get; init; }

        public Guid? UserId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "name is required")]
        [MaxLength(256)]
        public string? Name { get; init; }

        [MaxLength(256)]
        public string? Email { get; init; }

        [MaxLength(50)]
        public string? Phone { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "residentType is required")]
        [MaxLength(100)]
        public string? ResidentType { get; init; }

        [MaxLength(200)]
        public string? PhotoFileId { get; init; }
    }

    public sealed class SetPresenceBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "presenceStatus is required")]
        [MaxLength(20)]
        public string? PresenceStatus { get; init; }
    }

    public sealed class SubmitKycBody
    {
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

    public sealed class UpdateResidentBody
    {
        [MaxLength(256)]
        public string? Name { get; init; }

        [MaxLength(100)]
        public string? ResidentType { get; init; }

        [MaxLength(256)]
        public string? Email { get; init; }

        public bool UpdateEmail { get; init; }

        [MaxLength(50)]
        public string? Phone { get; init; }

        public bool UpdatePhone { get; init; }

        public Guid? UserId { get; init; }

        public bool UpdateUserId { get; init; }

        [MaxLength(200)]
        public string? PhotoFileId { get; init; }

        public bool UpdatePhotoFileId { get; init; }
    }

    public sealed class RejectResidentBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "reason is required")]
        [MaxLength(1000)]
        public string? Reason { get; init; }
    }

    public sealed class MoveInBody
    {
        [Required(ErrorMessage = "unitId is required")]
        public Guid UnitId { get; init; }

        public DateTime? MoveInDate { get; init; }

        [MaxLength(100)]
        public string? Relationship { get; init; }
    }

    public sealed class MoveOutBody
    {
        [Required(ErrorMessage = "unitId is required")]
        public Guid UnitId { get; init; }

        public DateTime? MoveOutDate { get; init; }
    }

    public sealed class AssociateHouseholdBody
    {
        [Required(ErrorMessage = "unitId is required")]
        public Guid UnitId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "relationship is required")]
        [MaxLength(100)]
        public string? Relationship { get; init; }

        public DateTime? StartDate { get; init; }
    }

    public sealed class CreateResidentLoginBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "identifier is required")]
        [MaxLength(256)]
        public string? Identifier { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "password is required")]
        [MaxLength(256)]
        public string? Password { get; init; }

        [MaxLength(128)]
        public string? RoleName { get; init; }
    }

    public sealed class AddEmergencyContactBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "name is required")]
        [MaxLength(256)]
        public string? Name { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "phone is required")]
        [MaxLength(50)]
        public string? Phone { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "relationship is required")]
        [MaxLength(100)]
        public string? Relationship { get; init; }
    }

    public sealed class SetCommunicationPreferencesBody
    {
        [Required(ErrorMessage = "preferences is required")]
        public List<CommunicationPreferenceItem>? Preferences { get; init; }
    }

    public sealed class CommunicationPreferenceItem
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "category is required")]
        [MaxLength(128)]
        public string? Category { get; init; }

        public bool OptedIn { get; init; }
    }
}
