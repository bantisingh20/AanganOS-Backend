using System.ComponentModel.DataAnnotations;

namespace App.Application.Community;

/// <summary>
/// Bounds for bulk Unit import/export, bound from configuration (options pattern — not a hardcoded
/// literal; Req 21.4). <see cref="MaxRowsPerOperation"/> is the ceiling on the number of rows a
/// single import or export may process: a file with more rows is rejected up front
/// (PAYLOAD_TOO_LARGE) and an export is capped to this many rows, so bulk work stays bounded under
/// load (Req 65.2).
/// </summary>
public sealed class BulkOptions
{
    public const string SectionName = "Bulk";

    [Range(1, 1_000_000)]
    public int MaxRowsPerOperation { get; set; } = 5_000;
}
