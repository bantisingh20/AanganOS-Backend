using App.Application.Auth;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Auth;

/// <summary>
/// Placeholder <see cref="IOtpDeliverer"/> until Notification_Service (Task 7.1) provides real
/// delivery. It records only that an OTP was dispatched for a (redacted) destination — never the
/// code itself (steering: never log secret values). Swapping in the Notification_Service adapter
/// later is a one-line DI change; Auth_Service does not change.
/// </summary>
public sealed class LoggingOtpDeliverer(ILogger<LoggingOtpDeliverer> logger) : IOtpDeliverer
{
    public Task DeliverAsync(string destination, string code, CancellationToken cancellationToken = default)
    {
        // Redact the contact and NEVER log the code. The code parameter is intentionally unused in
        // the log — it exists so the real deliverer can send it to the user.
        logger.LogInformation(
            "Password-reset OTP generated and queued for delivery to {Destination}.",
            Redact(destination));
        return Task.CompletedTask;
    }

    private static string Redact(string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return "<unknown>";
        }

        // Keep only enough to correlate a support request; mask the rest.
        var trimmed = destination.Trim();
        return trimmed.Length <= 2
            ? "**"
            : $"{trimmed[0]}***{trimmed[^1]}";
    }
}
