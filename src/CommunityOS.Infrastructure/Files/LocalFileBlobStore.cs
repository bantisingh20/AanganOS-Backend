using CommunityOS.Application.Files;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommunityOS.Infrastructure.Files;

/// <summary>
/// Local-filesystem <see cref="IFileBlobStore"/>: writes file bytes under the configured
/// <see cref="FileStorageOptions.RootPath"/>, a directory that is NOT under the web root and is
/// not statically served (Req 7.4). This is the "local now" side of the cloud seam — a future
/// S3/Azure implementation replaces this class behind <see cref="IFileBlobStore"/> with no change
/// to <see cref="IFileService"/> or any caller (ponytail: local now, seam for cloud).
///
/// <para><b>Opaque keys, no path traversal.</b> The storage key is generated here from a fresh
/// GUID (<c>yyyy/MM/{guid-n}.bin</c>) and never derived from a client-supplied file name, so a
/// client can never influence the physical path. On read, the key is resolved and the final full
/// path is verified to still sit under the storage root before any file is opened — a key
/// containing <c>..</c> or an absolute path can never escape the root (steering: security first).</para>
///
/// <para>IO is async and streamed (<see cref="Stream.CopyToAsync(Stream, CancellationToken)"/>),
/// so files are not buffered whole in memory.</para>
/// </summary>
public sealed class LocalFileBlobStore : IFileBlobStore
{
    private readonly string _root;
    private readonly ILogger<LocalFileBlobStore> _logger;

    public LocalFileBlobStore(IOptions<FileStorageOptions> options, ILogger<LocalFileBlobStore> logger)
    {
        _logger = logger;

        var configured = options.Value.RootPath;
        // Resolve a relative root against the app base directory (outside any wwwroot), and
        // normalise to a rooted, canonical path we can safely containment-check against on read.
        _root = Path.GetFullPath(
            Path.IsPathRooted(configured) ? configured : Path.Combine(AppContext.BaseDirectory, configured));
    }

    public async Task<string> SaveAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Date-sharded, GUID-named key — generated, never client-derived (path-traversal safety).
        var now = DateTime.UtcNow;
        var key = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}.bin";

        var fullPath = ResolveWithinRoot(key);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // Stream to disk; FileMode.CreateNew guarantees we never overwrite an existing blob and the
        // async stream copy keeps memory bounded regardless of file size.
        await using (var destination = new FileStream(
            fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
        {
            await content.CopyToAsync(destination, cancellationToken);
        }

        return key;
    }

    public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return Task.FromResult<Stream?>(null);
        }

        string fullPath;
        try
        {
            fullPath = ResolveWithinRoot(storageKey);
        }
        catch (InvalidOperationException)
        {
            // A key that resolves outside the root is treated as "not found" and logged — never
            // throw a path back to the caller, never read outside the root.
            _logger.LogWarning("Rejected file open for a storage key that resolves outside the storage root.");
            return Task.FromResult<Stream?>(null);
        }

        if (!File.Exists(fullPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        return Task.FromResult<Stream?>(stream);
    }

    /// <summary>
    /// Resolve a storage key to a full path and verify it is contained within the storage root.
    /// Throws <see cref="InvalidOperationException"/> if the resolved path escapes the root, so a
    /// malicious or malformed key (<c>..</c> segments, absolute path) can never read/write outside.
    /// </summary>
    private string ResolveWithinRoot(string key)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_root, key));
        var rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Resolved path escapes the configured storage root.");
        }

        return fullPath;
    }
}
