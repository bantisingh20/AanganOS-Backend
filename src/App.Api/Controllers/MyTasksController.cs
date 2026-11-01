using App.Application.MyTasks;
using App.Contracts.Envelope;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

[ApiController]
[Route("api/v1/my-tasks")]
[Authorize]
public sealed class MyTasksController(IMyTasksService svc) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var items = await svc.GetSummaryAsync(ct);
        return Ok(ApiResult<IReadOnlyList<MyTaskSummaryItem>>.Ok(items, string.Empty));
    }

    [HttpGet]
    public async Task<IActionResult> Cards([FromQuery] string? type, CancellationToken ct)
    {
        var items = await svc.GetCardsAsync(type, ct);
        return Ok(ApiResult<IReadOnlyList<MyTaskCard>>.Ok(items, string.Empty));
    }
}
