using System.ComponentModel.DataAnnotations;

namespace App.Application.Abstractions.Caching;

/// <summary>
/// Cache configuration bound from the "Cache" appsettings section. <see cref="Provider"/> selects the
/// backend at startup: "Memory" uses an in-process cache (single instance / dev), "Redis" uses a
/// shared distributed cache (multi-instance / scale-out). Callers depend only on
/// <see cref="IAppCache"/>, so switching providers is a config change, not a code change.
/// </summary>
public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    public const string MemoryProvider = "Memory";
    public const string RedisProvider = "Redis";

    [Required]
    public string Provider { get; init; } = MemoryProvider;

    public string? RedisConnectionString { get; init; }

    public string? InstanceName { get; init; } = "aanganos";

    [Range(1, 86400)]
    public int UnitsListTtlSeconds { get; init; } = 300;

    public bool UseRedis => string.Equals(Provider, RedisProvider, StringComparison.OrdinalIgnoreCase);
}
