using App.Api.Security;
using App.Application.Messaging;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

[ApiController]
[Route("api/v1/communication-test")]
[Authorize]
public sealed class CommunicationTestController : ControllerBase
{
    private readonly ICommunicationTestService _testService;

    public CommunicationTestController(ICommunicationTestService testService)
    {
        _testService = testService;
    }

    [HttpGet("config-summary")]
    [RequirePermission(PermissionAction.View, "admin.communication-test")]
    public async Task<IActionResult> GetConfigSummary(CancellationToken ct)
    {
        var summary = await _testService.GetConfigSummaryAsync(ct);
        return Ok(ApiResult<CommunicationConfigSummaryDto>.Ok(summary, string.Empty));
    }

    [HttpPost("email")]
    [RequirePermission(PermissionAction.Edit, "admin.communication-test")]
    public async Task<IActionResult> TestEmail([FromBody] TestEmailRequest request, CancellationToken ct)
    {
        var result = await _testService.TestEmailAsync(request, ct);
        return Ok(ApiResult<TestEmailResponse>.Ok(result, string.Empty));
    }

    [HttpPost("sms")]
    [RequirePermission(PermissionAction.Edit, "admin.communication-test")]
    public async Task<IActionResult> TestSms([FromBody] TestSmsRequest request, CancellationToken ct)
    {
        var result = await _testService.TestSmsAsync(request, ct);
        return Ok(ApiResult<TestSmsResponse>.Ok(result, string.Empty));
    }
}
