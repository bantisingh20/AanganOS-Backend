using UrbanNest.Application.Abstractions;

namespace UrbanNest.Infrastructure;

/// <summary>Default <see cref="IClock"/> backed by the system UTC clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
