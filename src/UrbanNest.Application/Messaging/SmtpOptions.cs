namespace UrbanNest.Application.Messaging;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public bool IsEnabled { get; init; }

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 587;

    public string User { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string FromName { get; init; } = string.Empty;

    public bool EnableSsl { get; init; } = true;
}
