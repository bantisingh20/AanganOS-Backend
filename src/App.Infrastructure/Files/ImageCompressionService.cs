//using System.Text.RegularExpressions;
//using App.Application.Exceptions;
//using App.Application.Files;
//using Microsoft.Extensions.Logging;
//using SkiaSharp;

//namespace App.Infrastructure.Files;

///// <summary>
///// Production image & document compressor using SixLabors.ImageSharp.
///// Automatically handles EXIF rotation, dimension scaling (max 1920x1080 / 1080x1920),
///// progressive quality reduction to ensure output is strictly within 500KB - 1MB,
///// and safe passthrough for non-image files (such as PDFs).
///// </summary>
//public sealed partial class ImageCompressionService(ILogger<ImageCompressionService> logger) : IImageCompressionService
//{
//    private static readonly HashSet<string> CompressibleMimeTypes = new(StringComparer.OrdinalIgnoreCase)
//    {
//        "image/jpeg",
//        "image/jpg",
//        "image/png",
//        "image/webp",
//        "image/bmp",
//        "image/tiff"
//    };

//    public async Task<CompressedFileResult> CompressImageAsync(
//        Stream inputStream,
//        string contentType,
//        string fileName,
//        int maxWidth = 1920,
//        int maxHeight = 1080,
//        long targetMaxSizeBytes = 500 * 1024,
//        CancellationToken cancellationToken = default)
//    {
//        ArgumentNullException.ThrowIfNull(inputStream);

//        // If not an image (e.g. PDF, text document), pass through safely without corruption
//        if (!IsCompressibleImage(contentType, fileName))
//        {
//            var len = inputStream.CanSeek ? inputStream.Length : 0;
//            return new CompressedFileResult(inputStream, contentType, len, false);
//        }

//        // Buffer input stream into memory so ImageSharp can inspect and process it
//        using var memoryInput = new MemoryStream();
//        if (inputStream.CanSeek)
//        {
//            inputStream.Position = 0;
//        }
//        await inputStream.CopyToAsync(memoryInput, cancellationToken);
//        memoryInput.Position = 0;

//        // Try loading with ImageSharp. If decoding fails (corrupted or unrecognized format), pass original safely
//        Image image;
//        try
//        {
//            image = await Image.LoadAsync(memoryInput, cancellationToken);
//        }
//        catch (Exception ex)
//        {
//            logger.LogWarning(ex, "Failed to decode image '{FileName}' for compression. Passing through original stream.", fileName);
//            memoryInput.Position = 0;
//            return new CompressedFileResult(memoryInput, contentType, memoryInput.Length, false);
//        }

//        using (image)
//        {
//            // Auto orient based on EXIF (handles photos taken in portrait/landscape on mobile cameras)
//            image.Mutate(ctx => ctx.AutoOrient());

//            var originalWidth = image.Width;
//            var originalHeight = image.Height;

//            // Check if dimension resizing is needed
//            // If portrait (height > width), swap max bounds to accommodate natural orientation
//            var effMaxWidth = maxWidth;
//            var effMaxHeight = maxHeight;
//            if (originalHeight > originalWidth && effMaxWidth > effMaxHeight)
//            {
//                (effMaxWidth, effMaxHeight) = (effMaxHeight, effMaxWidth);
//            }

//            if (originalWidth > effMaxWidth || originalHeight > effMaxHeight)
//            {
//                image.Mutate(ctx => ctx.Resize(new ResizeOptions
//                {
//                    Size = new Size(effMaxWidth, effMaxHeight),
//                    Mode = ResizeMode.Max
//                }));
//            }

//            // Encode: if transparent PNG and small enough, keep PNG; otherwise encode as JPEG with quality tuning
//            var outputStream = new MemoryStream();
//            var outContentType = "image/jpeg";
//            var quality = 80;

//            // First compression pass
//            await image.SaveAsJpegAsync(outputStream, new JpegEncoder
//            {
//                Quality = quality
//            }, cancellationToken);

//            // If output still exceeds targetMaxSizeBytes (e.g. 500KB - 1MB), progressively lower quality until within bounds
//            while (outputStream.Length > targetMaxSizeBytes && quality > 40)
//            {
//                quality -= 15;
//                outputStream.SetLength(0);
//                outputStream.Position = 0;

//                await image.SaveAsJpegAsync(outputStream, new JpegEncoder
//                {
//                    Quality = quality
//                }, cancellationToken);
//            }

//            outputStream.Position = 0;
//            logger.LogInformation(
//                "Image '{FileName}' compressed from {OriginalBytes} bytes ({OrigW}x{OrigH}) to {NewBytes} bytes ({NewW}x{NewH}, Q={Quality}).",
//                fileName, memoryInput.Length, originalWidth, originalHeight, outputStream.Length, image.Width, image.Height, quality);

//            return new CompressedFileResult(
//                outputStream,
//                outContentType,
//                outputStream.Length,
//                true,
//                image.Width,
//                image.Height);
//        }
//    }

//    public async Task<CompressedFileResult> CompressBase64Async(
//        string base64Data,
//        string defaultFileName = "upload.jpg",
//        int maxWidth = 1920,
//        int maxHeight = 1080,
//        long targetMaxSizeBytes = 500 * 1024,
//        CancellationToken cancellationToken = default)
//    {
//        if (string.IsNullOrWhiteSpace(base64Data))
//        {
//            throw new ValidationException("image", "Image data is required.");
//        }

//        var (bytes, declaredContentType) = ParseBase64Data(base64Data);
//        using var stream = new MemoryStream(bytes);

//        var result = await CompressImageAsync(
//            stream,
//            declaredContentType,
//            defaultFileName,
//            maxWidth,
//            maxHeight,
//            targetMaxSizeBytes,
//            cancellationToken);

//        return result;
//    }

//    private static bool IsCompressibleImage(string? contentType, string? fileName)
//    {
//        if (!string.IsNullOrWhiteSpace(contentType) && CompressibleMimeTypes.Contains(contentType))
//        {
//            return true;
//        }

//        if (!string.IsNullOrWhiteSpace(fileName))
//        {
//            var ext = Path.GetExtension(fileName);
//            if (ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
//                ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
//                ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
//                ext.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
//                ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
//            {
//                return true;
//            }
//        }

//        return false;
//    }

//    public static (byte[] Bytes, string ContentType) ParseBase64Data(string base64Data)
//    {
//        var raw = base64Data.Trim();
//        var contentType = "image/jpeg";

//        var comma = raw.IndexOf(',');
//        if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
//        {
//            var header = raw[5..comma];
//            var semi = header.IndexOf(';');
//            var mime = semi > 0 ? header[..semi] : header;
//            if (!string.IsNullOrWhiteSpace(mime))
//            {
//                contentType = mime.Trim();
//            }
//            raw = raw[(comma + 1)..];
//        }

//        try
//        {
//            var bytes = Convert.FromBase64String(raw);
//            return (bytes, contentType);
//        }
//        catch (FormatException)
//        {
//            throw new ValidationException("image", "The image is not valid base64 data.");
//        }
//    }
//}


using App.Application.Exceptions;
using App.Application.Files;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace App.Infrastructure.Files;

public sealed partial class ImageCompressionService(
    ILogger<ImageCompressionService> logger
) : IImageCompressionService
{
    private static readonly HashSet<string> CompressibleMimeTypes = new(
        StringComparer.OrdinalIgnoreCase)
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

        if (!IsCompressibleImage(contentType, fileName))
        {
            if (inputStream.CanSeek)
                inputStream.Position = 0;

            var length = inputStream.CanSeek ? inputStream.Length : 0;

            return new CompressedFileResult(
                inputStream, contentType, length, false);
        }

        if (maxWidth <= 0 || maxHeight <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxWidth), "Image dimensions must be positive.");

        using var memoryInput = new MemoryStream();

        if (inputStream.CanSeek)
            inputStream.Position = 0;

        await inputStream.CopyToAsync(
            memoryInput, cancellationToken);

        memoryInput.Position = 0;

        SKBitmap decoded;

        try
        {
            using var codec = SKCodec.Create(memoryInput);

            if (codec == null)
                throw new InvalidOperationException("Unsupported image format.");

            decoded = SKBitmap.Decode(codec)
                ?? throw new InvalidOperationException("Image decoding failed.");

            // Apply EXIF orientation.
            var oriented = ApplyExifOrientation(
                decoded, codec.EncodedOrigin);

            decoded.Dispose();
            decoded = oriented;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to decode image '{FileName}'. Passing original file.",
                fileName);

            // Return an independent stream. Do not return memoryInput,
            // because it is disposed when this method exits.
            var fallback = new MemoryStream(
                memoryInput.ToArray(), writable: false);

            return new CompressedFileResult(
                fallback,
                contentType,
                fallback.Length,
                false);
        }

        using (decoded)
        {
            int originalWidth = decoded.Width;
            int originalHeight = decoded.Height;

            // Portrait images use portrait limits.
            int effectiveMaxWidth = maxWidth;
            int effectiveMaxHeight = maxHeight;

            if (originalHeight > originalWidth &&
                effectiveMaxWidth > effectiveMaxHeight)
            {
                (effectiveMaxWidth, effectiveMaxHeight) =
                    (effectiveMaxHeight, effectiveMaxWidth);
            }

            // Resize without changing aspect ratio.
            float scale = Math.Min(
                1f,
                Math.Min(
                    (float)effectiveMaxWidth / decoded.Width,
                    (float)effectiveMaxHeight / decoded.Height));

            int newWidth = Math.Max(
                1, (int)Math.Round(decoded.Width * scale));

            int newHeight = Math.Max(
                1, (int)Math.Round(decoded.Height * scale));

            SKBitmap processed;

            if (newWidth != decoded.Width ||
                newHeight != decoded.Height)
            {
                //processed = decoded.Resize(
                //    new SKImageInfo(newWidth, newHeight),
                //    SKFilterQuality.High)
                //    ?? throw new InvalidOperationException(
                //        "Image resize failed.");
                processed = decoded.Resize(
    new SKImageInfo(newWidth, newHeight),
    new SKSamplingOptions(
        SKFilterMode.Linear,
        SKMipmapMode.Linear))
    ?? throw new InvalidOperationException("Image resize failed.");
            }
            else
            {
                processed = decoded.Copy();
            }

            using (processed)
            using (var image = SKImage.FromBitmap(processed))
            {
                var outputStream = new MemoryStream();

                int quality = 80;

                using var firstEncoding = image.Encode(
                    SKEncodedImageFormat.Jpeg, quality);

                if (firstEncoding == null)
                    throw new InvalidOperationException(
                        "JPEG encoding failed.");

                firstEncoding.SaveTo(outputStream);

                // Reduce JPEG quality until target size is reached
                // or minimum quality of 40 is reached.
                while (outputStream.Length > targetMaxSizeBytes &&
                       quality > 40)
                {
                    quality = Math.Max(40, quality - 15);

                    outputStream.SetLength(0);
                    outputStream.Position = 0;

                    using var encoded = image.Encode(
                        SKEncodedImageFormat.Jpeg, quality);

                    if (encoded == null)
                        throw new InvalidOperationException(
                            "JPEG encoding failed.");

                    encoded.SaveTo(outputStream);
                }

                outputStream.Position = 0;

                logger.LogInformation(
                    "Image '{FileName}' compressed from {OriginalBytes} bytes " +
                    "({OriginalWidth}x{OriginalHeight}) to {NewBytes} bytes " +
                    "({NewWidth}x{NewHeight}, Q={Quality}).",
                    fileName,
                    memoryInput.Length,
                    originalWidth,
                    originalHeight,
                    outputStream.Length,
                    processed.Width,
                    processed.Height,
                    quality);

                return new CompressedFileResult(
                    outputStream,
                    "image/jpeg",
                    outputStream.Length,
                    true,
                    processed.Width,
                    processed.Height);
            }
        }
    }

    private static SKBitmap ApplyExifOrientation(
        SKBitmap source,
        SKEncodedOrigin orientation)
    {
        bool swapDimensions = orientation is
            SKEncodedOrigin.LeftTop or
            SKEncodedOrigin.RightTop or
            SKEncodedOrigin.RightBottom or
            SKEncodedOrigin.LeftBottom;

        int width = source.Width;
        int height = source.Height;

        int outputWidth = swapDimensions ? height : width;
        int outputHeight = swapDimensions ? width : height;

        var result = new SKBitmap(outputWidth, outputHeight);

        using var canvas = new SKCanvas(result);

        var matrix = orientation switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix
            {
                ScaleX = -1,
                ScaleY = 1,
                TransX = width
            },

            SKEncodedOrigin.BottomRight => new SKMatrix
            {
                ScaleX = -1,
                ScaleY = -1,
                TransX = width,
                TransY = height
            },

            SKEncodedOrigin.BottomLeft => new SKMatrix
            {
                ScaleX = 1,
                ScaleY = -1,
                TransY = height
            },

            SKEncodedOrigin.LeftTop => new SKMatrix
            {
                ScaleX = 0,
                SkewX = 1,
                SkewY = 1,
                ScaleY = 0
            },

            SKEncodedOrigin.RightTop => new SKMatrix
            {
                ScaleX = 0,
                SkewX = -1,
                SkewY = 1,
                ScaleY = 0,
                TransX = height
            },

            SKEncodedOrigin.RightBottom => new SKMatrix
            {
                ScaleX = 0,
                SkewX = -1,
                SkewY = -1,
                ScaleY = 0,
                TransX = height,
                TransY = width
            },

            SKEncodedOrigin.LeftBottom => new SKMatrix
            {
                ScaleX = 0,
                SkewX = 1,
                SkewY = -1,
                ScaleY = 0,
                TransY = width
            },

            _ => SKMatrix.Identity
        };

        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();

        return result;
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
            throw new ValidationException(
                "image", "Image data is required.");
        }

        var (bytes, declaredContentType) =
            ParseBase64Data(base64Data);

        using var stream = new MemoryStream(bytes);

        return await CompressImageAsync(
            stream,
            declaredContentType,
            defaultFileName,
            maxWidth,
            maxHeight,
            targetMaxSizeBytes,
            cancellationToken);
    }

    private static bool IsCompressibleImage(
        string? contentType,
        string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            CompressibleMimeTypes.Contains(contentType))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var extension = Path.GetExtension(fileName);

            return new[]
            {
                ".jpg", ".jpeg", ".png", ".webp",
                ".bmp", ".tif", ".tiff"
            }.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        return false;
    }

    public static (byte[] Bytes, string ContentType)
        ParseBase64Data(string base64Data)
    {
        var raw = base64Data.Trim();
        var contentType = "image/jpeg";

        var comma = raw.IndexOf(',');

        if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            && comma > 0)
        {
            var header = raw[5..comma];
            var semi = header.IndexOf(';');

            var mime = semi > 0 ? header[..semi] : header;

            if (!string.IsNullOrWhiteSpace(mime))
                contentType = mime.Trim();

            raw = raw[(comma + 1)..];
        }

        try
        {
            return (Convert.FromBase64String(raw), contentType);
        }
        catch (FormatException)
        {
            throw new ValidationException(
                "image", "The image is not valid base64 data.");
        }
    }
}

