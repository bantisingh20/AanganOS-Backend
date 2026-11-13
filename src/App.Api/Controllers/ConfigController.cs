using System.ComponentModel.DataAnnotations;
using App.Api.Endpoints;
using App.Api.Security;
using App.Application.Config;
using App.Contracts.Envelope;
using App.Domain.Config;
using App.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
public sealed class ConfigController(IConfigService config) : ControllerBase
{
    [HttpGet("api/v1/config")]
    [Authorize]
    public async Task<IActionResult> List([FromQuery] string? search, CancellationToken ct)
    {
        var rows = await config.ListConfigAsync(search, ct);
        return Ok(ApiResult<IReadOnlyList<ConfigRow>>.Ok(rows, string.Empty));
    }

    [HttpPost("api/v1/config")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Configuration")]
    public async Task<IActionResult> Upsert([FromBody] UpsertConfigBody body, CancellationToken ct)
    {
        var row = await config.UpsertConfigAsync(
            new UpsertConfigRequest(body.Key ?? string.Empty, body.Scope, body.OrganizationId, body.CommunityId, body.Value ?? string.Empty), ct);
        return Ok(ApiResult<ConfigRow>.Ok(row, string.Empty));
    }

    [HttpDelete("api/v1/config/{id:guid}")]
    [RequirePermission(PermissionAction.Edit, "Configuration")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await config.DeleteConfigAsync(id, ct);
        return Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
    }

    [HttpGet("api/v1/config/{key}")]
    [Authorize]
    public async Task<IActionResult> GetValue(string key, [FromQuery] Guid? communityId, CancellationToken ct)
    {
        var value = await config.GetAsync(key, communityId, ct);
        return Ok(ApiResult<ConfigValueResponse>.Ok(new ConfigValueResponse(key, communityId, value), string.Empty));
    }

    [HttpPut("api/v1/config/{key}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Configuration")]
    public async Task<IActionResult> SetValue(string key, [FromQuery] Guid? communityId, [FromBody] SetConfigRequest request, CancellationToken ct)
    {
        await config.SetAsync(key, request.Value, communityId, ct);
        return Ok(ApiResult<ConfigValueResponse>.Ok(new ConfigValueResponse(key, communityId, request.Value), string.Empty));
    }

    [HttpGet("api/v1/master-data/{listKey}")]
    [Authorize]
    public async Task<IActionResult> GetMasterData(
        string listKey,
        [FromQuery] Guid? communityId,
        [FromQuery] bool? includeInactive,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var entries = await config.GetMasterDataAsync(listKey, communityId, includeInactive ?? false, ct);

        var p = page is null or < 1 ? 1 : page.Value;
        var size = pageSize is null or < 1 ? 50 : Math.Min(pageSize.Value, 200);
        var pageItems = entries.Skip((p - 1) * size).Take(size).ToList();

        var data = new PagedData<MasterDataEntry>
        {
            Items = pageItems,
            Page = p,
            PageSize = size,
            TotalCount = entries.Count,
        };
        return Ok(ApiResult<PagedData<MasterDataEntry>>.Ok(data, string.Empty));
    }

    [HttpPut("api/v1/master-data/{listKey}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "MasterData")]
    public async Task<IActionResult> SetMasterData(string listKey, [FromQuery] Guid? communityId, [FromBody] SetMasterDataRequest request, CancellationToken ct)
    {
        var entry = new MasterDataEntry(request.Code, request.Label, request.SortOrder, request.IsActive);
        await config.SetMasterDataAsync(listKey, entry, communityId, ct);
        return Ok(ApiResult<MasterDataEntry>.Ok(entry, string.Empty));
    }

    public sealed record ConfigValueResponse(string Key, Guid? CommunityId, string? Value);

    public sealed class SetConfigRequest
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "value is required")]
        [MaxLength(4000)]
        public string Value { get; init; } = string.Empty;
    }

    public sealed class UpsertConfigBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "key is required")]
        [MaxLength(200)]
        public string? Key { get; init; }

        public ConfigScope Scope { get; init; } = ConfigScope.Platform;

        public Guid? OrganizationId { get; init; }

        public Guid? CommunityId { get; init; }

        [Required(ErrorMessage = "value is required")]
        [MaxLength(4000)]
        public string? Value { get; init; }
    }

    public sealed class SetMasterDataRequest
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "code is required")]
        [MaxLength(100)]
        public string Code { get; init; } = string.Empty;

        [Required(AllowEmptyStrings = false, ErrorMessage = "label is required")]
        [MaxLength(200)]
        public string Label { get; init; } = string.Empty;

        [Range(0, int.MaxValue, ErrorMessage = "sortOrder must be non-negative")]
        public int SortOrder { get; init; }

        public bool IsActive { get; init; } = true;
    }
}
