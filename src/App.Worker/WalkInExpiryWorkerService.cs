using App.Application.Gate;
using Microsoft.Extensions.Options;

namespace App.Worker;

public sealed class WalkInExpiryWorkerService(
    IServiceScopeFactory scopeFactory,
    IOptions<WalkInExpiryWorkerOptions> workerOptions,
    ILogger<WalkInExpiryWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(workerOptions.Value.PollIntervalSeconds);
        logger.LogInformation(
            "Walk-in expiry worker started (batch size {BatchSize}, poll {PollSeconds}s).",
            workerOptions.Value.BatchSize,
            workerOptions.Value.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            int expired;
            try
            {
                expired = await SweepBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Walk-in expiry sweep pass failed; backing off.");
                expired = 0;
            }

            if (expired < workerOptions.Value.BatchSize)
            {
                await DelaySafelyAsync(pollInterval, stoppingToken);
            }
        }

        logger.LogInformation("Walk-in expiry worker stopping.");
    }

    private async Task<int> SweepBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var gate = scope.ServiceProvider.GetRequiredService<IGateService>();
        return await gate.ExpirePendingWalkInsAsync(workerOptions.Value.BatchSize, cancellationToken);
    }

    private static async Task DelaySafelyAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }
}

public sealed class WalkInExpiryWorkerOptions
{
    public const string SectionName = "WalkInExpiryWorker";

    public int PollIntervalSeconds { get; set; } = 60;

    public int BatchSize { get; set; } = 100;
}
