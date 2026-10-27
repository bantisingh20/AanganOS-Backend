using App.Domain.Common;

namespace App.Domain.Messaging;

public class MessageOutboxMessage : PlatformEntity
{
    public MessageChannel Channel { get; set; }

    public string Recipient { get; set; } = string.Empty;

    public string? Subject { get; set; }

    public string Body { get; set; } = string.Empty;

    public MessageStatus Status { get; set; } = MessageStatus.Pending;

    public int Attempts { get; set; }

    public DateTime? NextAttemptUtc { get; set; }

    public string? LastError { get; set; }
}
