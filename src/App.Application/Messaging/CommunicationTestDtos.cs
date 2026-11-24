namespace App.Application.Messaging;

public sealed record SmtpConfigSummaryDto(
    bool IsConfigured,
    bool IsEnabled,
    string Host,
    int Port,
    string User,
    string FromName,
    bool EnableSsl,
    bool HasPassword
);

public sealed record SmsConfigSummaryDto(
    bool IsConfigured,
    bool IsEnabled,
    string Provider,
    string ApiUrl,
    string SenderId,
    string FromNumber,
    bool HasApiKey,
    bool HasAuthToken
);

public sealed record CommunicationConfigSummaryDto(
    SmtpConfigSummaryDto Smtp,
    SmsConfigSummaryDto Sms
);

public sealed class TestEmailRequest
{
    public bool UseAppSettings { get; init; } = true;
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? User { get; init; }
    public string? Password { get; init; }
    public string? FromName { get; init; }
    public string? FromEmail { get; init; }
    public bool? EnableSsl { get; init; }
    public string ToEmail { get; init; } = string.Empty;
    public string? Subject { get; init; }
    public string? Body { get; init; }
}

public sealed record TestEmailResponse(
    bool Success,
    string Message,
    long DurationMs,
    string? Details = null,
    string? ServerUsed = null,
    string? FromUsed = null,
    string? ErrorCode = null,
    DateTime? TestedAtUtc = null
);

public sealed class TestSmsRequest
{
    public bool UseAppSettings { get; init; } = true;
    public string? Provider { get; init; }
    public string? ApiUrl { get; init; }
    public string? ApiKey { get; init; }
    public string? SenderId { get; init; }
    public string? AccountSid { get; init; }
    public string? AuthToken { get; init; }
    public string? FromNumber { get; init; }
    public string? HttpMethod { get; init; }
    public string? CustomHeadersJson { get; init; }
    public string? CustomBodyTemplate { get; init; }
    public string ToPhone { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed record TestSmsResponse(
    bool Success,
    string Message,
    long DurationMs,
    int? HttpStatusCode = null,
    string? ResponseBody = null,
    string? Details = null,
    string? ProviderUsed = null,
    DateTime? TestedAtUtc = null
);

public interface ICommunicationTestService
{
    Task<CommunicationConfigSummaryDto> GetConfigSummaryAsync(CancellationToken ct = default);
    Task<TestEmailResponse> TestEmailAsync(TestEmailRequest request, CancellationToken ct = default);
    Task<TestSmsResponse> TestSmsAsync(TestSmsRequest request, CancellationToken ct = default);
}
