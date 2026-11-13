using System.ComponentModel.DataAnnotations;
using App.Api.Security;
using App.Application.Subscription;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
[Route("api/v1/subscriptions")]
public sealed class SubscriptionsController(ISubscriptionService subscriptions) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        var dto = await subscriptions.GetMineAsync(ct);
        return Ok(ApiResult<SubscriptionDto>.Ok(dto, string.Empty));
    }

    [HttpGet("by-org/{orgId:guid}")]
    [RequirePermission(PermissionAction.View, "Community")]
    public async Task<IActionResult> GetByOrganization(Guid orgId, CancellationToken ct)
    {
        var dto = await subscriptions.GetByOrganizationAsync(orgId, ct);
        return Ok(ApiResult<SubscriptionDto>.Ok(dto, string.Empty));
    }

    [HttpPost("set-expiry")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Community")]
    public async Task<IActionResult> SetExpiry([FromBody] SetExpiryBody body, CancellationToken ct)
    {
        var dto = await subscriptions.SetExpiryAsync(
            body.OrganizationId, body.PlanMonths, body.StartsAtUtc, body.ExpiresAtUtc, ct);
        return Ok(ApiResult<SubscriptionDto>.Ok(dto, string.Empty));
    }

    [HttpPost("request-renewal")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    public async Task<IActionResult> RequestRenewal([FromBody] RequestRenewalBody body, CancellationToken ct)
    {
        var dto = await subscriptions.RequestRenewalAsync(body.PlanMonths, ct);
        return Ok(ApiResult<SubscriptionDto>.Ok(dto, string.Empty));
    }

    [HttpGet("renewals/pending")]
    [RequirePermission(PermissionAction.View, "Community")]
    public async Task<IActionResult> ListPendingRenewals(CancellationToken ct)
    {
        var data = await subscriptions.ListPendingRenewalsAsync(ct);
        return Ok(ApiResult<IReadOnlyList<RenewalRequestDto>>.Ok(data, string.Empty));
    }

    [HttpPost("renewals/{id:guid}/approve")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Community")]
    public async Task<IActionResult> ApproveRenewal(Guid id, [FromBody] ApproveRenewalBody body, CancellationToken ct)
    {
        var dto = await subscriptions.ApproveRenewalAsync(
            id, body.PlanMonths, body.StartsAtUtc, body.ExpiresAtUtc, ct);
        return Ok(ApiResult<SubscriptionDto>.Ok(dto, string.Empty));
    }

    public sealed class SetExpiryBody
    {
        [Required(ErrorMessage = "organizationId is required")]
        public Guid OrganizationId { get; init; }

        [Range(1, 12, ErrorMessage = "planMonths is required")]
        public int PlanMonths { get; init; }

        [Required(ErrorMessage = "startsAtUtc is required")]
        public DateTime StartsAtUtc { get; init; }

        [Required(ErrorMessage = "expiresAtUtc is required")]
        public DateTime ExpiresAtUtc { get; init; }
    }

    public sealed class RequestRenewalBody
    {
        [Range(1, 12, ErrorMessage = "planMonths is required")]
        public int PlanMonths { get; init; }
    }

    public sealed class ApproveRenewalBody
    {
        [Range(1, 12, ErrorMessage = "planMonths is required")]
        public int PlanMonths { get; init; }

        [Required(ErrorMessage = "startsAtUtc is required")]
        public DateTime StartsAtUtc { get; init; }

        [Required(ErrorMessage = "expiresAtUtc is required")]
        public DateTime ExpiresAtUtc { get; init; }
    }
}
