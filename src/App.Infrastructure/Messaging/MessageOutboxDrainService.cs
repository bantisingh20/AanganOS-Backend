using App.Application.Abstractions;
using App.Application.Messaging;
using App.Domain.Messaging;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Messaging;

public sealed class MessageOutboxDrainService(
    IServiceScopeFactory scopeFactory,
    IOptions<MessageOutboxWorkerOptions> workerOptions,
    ILogger<MessageOutboxDrainService> logger) : BackgroundService
{
    private readonly MessageOutboxWorkerOptions _options = workerOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(_options.PollIntervalSeconds);
        logger.LogInformation(
            "Message outbox drain started (batch {BatchSize}, poll {PollSeconds}s, max attempts {MaxAttempts}).",
            _options.BatchSize, _options.PollIntervalSeconds, _options.MaxDeliveryAttempts);

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
                logger.LogError(ex, "Message outbox drain pass failed; backing off.");
                processed = 0;
            }

            if (processed < _options.BatchSize)
            {
                await DelaySafelyAsync(pollInterval, stoppingToken);
            }
        }

        logger.LogInformation("Message outbox drain stopping.");
    }

    private async Task<int> DrainBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var email = sp.GetRequiredService<IEmailChannelSender>();
        var sms = sp.GetRequiredService<ISmsChannelSender>();
        var whatsApp = sp.GetRequiredService<IWhatsAppChannelSender>();
        var clock = sp.GetRequiredService<IClock>();

        var now = clock.UtcNow;

        var due = await db.Set<MessageOutboxMessage>()
            .Where(m => m.Status == MessageStatus.Pending &&
                        (m.NextAttemptUtc == null || m.NextAttemptUtc <= now))
            .OrderBy(m => m.CreatedAtUtc)
            .Take(_options.BatchSize)
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

            await ProcessAsync(message, email, sms, whatsApp, clock, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return due.Count;
    }

    private async Task ProcessAsync(
        MessageOutboxMessage message,
        IEmailChannelSender email,
        ISmsChannelSender sms,
        IWhatsAppChannelSender whatsApp,
        IClock clock,
        CancellationToken cancellationToken)
    {
        message.Attempts++;

        var result = message.Channel switch
        {
            MessageChannel.Email => await email.SendAsync(message.Recipient, message.Subject, message.Body, cancellationToken),
            MessageChannel.Sms => await sms.SendAsync(message.Recipient, message.Body, cancellationToken),
            MessageChannel.WhatsApp => await whatsApp.SendAsync(message.Recipient, message.Body, cancellationToken),
            _ => ChannelSendResult.PermanentFailure("Unknown channel."),
        };

        if (result.Delivered)
        {
            message.Status = MessageStatus.Sent;
            message.NextAttemptUtc = null;
            message.LastError = null;
            return;
        }

        message.LastError = result.Error;

        var exhausted = message.Attempts >= _options.MaxDeliveryAttempts;
        if (!result.Transient || exhausted)
        {
            message.Status = MessageStatus.Failed;
            message.NextAttemptUtc = null;
            logger.LogWarning(
                "Message {MessageId} ({Channel}) marked Failed after {Attempts}/{MaxAttempts}: {Reason}. Error: {Error}",
                message.Id, message.Channel, message.Attempts, _options.MaxDeliveryAttempts,
                result.Transient ? "attempts exhausted" : "permanent failure", result.Error);
            return;
        }

        message.Status = MessageStatus.Pending;
        message.NextAttemptUtc = clock.UtcNow.Add(ComputeBackoff(message.Attempts));
        logger.LogWarning(
            "Message {MessageId} ({Channel}) delivery failed (attempt {Attempts}/{MaxAttempts}); retry at {NextAttemptUtc:o}. Error: {Error}",
            message.Id, message.Channel, message.Attempts, _options.MaxDeliveryAttempts, message.NextAttemptUtc, result.Error);
    }

    private TimeSpan ComputeBackoff(int attempts)
    {
        var baseSeconds = _options.RetryBackoffSeconds;
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
        }
    }
}
