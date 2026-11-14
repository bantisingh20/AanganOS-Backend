namespace App.Application.Files;

/// <summary>
/// Result of an image or document compression operation.
/// </summary>
/// <param name="Stream">The compressed stream (or original stream if not compressible).</param>
/// <param name="ContentType">The MIME content type of the compressed data.</param>
/// <param name="SizeBytes">The byte length of the compressed stream.</param>
/// <param name="WasCompressed">True if compression/resizing was applied, false if passed through.</param>
/// <param name="Width">The resulting image width in pixels if known.</param>
/// <param name="Height">The resulting image height in pixels if known.</param>
public sealed record CompressedFileResult(
    Stream Stream,
    string ContentType,
    long SizeBytes,
    bool WasCompressed,
    int? Width = null,
    int? Height = null);

/// <summary>
/// Compression service to resize and compress photos, resident IDs, visitor images,
/// and documents before persisting to blob storage or DB references, ensuring files remain well
/// under 500KB - 1MB and avoiding memory and storage bloat.
/// </summary>
public interface IImageCompressionService
{
    /// <summary>
    /// Compresses an image stream: auto-rotates by EXIF orientation, resizes within max bounds,
    /// and compresses quality (target &lt; 500KB - 1MB). If non-image (e.g. PDF), passes through safely.
    /// </summary>
    Task<CompressedFileResult> CompressImageAsync(
        Stream inputStream,
        string contentType,
        string fileName,
        int maxWidth = 1920,
        int maxHeight = 1080,
        long targetMaxSizeBytes = 500 * 1024,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decodes a base64 string (raw or data URL), compresses the image, and returns the compressed binary stream.
    /// </summary>
    Task<CompressedFileResult> CompressBase64Async(
        string base64Data,
        string defaultFileName = "upload.jpg",
        int maxWidth = 1920,
        int maxHeight = 1080,
        long targetMaxSizeBytes = 500 * 1024,
        CancellationToken cancellationToken = default);
}
