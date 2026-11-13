using System.ComponentModel.DataAnnotations;
using App.Api.Security;
using App.Application.Residents;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
[Route("api/v1/units/{unitId:guid}")]
public sealed class UnitHistoryController(IResidentService residents, IUnitHistoryService history) : ControllerBase
{
    [HttpGet("household")]
    [Authorize]
    public async Task<IActionResult> Household(Guid unitId, CancellationToken ct)
    {
        var members = await residents.GetUnitHouseholdAsync(unitId, ct);
        return Ok(ApiResult<IReadOnlyList<UnitHouseholdMemberDto>>.Ok(members, string.Empty));
    }

    [HttpGet("history")]
    [RequirePermission(PermissionAction.View, "OwnershipHistory")]
    public async Task<IActionResult> GetHistory(Guid unitId, CancellationToken ct)
    {
        var dto = await history.GetUnitHistoryAsync(unitId, ct);
        return Ok(ApiResult<UnitHistoryDto>.Ok(dto, string.Empty));
    }

    [HttpGet("ownership-history")]
    [RequirePermission(PermissionAction.View, "OwnershipHistory")]
    public async Task<IActionResult> OwnershipHistory(Guid unitId, CancellationToken ct)
    {
        var dto = await history.GetUnitHistoryAsync(unitId, ct);
        return Ok(ApiResult<IReadOnlyList<OwnershipHistoryDto>>.Ok(dto.Ownership, string.Empty));
    }

    [HttpGet("tenancy-history")]
    [RequirePermission(PermissionAction.View, "TenancyHistory")]
    public async Task<IActionResult> TenancyHistory(Guid unitId, CancellationToken ct)
    {
        var dto = await history.GetUnitHistoryAsync(unitId, ct);
        return Ok(ApiResult<IReadOnlyList<TenancyHistoryDto>>.Ok(dto.Tenancy, string.Empty));
    }

    [HttpPost("ownership")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "OwnershipHistory")]
    public async Task<IActionResult> AssignOwnership(Guid unitId, [FromBody] AssignOwnershipBody body, CancellationToken ct)
    {
        var dto = await history.AssignOwnershipAsync(
            new AssignOwnershipRequest(unitId, body.ResidentId, body.StartDate), ct);
        return Ok(ApiResult<OwnershipHistoryDto>.Ok(dto, string.Empty));
    }

    [HttpPost("tenancy")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "TenancyHistory")]
    public async Task<IActionResult> BeginTenancy(Guid unitId, [FromBody] BeginTenancyBody body, CancellationToken ct)
    {
        var dto = await history.BeginTenancyAsync(
            new BeginTenancyRequest(unitId, body.ResidentId, body.StartDate), ct);
        return Ok(ApiResult<TenancyHistoryDto>.Ok(dto, string.Empty));
    }

    [HttpPost("tenancy/end")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "TenancyHistory")]
    public async Task<IActionResult> EndTenancy(Guid unitId, [FromBody] EndTenancyBody body, CancellationToken ct)
    {
        var dto = await history.EndTenancyAsync(
            new EndTenancyRequest(unitId, body.ResidentId, body.EndDate), ct);
        return Ok(ApiResult<TenancyHistoryDto>.Ok(dto, string.Empty));
    }

    public sealed class AssignOwnershipBody
    {
        [Required(ErrorMessage = "residentId is required")]
        public Guid ResidentId { get; init; }

        public DateTime? StartDate { get; init; }
    }

    public sealed class BeginTenancyBody
    {
        [Required(ErrorMessage = "residentId is required")]
        public Guid ResidentId { get; init; }

        public DateTime? StartDate { get; init; }
    }

    public sealed class EndTenancyBody
    {
        [Required(ErrorMessage = "residentId is required")]
        public Guid ResidentId { get; init; }

        public DateTime? EndDate { get; init; }
    }
}
