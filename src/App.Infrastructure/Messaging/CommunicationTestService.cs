using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using App.Application.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Messaging;

public sealed class CommunicationTestService : ICommunicationTestService
{
    private readonly SmtpOptions _smtpOptions;
    private readonly SmsOptions _smsOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CommunicationTestService> _logger;

    public CommunicationTestService(
        IOptions<SmtpOptions> smtpOptions,
        IOptions<SmsOptions> smsOptions,
        IHttpClientFactory httpClientFactory,
        ILogger<CommunicationTestService> logger)
    {
        _smtpOptions = smtpOptions.Value;
        _smsOptions = smsOptions.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task<CommunicationConfigSummaryDto> GetConfigSummaryAsync(CancellationToken ct = default)
    {
        var smtpSummary = new SmtpConfigSummaryDto(
            IsConfigured: !string.IsNullOrWhiteSpace(_smtpOptions.Host) && !string.IsNullOrWhiteSpace(_smtpOptions.User),
            IsEnabled: _smtpOptions.IsEnabled,
            Host: _smtpOptions.Host ?? string.Empty,
            Port: _smtpOptions.Port,
            User: _smtpOptions.User ?? string.Empty,
            FromName: _smtpOptions.FromName ?? string.Empty,
            EnableSsl: _smtpOptions.EnableSsl,
            HasPassword: !string.IsNullOrWhiteSpace(_smtpOptions.Password)
        );

        var smsSummary = new SmsConfigSummaryDto(
            IsConfigured: _smsOptions.IsEnabled && (!string.IsNullOrWhiteSpace(_smsOptions.ApiUrl) || !string.IsNullOrWhiteSpace(_smsOptions.AccountSid)),
            IsEnabled: _smsOptions.IsEnabled,
            Provider: string.IsNullOrWhiteSpace(_smsOptions.Provider) ? "GenericHttp" : _smsOptions.Provider,
            ApiUrl: _smsOptions.ApiUrl ?? string.Empty,
            SenderId: _smsOptions.SenderId ?? string.Empty,
            FromNumber: _smsOptions.FromNumber ?? string.Empty,
            HasApiKey: !string.IsNullOrWhiteSpace(_smsOptions.ApiKey),
            HasAuthToken: !string.IsNullOrWhiteSpace(_smsOptions.AuthToken)
        );

        return Task.FromResult(new CommunicationConfigSummaryDto(smtpSummary, smsSummary));
    }

    public async Task<TestEmailResponse> TestEmailAsync(TestEmailRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ToEmail))
        {
            return new TestEmailResponse(
                Success: false,
                Message: "Recipient email (To) is required.",
                DurationMs: 0
            );
        }

        string host;
        int port;
        string user;
        string password;
        string fromName;
        string fromEmail;
        bool enableSsl;

        if (request.UseAppSettings)
        {
            if (!_smtpOptions.IsEnabled)
            {
                return new TestEmailResponse(
                    Success: false,
                    Message: "SMTP is currently disabled in appsettings (Smtp.IsEnabled is false). You can switch to manual credentials or enable it in server config.",
                    DurationMs: 0,
                    ServerUsed: _smtpOptions.Host
                );
            }

            if (string.IsNullOrWhiteSpace(_smtpOptions.Host) || string.IsNullOrWhiteSpace(_smtpOptions.User))
            {
                return new TestEmailResponse(
                    Success: false,
                    Message: "SMTP Host or Username is missing in appsettings. You can switch to manual credentials to test.",
                    DurationMs: 0
                );
            }

            host = _smtpOptions.Host;
            port = _smtpOptions.Port;
            user = _smtpOptions.User;
            password = _smtpOptions.Password;
            fromName = !string.IsNullOrWhiteSpace(_smtpOptions.FromName) ? _smtpOptions.FromName : "AanganOS";
            fromEmail = _smtpOptions.User;
            enableSsl = _smtpOptions.EnableSsl;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Host))
            {
                return new TestEmailResponse(
                    Success: false,
                    Message: "SMTP Host is required when using manual credentials.",
                    DurationMs: 0
                );
            }

            host = request.Host.Trim();
            port = request.Port.GetValueOrDefault(587);
            user = request.User?.Trim() ?? string.Empty;
            password = request.Password ?? string.Empty;
            fromName = !string.IsNullOrWhiteSpace(request.FromName) ? request.FromName.Trim() : "AanganOS";
            fromEmail = !string.IsNullOrWhiteSpace(request.FromEmail) ? request.FromEmail.Trim() : user;
            enableSsl = request.EnableSsl ?? true;
        }

        var sw = Stopwatch.StartNew();

        try
        {
            using var mail = new MailMessage
            {
                From = new MailAddress(string.IsNullOrWhiteSpace(fromEmail) ? "noreply@aanganos.local" : fromEmail, fromName),
                Subject = string.IsNullOrWhiteSpace(request.Subject) ? "AanganOS SMTP Test Email" : request.Subject.Trim(),
                Body = string.IsNullOrWhiteSpace(request.Body)
                    ? $"<h3>AanganOS SMTP Test</h3><p>This is a test email sent from AanganOS Admin at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC to verify email delivery.</p><p><b>Server:</b> {host}:{port}<br/><b>SSL:</b> {enableSsl}</p>"
                    : request.Body,
                IsBodyHtml = true
            };
            mail.To.Add(request.ToEmail.Trim());

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl,
                Timeout = 20000 // 20s timeout
            };

            if (!string.IsNullOrWhiteSpace(user) || !string.IsNullOrWhiteSpace(password))
            {
                client.Credentials = new NetworkCredential(user, password);
            }

            await client.SendMailAsync(mail, ct);
            sw.Stop();

            return new TestEmailResponse(
                Success: true,
                Message: $"Email successfully sent to {request.ToEmail.Trim()}.",
                DurationMs: sw.ElapsedMilliseconds,
                Details: $"Connected to {host}:{port} (SSL: {enableSsl}), authenticated as '{user}', and sent message in {sw.ElapsedMilliseconds}ms.",
                ServerUsed: $"{host}:{port}",
                FromUsed: $"{fromName} <{fromEmail}>",
                TestedAtUtc: DateTime.UtcNow
            );
        }
        catch (SmtpFailedRecipientException ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "SMTP failed recipient to {ToEmail}", request.ToEmail);
            return new TestEmailResponse(
                Success: false,
                Message: $"Mailbox recipient rejected: {ex.Message}",
                DurationMs: sw.ElapsedMilliseconds,
                Details: ex.ToString(),
                ErrorCode: ex.StatusCode.ToString(),
                ServerUsed: $"{host}:{port}",
                FromUsed: fromEmail,
                TestedAtUtc: DateTime.UtcNow
            );
        }
        catch (SmtpException ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "SMTP error connecting to {Host}:{Port}", host, port);
            return new TestEmailResponse(
                Success: false,
                Message: $"SMTP Error: {ex.Message}",
                DurationMs: sw.ElapsedMilliseconds,
                Details: $"StatusCode: {ex.StatusCode}\n{ex}",
                ErrorCode: ex.StatusCode.ToString(),
                ServerUsed: $"{host}:{port}",
                FromUsed: fromEmail,
                TestedAtUtc: DateTime.UtcNow
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Unexpected error testing SMTP to {Host}:{Port}", host, port);
            return new TestEmailResponse(
                Success: false,
                Message: $"Connection failed: {ex.Message}",
                DurationMs: sw.ElapsedMilliseconds,
                Details: ex.ToString(),
                ErrorCode: ex.GetType().Name,
                ServerUsed: $"{host}:{port}",
                FromUsed: fromEmail,
                TestedAtUtc: DateTime.UtcNow
            );
        }
    }

    public async Task<TestSmsResponse> TestSmsAsync(TestSmsRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ToPhone))
        {
            return new TestSmsResponse(
                Success: false,
                Message: "Recipient phone number (To) is required.",
                DurationMs: 0
            );
        }

        string provider;
        string apiUrl;
        string apiKey;
        string senderId;
        string accountSid;
        string authToken;
        string fromNumber;
        string httpMethod;

        if (request.UseAppSettings)
        {
            if (!_smsOptions.IsEnabled)
            {
                return new TestSmsResponse(
                    Success: false,
                    Message: "SMS is not enabled in server appsettings (Sms.IsEnabled is false). Switch to manual credentials to test with custom provider/API keys.",
                    DurationMs: 0
                );
            }

            provider = string.IsNullOrWhiteSpace(_smsOptions.Provider) ? "GenericHttp" : _smsOptions.Provider;
            apiUrl = _smsOptions.ApiUrl ?? string.Empty;
            apiKey = _smsOptions.ApiKey ?? string.Empty;
            senderId = _smsOptions.SenderId ?? string.Empty;
            accountSid = _smsOptions.AccountSid ?? string.Empty;
            authToken = _smsOptions.AuthToken ?? string.Empty;
            fromNumber = _smsOptions.FromNumber ?? string.Empty;
            httpMethod = string.IsNullOrWhiteSpace(_smsOptions.HttpMethod) ? "POST" : _smsOptions.HttpMethod;
        }
        else
        {
            provider = string.IsNullOrWhiteSpace(request.Provider) ? "GenericHttp" : request.Provider.Trim();
            apiUrl = request.ApiUrl?.Trim() ?? string.Empty;
            apiKey = request.ApiKey?.Trim() ?? string.Empty;
            senderId = request.SenderId?.Trim() ?? string.Empty;
            accountSid = request.AccountSid?.Trim() ?? string.Empty;
            authToken = request.AuthToken?.Trim() ?? string.Empty;
            fromNumber = request.FromNumber?.Trim() ?? string.Empty;
            httpMethod = string.IsNullOrWhiteSpace(request.HttpMethod) ? "POST" : request.HttpMethod.Trim().ToUpperInvariant();
        }

        var sw = Stopwatch.StartNew();
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);

        var messageText = string.IsNullOrWhiteSpace(request.Message)
            ? $"AanganOS test SMS verification code: {Random.Shared.Next(100000, 999999)}."
            : request.Message.Trim();

        try
        {
            if (provider.Equals("Twilio", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(accountSid) || string.IsNullOrWhiteSpace(authToken))
                {
                    return new TestSmsResponse(
                        Success: false,
                        Message: "Twilio Account SID and Auth Token are required.",
                        DurationMs: 0,
                        ProviderUsed: "Twilio"
                    );
                }

                var twilioUrl = $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json";
                var formValues = new Dictionary<string, string>
                {
                    ["To"] = request.ToPhone.Trim(),
                    ["From"] = string.IsNullOrWhiteSpace(fromNumber) ? senderId : fromNumber,
                    ["Body"] = messageText
                };

                using var req = new HttpRequestMessage(HttpMethod.Post, twilioUrl)
                {
                    Content = new FormUrlEncodedContent(formValues)
                };

                var authHeaderBytes = Encoding.ASCII.GetBytes($"{accountSid}:{authToken}");
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authHeaderBytes));

                var res = await client.SendAsync(req, ct);
                sw.Stop();

                var body = await res.Content.ReadAsStringAsync(ct);
                var isOk = res.IsSuccessStatusCode;

                return new TestSmsResponse(
                    Success: isOk,
                    Message: isOk ? "Twilio SMS dispatched successfully." : $"Twilio rejected request: {res.StatusCode}",
                    DurationMs: sw.ElapsedMilliseconds,
                    HttpStatusCode: (int)res.StatusCode,
                    ResponseBody: body,
                    Details: $"Twilio API returned HTTP {(int)res.StatusCode} in {sw.ElapsedMilliseconds}ms.",
                    ProviderUsed: "Twilio",
                    TestedAtUtc: DateTime.UtcNow
                );
            }
            else if (provider.Equals("Fast2SMS", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    return new TestSmsResponse(
                        Success: false,
                        Message: "Fast2SMS Authorization API Key is required.",
                        DurationMs: 0,
                        ProviderUsed: "Fast2SMS"
                    );
                }

                var f2sUrl = string.IsNullOrWhiteSpace(apiUrl) ? "https://www.fast2sms.com/dev/bulkV2" : apiUrl;
                using var req = new HttpRequestMessage(HttpMethod.Post, f2sUrl);
                req.Headers.Add("authorization", apiKey);

                var payload = new
                {
                    route = "q",
                    message = messageText,
                    language = "english",
                    flash = 0,
                    numbers = request.ToPhone.Trim()
                };

                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var res = await client.SendAsync(req, ct);
                sw.Stop();
                var body = await res.Content.ReadAsStringAsync(ct);

                return new TestSmsResponse(
                    Success: res.IsSuccessStatusCode,
                    Message: res.IsSuccessStatusCode ? "Fast2SMS message dispatched successfully." : $"Fast2SMS request failed with {res.StatusCode}",
                    DurationMs: sw.ElapsedMilliseconds,
                    HttpStatusCode: (int)res.StatusCode,
                    ResponseBody: body,
                    Details: $"Fast2SMS returned HTTP {(int)res.StatusCode} in {sw.ElapsedMilliseconds}ms.",
                    ProviderUsed: "Fast2SMS",
                    TestedAtUtc: DateTime.UtcNow
                );
            }
            else
            {
                // Generic HTTP (GET or POST)
                if (string.IsNullOrWhiteSpace(apiUrl))
                {
                    return new TestSmsResponse(
                        Success: false,
                        Message: "API Endpoint URL is required for Generic HTTP SMS.",
                        DurationMs: 0,
                        ProviderUsed: "GenericHttp"
                    );
                }

                var interpolatedUrl = apiUrl
                    .Replace("{phone}", Uri.EscapeDataString(request.ToPhone.Trim()), StringComparison.OrdinalIgnoreCase)
                    .Replace("{message}", Uri.EscapeDataString(messageText), StringComparison.OrdinalIgnoreCase)
                    .Replace("{sender}", Uri.EscapeDataString(senderId ?? string.Empty), StringComparison.OrdinalIgnoreCase)
                    .Replace("{apiKey}", Uri.EscapeDataString(apiKey ?? string.Empty), StringComparison.OrdinalIgnoreCase);

                var method = httpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) ? HttpMethod.Get : HttpMethod.Post;
                using var req = new HttpRequestMessage(method, interpolatedUrl);

                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    req.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
                    req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
                }

                if (!string.IsNullOrWhiteSpace(request.CustomHeadersJson))
                {
                    try
                    {
                        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(request.CustomHeadersJson);
                        if (dict != null)
                        {
                            foreach (var (k, v) in dict)
                            {
                                req.Headers.TryAddWithoutValidation(k, v);
                            }
                        }
                    }
                    catch
                    {
                        // Ignore malformed custom headers
                    }
                }

                if (method == HttpMethod.Post)
                {
                    string postBody;
                    string contentType = "application/json";

                    if (!string.IsNullOrWhiteSpace(request.CustomBodyTemplate))
                    {
                        postBody = request.CustomBodyTemplate
                            .Replace("{phone}", request.ToPhone.Trim(), StringComparison.OrdinalIgnoreCase)
                            .Replace("{message}", messageText, StringComparison.OrdinalIgnoreCase)
                            .Replace("{sender}", senderId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                            .Replace("{apiKey}", apiKey ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        var jsonPayload = new
                        {
                            to = request.ToPhone.Trim(),
                            message = messageText,
                            sender = senderId
                        };
                        postBody = JsonSerializer.Serialize(jsonPayload);
                    }

                    req.Content = new StringContent(postBody, Encoding.UTF8, contentType);
                }

                var res = await client.SendAsync(req, ct);
                sw.Stop();
                var body = await res.Content.ReadAsStringAsync(ct);

                return new TestSmsResponse(
                    Success: res.IsSuccessStatusCode,
                    Message: res.IsSuccessStatusCode ? "HTTP SMS request delivered successfully." : $"HTTP SMS gateway returned {(int)res.StatusCode} ({res.ReasonPhrase})",
                    DurationMs: sw.ElapsedMilliseconds,
                    HttpStatusCode: (int)res.StatusCode,
                    ResponseBody: body,
                    Details: $"Sent {method.Method} to {interpolatedUrl}. Received HTTP {(int)res.StatusCode} in {sw.ElapsedMilliseconds}ms.",
                    ProviderUsed: provider,
                    TestedAtUtc: DateTime.UtcNow
                );
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Error testing SMS gateway {Provider}", provider);
            return new TestSmsResponse(
                Success: false,
                Message: $"SMS gateway request failed: {ex.Message}",
                DurationMs: sw.ElapsedMilliseconds,
                Details: ex.ToString(),
                ProviderUsed: provider,
                TestedAtUtc: DateTime.UtcNow
            );
        }
    }
}
