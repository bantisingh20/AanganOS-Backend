namespace App.Application.Abstractions.Caching;

/// <summary>
/// Provider-agnostic cache seam. A single implementation is selected at startup from
/// <see cref="CacheOptions.Provider"/> (in-process memory or Redis). Services cache read-heavy,
/// rarely-changing data (e.g. the units dropdown) through this interface and never reference a
/// concrete cache, so the backend can scale from one instance to many by flipping config.
///
/// <para>Generation tokens give cheap bulk invalidation when per-key enumeration is impossible: a
/// writer bumps a named generation (scoped per tenant/community) and every cache key that embeds
/// that generation value misses at once. This mirrors the ConfigService pattern and works
/// identically for memory and Redis.</para>
/// </summary>
public interface IAppCache
{
    /// <summary>
    /// Return the cached value for <paramref name="key"/>, or run <paramref name="factory"/>, cache
    /// its result for <paramref name="ttl"/>, and return it. A null factory result is not cached.
    /// </summary>
    Task<T?> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T?>> factory, CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>Remove a single key.</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read the current value of a named generation token, creating it (at 0) if absent. Embed the
    /// returned value in cache keys so bumping the token invalidates them all.
    /// </summary>
    Task<long> GetGenerationAsync(string generationKey, CancellationToken cancellationToken = default);

    /// <summary>Increment a named generation token, invalidating every key that embeds its old value.</summary>
    Task BumpGenerationAsync(string generationKey, CancellationToken cancellationToken = default);
}
