using App.Application.Messaging;

namespace App.Infrastructure.Messaging;

public sealed class NotConfiguredSmsChannelSender : ISmsChannelSender
{
    public Task<ChannelSendResult> SendAsync(string toPhone, string text, CancellationToken cancellationToken = default)
        => Task.FromResult(ChannelSendResult.PermanentFailure("SMS channel is not configured (no provider wired)."));
}

public sealed class NotConfiguredWhatsAppChannelSender : IWhatsAppChannelSender
{
    public Task<ChannelSendResult> SendAsync(string toPhone, string text, CancellationToken cancellationToken = default)
        => Task.FromResult(ChannelSendResult.PermanentFailure("WhatsApp channel is not configured (no provider wired)."));
}
