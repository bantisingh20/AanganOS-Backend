using System.Net;
using System.Net.Mail;
using UrbanNest.Application.Messaging;
using Microsoft.Extensions.Options;

namespace UrbanNest.Infrastructure.Messaging;

public sealed class SmtpEmailChannelSender(IOptions<SmtpOptions> options) : IEmailChannelSender
{
    private readonly SmtpOptions _smtp = options.Value;

    public async Task<ChannelSendResult> SendAsync(string toEmail, string? subject, string body, CancellationToken cancellationToken = default)
    {
        if (!_smtp.IsEnabled)
        {
            return ChannelSendResult.PermanentFailure("Email is disabled (Smtp.IsEnabled = false).");
        }

        if (string.IsNullOrWhiteSpace(_smtp.Host) || string.IsNullOrWhiteSpace(_smtp.User))
        {
            return ChannelSendResult.PermanentFailure("SMTP is not configured (Host/User missing).");
        }

        try
        {
            using var mail = new MailMessage
            {
                From = new MailAddress(_smtp.User, _smtp.FromName),
                Subject = subject ?? string.Empty,
                Body = body,
                IsBodyHtml = true,
            };
            mail.To.Add(toEmail);

            using var client = new SmtpClient(_smtp.Host, _smtp.Port)
            {
                Credentials = new NetworkCredential(_smtp.User, _smtp.Password),
                EnableSsl = _smtp.EnableSsl,
            };

            await client.SendMailAsync(mail, cancellationToken);
            return ChannelSendResult.Success();
        }
        catch (SmtpException ex)
        {
            var transient = ex.StatusCode is SmtpStatusCode.ServiceNotAvailable
                or SmtpStatusCode.MailboxBusy
                or SmtpStatusCode.TransactionFailed
                or SmtpStatusCode.GeneralFailure;
            return transient
                ? ChannelSendResult.TransientFailure(ex.Message)
                : ChannelSendResult.PermanentFailure(ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ChannelSendResult.TransientFailure(ex.Message);
        }
    }
}
