namespace App.Application.Abstractions;

/// <summary>
/// Abstraction over the system clock so audit timestamps are testable (no DateTime.UtcNow
/// scattered through the data layer).
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
