using App.Api.Security;
using App.Application.Abstractions;
using App.Application.Exceptions;
using App.Application.Files;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
[Route("api/v1/files")]
public sealed class FilesController(IFileService files, ITenantContext tenant) : ControllerBase
{
    [HttpPost]
    [DisableRequestSizeLimit]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "File")]
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile file,
        [FromQuery] string owningResourceType,
        [FromQuery] Guid communityId,
        [FromQuery] Guid? owningResourceId,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            throw new ValidationException("file", "a non-empty file is required");
        }
        if (string.IsNullOrWhiteSpace(owningResourceType))
        {
            throw new ValidationException("owningResourceType", "owningResourceType is required");
        }

        if (!tenant.IsPlatformSuperAdmin && !tenant.AuthorizedCommunityIds.Contains(communityId))
        {
            throw new ForbiddenException("You are not authorized for the target community.");
        }

        await using var stream = file.OpenReadStream();
        var reference = await files.StoreAsync(
            new StoreFileRequest(
                OwningResourceType: owningResourceType,
                OwningResourceId: owningResourceId,
                CommunityId: communityId,
                FileName: file.FileName,
                ContentType: file.ContentType,
                Content: stream),
            ct);

        return Ok(ApiResult<StoredFileReference>.Ok(reference, string.Empty));
    }

    [HttpGet("{reference:guid}")]
    [RequirePermission(PermissionAction.View, "File")]
    public async Task<IActionResult> Download(Guid reference, CancellationToken ct)
    {
        var opened = await files.OpenAsync(reference, ct);
        return File(opened.Content, opened.ContentType, opened.FileName, enableRangeProcessing: true);
    }
}
