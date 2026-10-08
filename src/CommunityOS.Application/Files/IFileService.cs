namespace CommunityOS.Application.Files;

/// <summary>
/// File_Service (Req 7). Stores uploaded files in a non-publicly-browsable location bound to an
/// owning resource + CommunityId and returns an opaque reference (Req 7.1), and streams a stored
/// file back only to a caller who holds <c>View</c> on the owning resource and whose authorized
/// communities include the file's CommunityId (Req 7.2, 63.4). It never exposes a direct public
/// URL — bytes are reachable only through <see cref="OpenAsync"/> behind these checks (Req 7.4).
/// </summary>
public interface IFileService
{
    /// <summary>
    /// Validate and store a file (Req 7.1, 7.3). Rejects an oversize payload with
    /// <c>PAYLOAD_TOO_LARGE</c> and a disallowed content type with <c>UNSUPPORTED_MEDIA_TYPE</c>
    /// (limits/allow-list come from <see cref="FileStorageOptions"/> / Config_Service — not
    /// hardcoded). On success persists the bytes to the blob store, writes a tenant-owned
    /// <c>StoredFile</c> metadata row, audits the upload (Req 6.1), and returns the opaque
    /// reference id the owning resource stores.
    /// </summary>
    Task<StoredFileReference> StoreAsync(StoreFileRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Open a stored file for download (Req 7.2, 63.4). Throws <c>NotFoundException</c> when no file
    /// exists for the caller's tenant scope, and <c>ForbiddenException</c> when the caller lacks
    /// <c>View</c> on the owning resource or the file's CommunityId is outside their authorized
    /// communities. On success returns the content metadata plus a readable stream the caller is
    /// responsible for disposing.
    /// </summary>
    Task<OpenedFile> OpenAsync(Guid reference, CancellationToken cancellationToken = default);
}

/// <summary>Input for <see cref="IFileService.StoreAsync"/>: the bytes plus the metadata that binds the file to its owner and community (Req 7.1).</summary>
/// <param name="OwningResourceType">Resource type whose View permission governs the file (e.g. <c>Ticket</c>).</param>
/// <param name="OwningResourceId">The specific owning resource instance id, if already known.</param>
/// <param name="CommunityId">The community the file belongs to (part of the stored reference, Req 7.1).</param>
/// <param name="FileName">Original client file name — stored as metadata only, never used as a path (Req 7.4).</param>
/// <param name="ContentType">Declared MIME type, validated against the configured allow-list (Req 7.3).</param>
/// <param name="Content">The file bytes, read from the current position to the end.</param>
public readonly record struct StoreFileRequest(
    string OwningResourceType,
    Guid? OwningResourceId,
    Guid CommunityId,
    string FileName,
    string ContentType,
    Stream Content);

/// <summary>The opaque reference returned by <see cref="IFileService.StoreAsync"/> (Req 7.1).</summary>
/// <param name="Id">The stored-file reference id the owning resource persists.</param>
/// <param name="FileName">The stored original file name.</param>
/// <param name="ContentType">The validated content type.</param>
/// <param name="SizeBytes">The stored size in bytes.</param>
public readonly record struct StoredFileReference(Guid Id, string FileName, string ContentType, long SizeBytes);

/// <summary>A file opened for authorized download (Req 7.2): content metadata plus the readable byte stream.</summary>
/// <param name="FileName">Original file name for the download.</param>
/// <param name="ContentType">The content type to serve.</param>
/// <param name="SizeBytes">The size in bytes.</param>
/// <param name="Content">The readable stream over the bytes; the caller disposes it.</param>
public sealed record OpenedFile(string FileName, string ContentType, long SizeBytes, Stream Content);
