using App.Application.Abstractions.Security;
using App.Application.Audit;
using App.Application.Exceptions;
using App.Application.Files;
using App.Contracts.Errors;
using App.Domain.Files;
using App.Domain.Identity;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Files;

/// <summary>
/// EF-backed File_Service (Req 7). Validates uploads at the trust boundary, persists bytes through
/// the <see cref="IFileBlobStore"/> seam (non-public storage, Req 7.4) and the metadata as a
/// tenant-owned <see cref="StoredFile"/> row (Req 7.1), audits the upload (Req 6.1), and streams a
/// file back only behind a View-on-owning-resource + tenant-authorized check (Req 7.2, 63.4).
///
/// <para><b>Security-first.</b> Size and content type are validated against the configured limits
/// before anything is stored (Req 7.3), fail-closed on an empty allow-list. The size cap is also
/// enforced while streaming to the blob store, so an under-declared length cannot smuggle an
/// oversize file past the check. Download resolves the row through the tenant-filtered DbSet (a
/// foreign-tenant/soft-deleted row is already invisible) and additionally runs the explicit
/// permission + <see cref="IAuthorizationGuard.EnsureCommunityAuthorized"/> checks as
/// defense-in-depth. File contents are never logged.</para>
/// </summary>
public sealed class FileService(
    AppDbContext db,
    IFileBlobStore blobStore,
    IAuthorizationGuard authorizationGuard,
    IAuditService audit,
    IOptions<FileStorageOptions> options)
    : IFileService
{
    private readonly FileStorageOptions _options = options.Value;

    public async Task<StoredFileReference> StoreAsync(StoreFileRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request.Content);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OwningResourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);

        // Content-type allow-list (Req 7.3). Fail-closed: no allow-list configured ⇒ reject.
        if (!IsAllowedContentType(request.ContentType))
        {
            throw new AppExceptionWithCode(ErrorCode.UnsupportedMediaType,
                $"Content type '{request.ContentType}' is not allowed.");
        }

        // Size check up front when the stream reports a length (Req 7.3) — reject before writing.
        if (request.Content.CanSeek && request.Content.Length > _options.MaxSizeBytes)
        {
            throw new AppExceptionWithCode(ErrorCode.PayloadTooLarge,
                "The uploaded file exceeds the maximum allowed size.");
        }

        // Persist bytes through the blob store, capping the stream at MaxSizeBytes so a stream that
        // under-reports (or cannot report) its length still cannot exceed the limit (Req 7.3).
        await using var capped = new SizeLimitedReadStream(request.Content, _options.MaxSizeBytes);
        string storageKey;
        try
        {
            storageKey = await blobStore.SaveAsync(capped, cancellationToken);
        }
        catch (FileSizeExceededException)
        {
            throw new AppExceptionWithCode(ErrorCode.PayloadTooLarge,
                "The uploaded file exceeds the maximum allowed size.");
        }

        var stored = new StoredFile
        {
            CommunityId = request.CommunityId,
            OwningResourceType = request.OwningResourceType,
            OwningResourceId = request.OwningResourceId,
            FileName = request.FileName,
            ContentType = request.ContentType,
            SizeBytes = capped.BytesRead,
            StorageKey = storageKey,
        };

        db.Set<StoredFile>().Add(stored);
        await db.SaveChangesAsync(cancellationToken);

        // Audit the upload (Req 6.1) — metadata only, never the file contents.
        await audit.AppendAsync(
            new AuditEntry(
                Action: "Upload",
                ResourceType: "File",
                ResourceId: stored.Id.ToString(),
                CommunityId: stored.CommunityId,
                Detail: $"owningResourceType={stored.OwningResourceType};owningResourceId={stored.OwningResourceId};contentType={stored.ContentType};sizeBytes={stored.SizeBytes}"),
            cancellationToken);

        return new StoredFileReference(stored.Id, stored.FileName, stored.ContentType, stored.SizeBytes);
    }

    public async Task<OpenedFile> OpenAsync(Guid reference, CancellationToken cancellationToken = default)
    {
        // Tenant-filtered read: a foreign-tenant or soft-deleted row is already invisible here, so a
        // miss is an honest NOT_FOUND that leaks nothing about other tenants (Req 7.2).
        var file = await db.Set<StoredFile>()
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == reference, cancellationToken)
            ?? throw new NotFoundException("The requested file was not found.");

        // Defense-in-depth tenant check on the resolved row (Req 7.2, 63.4).
        authorizationGuard.EnsureCommunityAuthorized(file.CommunityId);

        // Caller must hold View on the owning resource type (Req 7.2, 63.4).
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, file.OwningResourceType, cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view this file.");
        }

        var content = await blobStore.OpenAsync(file.StorageKey, cancellationToken)
            ?? throw new NotFoundException("The requested file was not found.");

        return new OpenedFile(file.FileName, file.ContentType, file.SizeBytes, content);
    }

    private bool IsAllowedContentType(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType) &&
        _options.AllowedContentTypes is { Count: > 0 } &&
        _options.AllowedContentTypes.Any(ct => string.Equals(ct, contentType, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Concrete <see cref="AppException"/> for the file boundary codes (PAYLOAD_TOO_LARGE /
/// UNSUPPORTED_MEDIA_TYPE) that have no dedicated semantic subclass yet. Carries a safe,
/// client-facing message that the global exception middleware maps to the right status.
/// </summary>
public sealed class AppExceptionWithCode(ErrorCode code, string message) : AppException(code, message);
