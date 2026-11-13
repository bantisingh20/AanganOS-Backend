using System.ComponentModel.DataAnnotations;
using App.Api.Endpoints;
using App.Api.Security;
using App.Application.Community;
using App.Application.Exceptions;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
[Route("api/v1/units")]
public sealed class UnitsController(IUnitService units, IUnitBulkService bulk) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "Unit")]
    public async Task<IActionResult> Create([FromBody] CreateUnitBody body, CancellationToken ct)
    {
        var dto = await units.CreateAsync(new CreateUnitRequest(
            body.CommunityId,
            body.HierarchyNodeId,
            body.UnitNumber!,
            body.UnitType!,
            body.Area,
            body.Floor,
            body.Bedrooms,
            body.UnitStatus!,
            body.CustomAttributes), ct);
        return Ok(ApiResult<UnitDto>.Ok(dto, string.Empty));
    }

    [HttpGet]
    [RequirePermission(PermissionAction.View, "Unit")]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] string? unitStatus,
        [FromQuery] string? unitType,
        [FromQuery] string? floor,
        [FromQuery] Guid? hierarchyNodeId,
        [FromQuery] Guid? communityId,
        [FromQuery] Guid? organizationId,
        [FromQuery] bool? availableForOwner,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        if (!string.IsNullOrWhiteSpace(unitStatus))
        {
            filters.Add(new FilterTerm("unitStatus", FilterOperator.Equals, unitStatus));
        }
        if (!string.IsNullOrWhiteSpace(unitType))
        {
            filters.Add(new FilterTerm("unitType", FilterOperator.Equals, unitType));
        }
        if (!string.IsNullOrWhiteSpace(floor))
        {
            filters.Add(new FilterTerm("floor", FilterOperator.Equals, floor));
        }
        if (hierarchyNodeId is not null)
        {
            filters.Add(new FilterTerm("hierarchyNodeId", FilterOperator.Equals, hierarchyNodeId.Value.ToString()));
        }
        if (communityId is not null)
        {
            filters.Add(new FilterTerm("communityId", FilterOperator.Equals, communityId.Value.ToString()));
        }

        var parameters = new ListQueryParameters
        {
            Page = page,
            PageSize = pageSize,
            Search = string.IsNullOrWhiteSpace(search) ? null : search,
            Filters = filters,
            Sort = EndpointSort.Parse(sort),
        };

        var data = await units.ListAsync(parameters, availableForOwner ?? false, organizationId, ct);
        return Ok(ApiResult<PagedData<UnitDto>>.Ok(data, string.Empty));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionAction.View, "Unit")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var dto = await units.GetAsync(id, includeArchived: false, ct)
            ?? throw new NotFoundException("Unit not found.");
        return Ok(ApiResult<UnitDto>.Ok(dto, string.Empty));
    }

    [HttpPut("{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Unit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUnitBody body, CancellationToken ct)
    {
        var dto = await units.UpdateAsync(id, new UpdateUnitRequest(
            body.HierarchyNodeId,
            body.UpdateHierarchyNode,
            body.UnitNumber,
            body.UnitType,
            body.Area,
            body.UpdateArea,
            body.Floor,
            body.UpdateFloor,
            body.Bedrooms,
            body.UpdateBedrooms,
            body.UnitStatus,
            body.CustomAttributes), ct);
        return Ok(ApiResult<UnitDto>.Ok(dto, string.Empty));
    }

    [HttpDelete("{id:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Delete, "Unit")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await units.DeleteAsync(id, ct);
        return Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
    }

    [HttpPost("import")]
    [DisableRequestSizeLimit]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "Unit")]
    public async Task<IActionResult> Import([FromForm] IFormFile file, [FromQuery] Guid communityId, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            throw new App.Application.Exceptions.ValidationException("file", "a non-empty CSV file is required");
        }

        using var reader = new StreamReader(file.OpenReadStream());
        var csv = await reader.ReadToEndAsync(ct);

        var result = await bulk.ImportUnitsAsync(communityId, csv, ct);
        return Ok(ApiResult<BulkImportResult>.Ok(result, string.Empty));
    }

    [HttpGet("export")]
    [RequirePermission(PermissionAction.Export, "Unit")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var export = await bulk.ExportUnitsAsync(ct);
        return File(export.Content, export.ContentType, export.FileName);
    }

    public sealed class CreateUnitBody
    {
        [Required(ErrorMessage = "communityId is required")]
        public Guid CommunityId { get; init; }

        public Guid? HierarchyNodeId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "unitNumber is required")]
        [MaxLength(100)]
        public string? UnitNumber { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "unitType is required")]
        [MaxLength(100)]
        public string? UnitType { get; init; }

        public decimal? Area { get; init; }

        [MaxLength(100)]
        public string? Floor { get; init; }

        public int? Bedrooms { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "unitStatus is required")]
        [MaxLength(100)]
        public string? UnitStatus { get; init; }

        public IReadOnlyDictionary<string, string>? CustomAttributes { get; init; }
    }

    public sealed class UpdateUnitBody
    {
        public Guid? HierarchyNodeId { get; init; }

        public bool UpdateHierarchyNode { get; init; }

        [MaxLength(100)]
        public string? UnitNumber { get; init; }

        [MaxLength(100)]
        public string? UnitType { get; init; }

        public decimal? Area { get; init; }

        public bool UpdateArea { get; init; }

        [MaxLength(100)]
        public string? Floor { get; init; }

        public bool UpdateFloor { get; init; }

        public int? Bedrooms { get; init; }

        public bool UpdateBedrooms { get; init; }

        [MaxLength(100)]
        public string? UnitStatus { get; init; }

        public IReadOnlyDictionary<string, string>? CustomAttributes { get; init; }
    }
}
