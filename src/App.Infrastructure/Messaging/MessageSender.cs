using App.Application.Abstractions;
using App.Application.Exceptions;
using App.Application.Messaging;
using App.Domain.Messaging;
using App.Infrastructure.Persistence;

namespace App.Infrastructure.Messaging;

public sealed class MessageSender(AppDbContext db, IClock clock) : IMessageSender
{
    public Task QueueEmailAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        var recipient = (toEmail ?? string.Empty).Trim();
        if (recipient.Length == 0 || !recipient.Contains('@', StringComparison.Ordinal))
        {
            throw new ValidationException("toEmail", "A valid email address is required.");
        }

        return QueueAsync(MessageChannel.Email, recipient, subject, body, cancellationToken);
    }

    public Task QueueSmsAsync(string toPhone, string text, CancellationToken cancellationToken = default)
        => QueuePhoneAsync(MessageChannel.Sms, toPhone, text, cancellationToken);

    public Task QueueWhatsAppAsync(string toPhone, string text, CancellationToken cancellationToken = default)
        => QueuePhoneAsync(MessageChannel.WhatsApp, toPhone, text, cancellationToken);

    private Task QueuePhoneAsync(MessageChannel channel, string toPhone, string text, CancellationToken cancellationToken)
    {
        var recipient = (toPhone ?? string.Empty).Trim();
        if (recipient.Length == 0)
        {
            throw new ValidationException("toPhone", "A phone number is required.");
        }

        return QueueAsync(channel, recipient, null, text, cancellationToken);
    }

    private async Task QueueAsync(MessageChannel channel, string recipient, string? subject, string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ValidationException("body", "Message body is required.");
        }

        var message = new MessageOutboxMessage
        {
            Channel = channel,
            Recipient = recipient,
            Subject = subject?.Trim(),
            Body = body,
            Status = MessageStatus.Pending,
            Attempts = 0,
            NextAttemptUtc = null,
            CreatedAtUtc = clock.UtcNow,
        };

        await db.Set<MessageOutboxMessage>().AddAsync(message, cancellationToken);
    }
}
