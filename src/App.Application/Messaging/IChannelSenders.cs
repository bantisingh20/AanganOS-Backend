namespace App.Application.Messaging;

public sealed record ChannelSendResult(bool Delivered, string? Error = null, bool Transient = false)
{
    public static ChannelSendResult Success() => new(true);

    public static ChannelSendResult TransientFailure(string error) => new(false, error, true);

    public static ChannelSendResult PermanentFailure(string error) => new(false, error, false);
}

public interface IEmailChannelSender
{
    Task<ChannelSendResult> SendAsync(string toEmail, string? subject, string body, CancellationToken cancellationToken = default);
}

public interface ISmsChannelSender
{
    Task<ChannelSendResult> SendAsync(string toPhone, string text, CancellationToken cancellationToken = default);
}

public interface IWhatsAppChannelSender
{
    Task<ChannelSendResult> SendAsync(string toPhone, string text, CancellationToken cancellationToken = default);
}
