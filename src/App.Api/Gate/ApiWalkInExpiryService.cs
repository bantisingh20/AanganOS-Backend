using App.Application.Abstractions;
using App.Application.Gate;
using App.Infrastructure;
using App.Infrastructure.Tenancy;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace App.Api.Gate;

public sealed class ApiWalkInExpiryService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiWalkInExpiryService> _logger;
    private ServiceProvider? _provider;

    public ApiWalkInExpiryService(IConfiguration configuration, ILogger<ApiWalkInExpiryService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    private int PollIntervalSeconds => _configuration.GetValue<int?>("WalkInExpiryWorker:PollIntervalSeconds") ?? 60;

    private int BatchSize => _configuration.GetValue<int?>("WalkInExpiryWorker:BatchSize") ?? 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(_configuration);
        services.RemoveAll<ITenantContext>();
        services.AddScoped<ITenantContext, SuperAdminTenantContext>();
        _provider = services.BuildServiceProvider();

        var pollInterval = TimeSpan.FromSeconds(PollIntervalSeconds);
        _logger.LogInformation(
            "In-API walk-in expiry sweep started (batch {BatchSize}, poll {PollSeconds}s).",
            BatchSize, PollIntervalSeconds);

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
                _logger.LogError(ex, "In-API walk-in expiry sweep pass failed; backing off.");
                expired = 0;
            }

            if (expired < BatchSize)
            {
                await DelaySafelyAsync(pollInterval, stoppingToken);
            }
        }

        _logger.LogInformation("In-API walk-in expiry sweep stopping.");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }
    }

    private async Task<int> SweepBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _provider!.CreateScope();
        var gate = scope.ServiceProvider.GetRequiredService<IGateService>();
        return await gate.ExpirePendingWalkInsAsync(BatchSize, cancellationToken);
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
