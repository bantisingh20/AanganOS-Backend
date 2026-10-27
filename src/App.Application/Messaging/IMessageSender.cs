namespace App.Application.Messaging;

public interface IMessageSender
{
    Task QueueEmailAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default);

    Task QueueSmsAsync(string toPhone, string text, CancellationToken cancellationToken = default);

    Task QueueWhatsAppAsync(string toPhone, string text, CancellationToken cancellationToken = default);
}
