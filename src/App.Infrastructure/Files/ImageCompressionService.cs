using System.Text.RegularExpressions;
using App.Application.Exceptions;
using App.Application.Files;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace App.Infrastructure.Files;

/// <summary>
/// Production image & document compressor using SixLabors.ImageSharp.
/// Automatically handles EXIF rotation, dimension scaling (max 1920x1080 / 1080x1920),
/// progressive quality reduction to ensure output is strictly within 500KB - 1MB,
/// and safe passthrough for non-image files (such as PDFs).
/// </summary>
public sealed partial class ImageCompressionService(ILogger<ImageCompressionService> logger) : IImageCompressionService
{
    private static readonly HashSet<string> CompressibleMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/webp",
        "image/bmp",
        "image/tiff"
    };

    public async Task<CompressedFileResult> CompressImageAsync(
        Stream inputStream,
        string contentType,
        string fileName,
        int maxWidth = 1920,
        int maxHeight = 1080,
        long targetMaxSizeBytes = 500 * 1024,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputStream);

        // If not an image (e.g. PDF, text document), pass through safely without corruption
        if (!IsCompressibleImage(contentType, fileName))
        {
            var len = inputStream.CanSeek ? inputStream.Length : 0;
            return new CompressedFileResult(inputStream, contentType, len, false);
        }

        // Buffer input stream into memory so ImageSharp can inspect and process it
        using var memoryInput = new MemoryStream();
        if (inputStream.CanSeek)
        {
            inputStream.Position = 0;
        }
        await inputStream.CopyToAsync(memoryInput, cancellationToken);
        memoryInput.Position = 0;

        // Try loading with ImageSharp. If decoding fails (corrupted or unrecognized format), pass original safely
        Image image;
        try
        {
            image = await Image.LoadAsync(memoryInput, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to decode image '{FileName}' for compression. Passing through original stream.", fileName);
            memoryInput.Position = 0;
            return new CompressedFileResult(memoryInput, contentType, memoryInput.Length, false);
        }

        using (image)
        {
            // Auto orient based on EXIF (handles photos taken in portrait/landscape on mobile cameras)
            image.Mutate(ctx => ctx.AutoOrient());

            var originalWidth = image.Width;
            var originalHeight = image.Height;

            // Check if dimension resizing is needed
            // If portrait (height > width), swap max bounds to accommodate natural orientation
            var effMaxWidth = maxWidth;
            var effMaxHeight = maxHeight;
            if (originalHeight > originalWidth && effMaxWidth > effMaxHeight)
            {
                (effMaxWidth, effMaxHeight) = (effMaxHeight, effMaxWidth);
            }

            if (originalWidth > effMaxWidth || originalHeight > effMaxHeight)
            {
                image.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    Size = new Size(effMaxWidth, effMaxHeight),
                    Mode = ResizeMode.Max
                }));
            }

            // Encode: if transparent PNG and small enough, keep PNG; otherwise encode as JPEG with quality tuning
            var outputStream = new MemoryStream();
            var outContentType = "image/jpeg";
            var quality = 80;

            // First compression pass
            await image.SaveAsJpegAsync(outputStream, new JpegEncoder
            {
                Quality = quality
            }, cancellationToken);

            // If output still exceeds targetMaxSizeBytes (e.g. 500KB - 1MB), progressively lower quality until within bounds
            while (outputStream.Length > targetMaxSizeBytes && quality > 40)
            {
                quality -= 15;
                outputStream.SetLength(0);
                outputStream.Position = 0;

                await image.SaveAsJpegAsync(outputStream, new JpegEncoder
                {
                    Quality = quality
                }, cancellationToken);
            }

            outputStream.Position = 0;
            logger.LogInformation(
                "Image '{FileName}' compressed from {OriginalBytes} bytes ({OrigW}x{OrigH}) to {NewBytes} bytes ({NewW}x{NewH}, Q={Quality}).",
                fileName, memoryInput.Length, originalWidth, originalHeight, outputStream.Length, image.Width, image.Height, quality);

            return new CompressedFileResult(
                outputStream,
                outContentType,
                outputStream.Length,
                true,
                image.Width,
                image.Height);
        }
    }

    public async Task<CompressedFileResult> CompressBase64Async(
        string base64Data,
        string defaultFileName = "upload.jpg",
        int maxWidth = 1920,
        int maxHeight = 1080,
        long targetMaxSizeBytes = 500 * 1024,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(base64Data))
        {
            throw new ValidationException("image", "Image data is required.");
        }

        var (bytes, declaredContentType) = ParseBase64Data(base64Data);
        using var stream = new MemoryStream(bytes);

        var result = await CompressImageAsync(
            stream,
            declaredContentType,
            defaultFileName,
            maxWidth,
            maxHeight,
            targetMaxSizeBytes,
            cancellationToken);

        return result;
    }

    private static bool IsCompressibleImage(string? contentType, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType) && CompressibleMimeTypes.Contains(contentType))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var ext = Path.GetExtension(fileName);
            if (ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static (byte[] Bytes, string ContentType) ParseBase64Data(string base64Data)
    {
        var raw = base64Data.Trim();
        var contentType = "image/jpeg";

        var comma = raw.IndexOf(',');
        if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
        {
            var header = raw[5..comma];
            var semi = header.IndexOf(';');
            var mime = semi > 0 ? header[..semi] : header;
            if (!string.IsNullOrWhiteSpace(mime))
            {
                contentType = mime.Trim();
            }
            raw = raw[(comma + 1)..];
        }

        try
        {
            var bytes = Convert.FromBase64String(raw);
            return (bytes, contentType);
        }
        catch (FormatException)
        {
            throw new ValidationException("image", "The image is not valid base64 data.");
        }
    }
}
