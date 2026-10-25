namespace UrbanNest.Application.Querying;

/// <summary>
/// Resolves the effective default and maximum page size for the current request. The default
/// implementation reads the platform-wide <see cref="PaginationOptions"/> from configuration;
/// this seam lets Config_Service (Task 3.1) later return per-community overrides without the
/// shared query helper knowing or caring where the numbers come from.
/// </summary>
public interface IPageSizePolicy
{
    /// <summary>The page size to use when the client did not request one.</summary>
    int DefaultPageSize { get; }

    /// <summary>The ceiling a requested page size is clamped to (Req 9.4).</summary>
    int MaxPageSize { get; }
}
