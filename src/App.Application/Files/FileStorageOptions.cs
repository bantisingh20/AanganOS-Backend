using System.ComponentModel.DataAnnotations;

namespace App.Application.Files;

/// <summary>
/// File_Service limits and storage location, bound from configuration (options pattern — no
/// hardcoded size limits, allow-lists or paths; steering: no hardcoding). The size limit and
/// content-type allow-list back the Req 7.3 boundary checks; <see cref="RootPath"/> is the
/// non-publicly-browsable directory the local blob store writes under (Req 7.4).
/// </summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>
    /// Filesystem root the local blob store writes under. MUST be a location that is NOT statically
    /// served / not under the web root (Req 7.4). Relative paths resolve against the content root.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string RootPath { get; set; } = "App_Data/files";

    /// <summary>Maximum accepted upload size in bytes; larger uploads are rejected PAYLOAD_TOO_LARGE (Req 7.3).</summary>
    [Range(1, long.MaxValue)]
    public long MaxSizeBytes { get; set; } = 25 * 1024 * 1024;

    /// <summary>
    /// Allowed MIME content types. An upload whose content type is not in this list is rejected
    /// UNSUPPORTED_MEDIA_TYPE (Req 7.3). Fail-closed: an empty list rejects everything.
    /// </summary>
    public IReadOnlyList<string> AllowedContentTypes { get; set; } =
    [
        "image/jpeg",
        "image/png",
        "image/webp",
        "application/pdf",
    ];
}
