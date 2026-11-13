using App.Application.Abstractions.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace App.Infrastructure.Caching;

/// <summary>
/// In-process <see cref="IAppCache"/> over <see cref="IMemoryCache"/>. The default backend: zero
/// extra infrastructure, ideal for a single API instance or development. Generation tokens live in
/// the same memory cache. ponytail: fine until the API runs on multiple instances — then switch
/// CacheOptions.Provider to "Redis" (shared cache) with no caller changes.
/// </summary>
public sealed class MemoryAppCache(IMemoryCache cache) : IAppCache
{
    public Task<T?> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T?>> factory, CancellationToken cancellationToken = default)
        where T : class
    {
        if (cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return Task.FromResult<T?>(cached);
        }

        return CreateAsync(key, ttl, factory, cancellationToken);
    }

    private async Task<T?> CreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T?>> factory, CancellationToken cancellationToken)
        where T : class
    {
        var value = await factory(cancellationToken);
        if (value is not null)
        {
            cache.Set(key, value, ttl);
        }
        return value;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cache.Remove(key);
        return Task.CompletedTask;
    }

    public Task<long> GetGenerationAsync(string generationKey, CancellationToken cancellationToken = default)
    {
        var gen = cache.TryGetValue(generationKey, out long g) ? g : 0;
        return Task.FromResult(gen);
    }

    public Task BumpGenerationAsync(string generationKey, CancellationToken cancellationToken = default)
    {
        var gen = cache.TryGetValue(generationKey, out long g) ? g : 0;
        cache.Set(generationKey, gen + 1);
        return Task.CompletedTask;
    }
}
