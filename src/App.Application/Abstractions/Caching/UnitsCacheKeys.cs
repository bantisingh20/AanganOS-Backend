namespace App.Application.Abstractions.Caching;

/// <summary>
/// Shared cache-key builders for the units list, so every reader and writer uses the exact same
/// generation-token names (keeping invalidation and reads in sync). Keep this the single source.
/// </summary>
public static class UnitsCacheKeys
{
    public const string SuperGeneration = "units:gen:super";

    public static string CommunityGeneration(Guid communityId) => $"units:gen:{communityId}";
}
