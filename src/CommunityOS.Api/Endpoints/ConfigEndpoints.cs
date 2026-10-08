using System.ComponentModel.DataAnnotations;
using CommunityOS.Api.Security;
using CommunityOS.Api.Validation;
using CommunityOS.Application.Abstractions.Security;
using CommunityOS.Domain.Identity;
using CommunityOS.Application.Config;
using CommunityOS.Contracts.Envelope;

namespace CommunityOS.Api.Endpoints;

/// <summary>
/// Minimal, usable surface over Config_Service (Task 3.1) — the full admin UI is a later phase.
/// Every response uses the <see cref="ApiResult{T}"/> envelope (Req 9.1); writes require the Edit
/// permission and reads require View via <see cref="RequirePermissionAttribute"/> (the Task 5.3
/// authorization seam, Req 8.2/4.1); request bodies validate at the trust boundary (Req 9.2); and
/// the master-data read is a paginated list (Req 9.3).
///
/// <para>A <c>communityId</c> query parameter selects the community scope; omitting it targets the
/// platform-wide default. Config_Service applies community→platform fallback on reads (Req 8.1, 8.3)
/// and records edits through the audit seam (Req 8.2).</para>
/// </summary>
public static class ConfigEndpoints
{
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/config");

        // GET /api/v1/config/{key}?communityId= — resolve a value with community→platform fallback.
        group.MapGet("/{key}", async (
                string key,
                Guid? communityId,
                IConfigService config,
                CancellationToken ct) =>
            {
                var value = await config.GetAsync(key, communityId, ct);
                return Results.Ok(ApiResult<ConfigValueResponse>.Ok(
                    new ConfigValueResponse(key, communityId, value), string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Configuration"));

        // PUT /api/v1/config/{key} — persist a value (platform or community); audited + cache-invalidated.
        group.MapPut("/{key}", async (
                string key,
                Guid? communityId,
                SetConfigRequest request,
                IConfigService config,
                CancellationToken ct) =>
            {
                await config.SetAsync(key, request.Value, communityId, ct);
                return Results.Ok(ApiResult<ConfigValueResponse>.Ok(
                    new ConfigValueResponse(key, communityId, request.Value), string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Configuration"));

        var master = app.MapGroup("/api/v1/master-data");

        // GET /api/v1/master-data/{listKey}?communityId=&page=&pageSize= — paginated resolved list.
        master.MapGet("/{listKey}", async (
                string listKey,
                Guid? communityId,
                bool? includeInactive,
                int? page,
                int? pageSize,
                IConfigService config,
                CancellationToken ct) =>
            {
                var entries = await config.GetMasterDataAsync(listKey, communityId, includeInactive ?? false, ct);

                // The resolved list is already a small, in-memory reference set; page over it in
                // memory here (Req 9.3) rather than hitting the DB again — bounded by design.
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
                return Results.Ok(ApiResult<PagedData<MasterDataEntry>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "MasterData"));

        // PUT /api/v1/master-data/{listKey} — add/update an entry (platform or community); audited.
        master.MapPut("/{listKey}", async (
                string listKey,
                Guid? communityId,
                SetMasterDataRequest request,
                IConfigService config,
                CancellationToken ct) =>
            {
                var entry = new MasterDataEntry(request.Code, request.Label, request.SortOrder, request.IsActive);
                await config.SetMasterDataAsync(listKey, entry, communityId, ct);
                return Results.Ok(ApiResult<MasterDataEntry>.Ok(entry, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "MasterData"));

        return app;
    }

    public sealed record ConfigValueResponse(string Key, Guid? CommunityId, string? Value);

    public sealed class SetConfigRequest
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "value is required")]
        [MaxLength(4000)]
        public string Value { get; init; } = string.Empty;
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
