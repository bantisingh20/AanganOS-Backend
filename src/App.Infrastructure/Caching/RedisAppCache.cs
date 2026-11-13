using System.Text;
using System.Text.Json;
using App.Application.Abstractions.Caching;
using Microsoft.Extensions.Caching.Distributed;

namespace App.Infrastructure.Caching;

/// <summary>
/// Distributed <see cref="IAppCache"/> over <see cref="IDistributedCache"/> (Redis). Shared across
/// every API instance, so a write on one instance and a cached read on another stay consistent once
/// a generation token is bumped. Values are stored as UTF-8 JSON. Selected when
/// CacheOptions.Provider = "Redis".
/// </summary>
public sealed class RedisAppCache(IDistributedCache cache) : IAppCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T?>> factory, CancellationToken cancellationToken = default)
        where T : class
    {
        var bytes = await cache.GetAsync(key, cancellationToken);
        if (bytes is { Length: > 0 })
        {
            try
            {
                var cached = JsonSerializer.Deserialize<T>(bytes, JsonOptions);
                if (cached is not null)
                {
                    return cached;
                }
            }
            catch (JsonException)
            {
                // Corrupt/legacy entry — fall through and re-create.
            }
        }

        var value = await factory(cancellationToken);
        if (value is not null)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            await cache.SetAsync(
                key,
                payload,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
                cancellationToken);
        }
        return value;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => cache.RemoveAsync(key, cancellationToken);

    public async Task<long> GetGenerationAsync(string generationKey, CancellationToken cancellationToken = default)
    {
        var bytes = await cache.GetAsync(generationKey, cancellationToken);
        if (bytes is { Length: > 0 } && long.TryParse(Encoding.UTF8.GetString(bytes), out var gen))
        {
            return gen;
        }
        return 0;
    }

    public async Task BumpGenerationAsync(string generationKey, CancellationToken cancellationToken = default)
    {
        var current = await GetGenerationAsync(generationKey, cancellationToken);
        var next = Encoding.UTF8.GetBytes((current + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        await cache.SetAsync(generationKey, next, new DistributedCacheEntryOptions(), cancellationToken);
    }
}
