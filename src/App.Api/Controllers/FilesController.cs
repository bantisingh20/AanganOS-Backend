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
public sealed class FilesController(
    IFileService files,
    ITenantContext tenant,
    IImageCompressionService compression) : ControllerBase
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

    /// <summary>
    /// Uploads an image supplied as a Base64 string (e.g. from camera capture, canvas, or scanner).
    /// Automatically compresses to &lt; 500KB - 1MB and persists into StoredFiles blob store.
    /// Returns the StoredFileReference whose Id can be stored as PhotoFileId/AttachmentId.
    /// </summary>
    [HttpPost("base64")]
    [DisableRequestSizeLimit]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "File")]
    public async Task<IActionResult> UploadBase64(
        [FromBody] UploadBase64Body body,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (string.IsNullOrWhiteSpace(body.Base64))
        {
            throw new ValidationException("base64", "base64 image content is required");
        }
        if (string.IsNullOrWhiteSpace(body.OwningResourceType))
        {
            throw new ValidationException("owningResourceType", "owningResourceType is required");
        }
        if (!tenant.IsPlatformSuperAdmin && !tenant.AuthorizedCommunityIds.Contains(body.CommunityId))
        {
            throw new ForbiddenException("You are not authorized for the target community.");
        }

        var fileName = string.IsNullOrWhiteSpace(body.FileName)
            ? $"{body.OwningResourceType.ToLowerInvariant()}-{Guid.NewGuid():N}.jpg"
            : body.FileName.Trim();

        var compressed = await compression.CompressBase64Async(
            body.Base64,
            defaultFileName: fileName,
            maxWidth: body.MaxWidth ?? 1920,
            maxHeight: body.MaxHeight ?? 1080,
            targetMaxSizeBytes: body.TargetMaxSizeBytes ?? (500 * 1024),
            cancellationToken: ct);

        await using var stream = compressed.Stream;
        var reference = await files.StoreAsync(
            new StoreFileRequest(
                OwningResourceType: body.OwningResourceType,
                OwningResourceId: body.OwningResourceId,
                CommunityId: body.CommunityId,
                FileName: fileName,
                ContentType: compressed.ContentType,
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

public sealed record UploadBase64Body(
    string Base64,
    string OwningResourceType,
    Guid CommunityId,
    Guid? OwningResourceId = null,
    string? FileName = null,
    int? MaxWidth = null,
    int? MaxHeight = null,
    long? TargetMaxSizeBytes = null);
