using System.ComponentModel.DataAnnotations;

namespace UrbanNest.Application.Messaging;

public sealed class MessageOutboxWorkerOptions
{
    public const string SectionName = "MessageOutboxWorker";

    [Range(1, 3600)]
    public int PollIntervalSeconds { get; init; } = 10;

    [Range(1, 1000)]
    public int BatchSize { get; init; } = 100;

    [Range(1, 50)]
    public int MaxDeliveryAttempts { get; init; } = 5;

    [Range(1, 86400)]
    public int RetryBackoffSeconds { get; init; } = 60;

    public bool RunInApi { get; init; }
}
