using App.Api.Security;
using App.Application.Abstractions;
using App.Application.Exceptions;
using App.Application.Files;
using App.Contracts.Envelope;
using App.Domain.Identity;

namespace App.Api.Endpoints;

/// <summary>
/// HTTP surface over File_Service (Task 6.1). Multipart upload and authorized streaming download
/// both go through the service's permission checks — there is no static/public file route, so file
/// bytes are reachable only here, behind authorization (Req 7.4, 63.4).
///
/// <list type="bullet">
///   <item><c>POST /api/v1/files</c> — multipart upload, gated by <c>Create</c> on the <c>File</c>
///   resource. The owning community must be within the caller's authorized communities (defense in
///   depth on top of the server-side permission gate). The upload is audited inside the service
///   (Req 6.1). Returns the opaque reference in the standard envelope (Req 7.1, 9.1).</item>
///   <item><c>GET /api/v1/files/{reference}</c> — authorized streaming download. The service
///   enforces View on the owning resource + tenant authorization and returns the bytes only then;
///   otherwise it throws, and the global exception middleware renders the enveloped error (Req 7.2,
///   63.4). Success streams the raw bytes (a binary download, not a JSON envelope).</item>
/// </list>
/// </summary>
public static class FileEndpoints
{
    // 413/415 are produced by File_Service (PAYLOAD_TOO_LARGE / UNSUPPORTED_MEDIA_TYPE) and
    // rendered by the exception middleware; the framework's own default multipart body limit still
    // applies as an outer guard. ponytail: let the service be the single size authority.

    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/files");

        // POST /api/v1/files — multipart upload. Create-gated on the File resource (Req 63.4).
        group.MapPost("", async (
                IFormFile file,
                string owningResourceType,
                Guid communityId,
                Guid? owningResourceId,
                IFileService files,
                ITenantContext tenant,
                CancellationToken ct) =>
            {
                if (file is null || file.Length == 0)
                {
                    throw new ValidationException("file", "a non-empty file is required");
                }
                if (string.IsNullOrWhiteSpace(owningResourceType))
                {
                    throw new ValidationException("owningResourceType", "owningResourceType is required");
                }

                // Defense in depth: the file may only be stored into a community the caller is
                // authorized for (the Create gate already ran; this blocks a cross-tenant communityId).
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

                return Results.Ok(ApiResult<StoredFileReference>.Ok(reference, string.Empty));
            })
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "File"));

        // GET /api/v1/files/{reference} — authorized streaming download (Req 7.2, 63.4). The View
        // permission + tenant check live inside File_Service; the endpoint carries the File-View
        // gate so an unauthenticated/forbidden caller is stopped by the pipeline too.
        group.MapGet("/{reference:guid}", async (
                Guid reference,
                IFileService files,
                CancellationToken ct) =>
            {
                var opened = await files.OpenAsync(reference, ct);
                // Stream the bytes; a binary download is intentionally NOT wrapped in the JSON
                // envelope. enableRangeProcessing lets clients resume/seek large downloads.
                return Results.Stream(opened.Content, opened.ContentType, opened.FileName, enableRangeProcessing: true);
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "File"));

        return app;
    }
}
