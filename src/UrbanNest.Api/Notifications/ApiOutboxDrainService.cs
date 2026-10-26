using UrbanNest.Application.Abstractions;
using UrbanNest.Application.Notifications;
using UrbanNest.Domain.Notifications;
using UrbanNest.Infrastructure;
using UrbanNest.Infrastructure.Persistence;
using UrbanNest.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace UrbanNest.Api.Notifications;

public sealed class ApiOutboxDrainService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly NotificationOptions _notificationOptions;
    private readonly NotificationWorkerOptions _workerOptions;
    private readonly ILogger<ApiOutboxDrainService> _logger;
    private ServiceProvider? _provider;

    public ApiOutboxDrainService(
        IConfiguration configuration,
        IOptions<NotificationOptions> notificationOptions,
        IOptions<NotificationWorkerOptions> workerOptions,
        ILogger<ApiOutboxDrainService> logger)
    {
        _configuration = configuration;
        _notificationOptions = notificationOptions.Value;
        _workerOptions = workerOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        var services = new ServiceCollection();
        services.AddInfrastructure(_configuration);
        services.RemoveAll<ITenantContext>();
        services.AddScoped<ITenantContext, SuperAdminTenantContext>();
        _provider = services.BuildServiceProvider();

        var pollInterval = TimeSpan.FromSeconds(_workerOptions.PollIntervalSeconds);
        _logger.LogInformation(
            "In-API notification outbox drain started (batch {BatchSize}, poll {PollSeconds}s, max attempts {MaxAttempts}).",
            _workerOptions.BatchSize, _workerOptions.PollIntervalSeconds, _notificationOptions.MaxDeliveryAttempts);

        while (!stoppingToken.IsCancellationRequested)
        {
            int processed;
            try
            {
                processed = await DrainBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A batch-level failure (e.g. DB blip) must not kill the API's background drain.
                _logger.LogError(ex, "In-API outbox drain pass failed; backing off.");
                processed = 0;
            }

            if (processed < _workerOptions.BatchSize)
            {
                await DelaySafelyAsync(pollInterval, stoppingToken);
            }
        }

        _logger.LogInformation("In-API notification outbox drain stopping.");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }
    }

    private async Task<int> DrainBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _provider!.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var dispatcher = sp.GetRequiredService<INotificationDispatcher>();
        var recipientResolver = sp.GetRequiredService<INotificationRecipientResolver>();
        var clock = sp.GetRequiredService<IClock>();

        var now = clock.UtcNow;
        var batchSize = _workerOptions.BatchSize;

        var due = await db.Set<NotificationOutboxMessage>()
            .Where(m => m.Status == OutboxStatus.Pending &&
                        (m.NextAttemptUtc == null || m.NextAttemptUtc <= now))
            .OrderBy(m => m.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
        {
            return 0;
        }

        foreach (var message in due)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await ProcessMessageAsync(message, dispatcher, recipientResolver, clock, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return due.Count;
    }

    private async Task ProcessMessageAsync(
        NotificationOutboxMessage message,
        INotificationDispatcher dispatcher,
        INotificationRecipientResolver recipientResolver,
        IClock clock,
        CancellationToken cancellationToken)
    {
        message.Attempts++;

        var playerIds = string.IsNullOrWhiteSpace(message.TargetPlayerId)
            ? await recipientResolver.ResolvePlayerIdsAsync(message.RecipientUserId, cancellationToken)
            : new[] { message.TargetPlayerId };
        var result = await dispatcher.DispatchAsync(message, playerIds, cancellationToken);

        if (result.Delivered)
        {
            message.Status = OutboxStatus.Sent;
            message.NextAttemptUtc = null;
            message.LastError = null;
            return;
        }

        message.LastError = result.Error;

        var maxAttempts = _notificationOptions.MaxDeliveryAttempts;
        var exhausted = message.Attempts >= maxAttempts;

        if (!result.Transient || exhausted)
        {
            message.Status = OutboxStatus.Failed;
            message.NextAttemptUtc = null;
            _logger.LogWarning(
                "Notification {MessageId} marked Failed after {Attempts}/{MaxAttempts}: {Reason}. Error: {Error}",
                message.Id, message.Attempts, maxAttempts,
                result.Transient ? "attempts exhausted" : "permanent failure", result.Error);
            return;
        }

        message.Status = OutboxStatus.Pending;
        message.NextAttemptUtc = clock.UtcNow.Add(ComputeBackoff(message.Attempts));
        _logger.LogWarning(
            "Notification {MessageId} delivery failed (attempt {Attempts}/{MaxAttempts}); retry at {NextAttemptUtc:o}. Error: {Error}",
            message.Id, message.Attempts, maxAttempts, message.NextAttemptUtc, result.Error);
    }

    private TimeSpan ComputeBackoff(int attempts)
    {
        var baseSeconds = _notificationOptions.RetryBackoffSeconds;
        var exponent = Math.Min(attempts - 1, 20);
        var factor = 1L << exponent;
        var seconds = Math.Min(baseSeconds * (double)factor, TimeSpan.FromDays(7).TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    private static async Task DelaySafelyAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown during the wait — the loop condition exits next.
        }
    }
}
