using System.ComponentModel.DataAnnotations;

namespace UrbanNest.Application.Querying;

/// <summary>
/// Default and maximum page size, bound from configuration (options pattern — not hardcoded
/// literals; Req 9.4). The <see cref="MaxPageSize"/> is the ceiling a client's requested
/// pageSize is clamped to; <see cref="DefaultPageSize"/> is used when the client omits one.
///
/// This is the platform-wide default. Per-community overrides are a later concern: the shared
/// helper resolves bounds through <see cref="IPageSizePolicy"/>, so Config_Service (Task 3.1)
/// can supply community-specific values without changing the query helper.
/// </summary>
public sealed class PaginationOptions
{
    public const string SectionName = "Pagination";

    [Range(1, 10_000)]
    public int DefaultPageSize { get; set; } = 50;

    [Range(1, 10_000)]
    public int MaxPageSize { get; set; } = 200;
}
