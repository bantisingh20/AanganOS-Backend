namespace App.Application.Messaging;

public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    public bool IsEnabled { get; init; }

    public string Provider { get; init; } = "GenericHttp"; // "GenericHttp", "Twilio", "Fast2SMS", "Msg91"

    public string ApiUrl { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public string SenderId { get; init; } = string.Empty;

    public string AccountSid { get; init; } = string.Empty;

    public string AuthToken { get; init; } = string.Empty;

    public string FromNumber { get; init; } = string.Empty;

    public string HttpMethod { get; init; } = "POST";
}
