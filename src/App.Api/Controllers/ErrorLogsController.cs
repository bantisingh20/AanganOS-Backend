using App.Api.Security;
using App.Application.Diagnostics;
using App.Application.Querying;
using App.Contracts.Diagnostics;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

[ApiController]
[Route("api/v1/error-logs")]
public sealed class ErrorLogsController(IErrorLogService errorLogs) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionAction.View, "error_logs")]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int? statusCode,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var parameters = new ListQueryParameters
        {
            Page = page ?? 1,
            PageSize = pageSize ?? 20,
            Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
        };

        var result = await errorLogs.QueryAsync(parameters, statusCode, fromUtc, toUtc, ct);
        return Ok(ApiResult<PagedData<ErrorLogSummaryDto>>.Ok(result, string.Empty));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionAction.View, "error_logs")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var item = await errorLogs.GetByIdAsync(id, ct);
        if (item is null)
        {
            return NotFound(ApiResult.Fail(new App.Contracts.Errors.ApiError
            {
                Code = App.Contracts.Errors.ErrorCode.NotFound,
                Message = "Error log not found.",
            }, string.Empty));
        }

        return Ok(ApiResult<ErrorLogDetailDto>.Ok(item, string.Empty));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionAction.Delete, "error_logs")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await errorLogs.DeleteAsync(id, ct);
        if (!deleted)
        {
            return NotFound(ApiResult.Fail(new App.Contracts.Errors.ApiError
            {
                Code = App.Contracts.Errors.ErrorCode.NotFound,
                Message = "Error log not found or already deleted.",
            }, string.Empty));
        }

        return Ok(ApiResult<bool>.Ok(true, string.Empty));
    }

    [HttpDelete("clear")]
    [RequirePermission(PermissionAction.Delete, "error_logs")]
    public async Task<IActionResult> Clear([FromQuery] int? olderThanDays, CancellationToken ct)
    {
        var count = await errorLogs.ClearAsync(olderThanDays, ct);
        return Ok(ApiResult<int>.Ok(count, string.Empty));
    }
}
