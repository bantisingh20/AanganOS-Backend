using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using UrbanNest.Application.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UrbanNest.Infrastructure.Notifications;

/// <summary>
/// <see cref="IOneSignalClient"/> backed by the OneSignal REST v1 "Create notification" endpoint
/// (POST https://api.onesignal.com/notifications). Uses a typed <see cref="HttpClient"/> injected via
/// <see cref="IHttpClientFactory"/> so connections are pooled and the socket-exhaustion hazard of
/// per-request <c>new HttpClient()</c> is avoided (Req 65.4).
///
/// <para><b>Security (steering, Req 10.4).</b> The OneSignal app id and REST API key come from
/// <see cref="NotificationOptions"/> (bound from configuration/secrets via the options pattern). The
/// API key is set on the Authorization header per request — it is <b>never</b> logged, never written
/// to a tracked file. The logger uses a redacted placeholder when tracing failures.</para>
///
/// <para><b>Response classification.</b></para>
/// <list type="bullet">
///   <item>2xx → accepted (success). Provider message id extracted when present.</item>
///   <item>429 (Too Many Requests) → transient. The worker retries with backoff.</item>
///   <item>Other 4xx (Bad Request, Unauthorized, etc.) → permanent. Retrying won't help.</item>
///   <item>5xx / network / timeout → transient. The worker retries with backoff.</item>
/// </list>
///
/// <para><b>Phase 1 scope:</b> push channel only. SMS/email/WhatsApp dispatchers are added by later
/// phases (Req 64); they will be additional <c>IOneSignalClient</c>-like seams or a multi-channel
/// dispatcher — the architecture already supports that.</para>
/// </summary>
public sealed class OneSignalHttpClient(
    HttpClient httpClient,
    IOptions<NotificationOptions> options,
    ILogger<OneSignalHttpClient> logger) : IOneSignalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<OneSignalSendResult> SendPushAsync(
        OneSignalPushRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var opts = options.Value;

        var body = new OneSignalCreateNotificationBody
        {
            AppId = opts.OneSignalAppId,
            IncludePlayerIds = request.PlayerIds.ToList(),
            Headings = new LocalizedText { En = request.Title },
            Contents = new LocalizedText { En = request.Body },
            Data = request.DataJson is not null
                ? JsonSerializer.Deserialize<Dictionary<string, object>>(request.DataJson)
                : null,
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "notifications")
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        // Authorization: Basic <REST_API_KEY> — the legacy format still supported by OneSignal and
        // consistent with the player-id targeting field used throughout the codebase.
        httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Basic {opts.OneSignalApiKey}");

        HttpResponseMessage? response = null;
        try
        {
            response = await httpClient.SendAsync(httpRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // Network/DNS/TLS failure — transient.
            logger.LogWarning("OneSignal push failed (network): {Message}", ex.Message);
            return new OneSignalSendResult(false, Error: $"Network error: {ex.Message}", Transient: true);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout — transient.
            logger.LogWarning("OneSignal push timed out");
            return new OneSignalSendResult(false, Error: "Request timed out", Transient: true);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                var result = await TryReadResponseAsync(response, cancellationToken);
                var messageId = result?.Id;
                return new OneSignalSendResult(true, ProviderMessageId: messageId);
            }

            var statusCode = (int)response.StatusCode;
            var errorBody = await SafeReadStringAsync(response, cancellationToken);

            // Log at warning; NEVER include the API key. Include status + truncated error body.
            logger.LogWarning(
                "OneSignal push failed: HTTP {StatusCode}. Response (truncated): {Error}",
                statusCode,
                Truncate(errorBody, 500));

            // 429 = rate limited → transient (retry with backoff).
            // Other 4xx = permanent (bad payload, auth error — retrying won't help).
            // 5xx = transient (provider issue).
            var isTransient = statusCode == (int)HttpStatusCode.TooManyRequests || statusCode >= 500;

            return new OneSignalSendResult(
                false,
                Error: $"HTTP {statusCode}: {Truncate(errorBody, 200)}",
                Transient: isTransient);
        }
    }

    private static async Task<OneSignalCreateNotificationResponse?> TryReadResponseAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<OneSignalCreateNotificationResponse>(JsonOptions, ct);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> SafeReadStringAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            return "(unreadable)";
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "…");

    // ── OneSignal request / response DTOs (internal, not public) ────────────

    private sealed class OneSignalCreateNotificationBody
    {
        public string AppId { get; init; } = string.Empty;
        public List<string> IncludePlayerIds { get; init; } = [];
        public LocalizedText? Headings { get; init; }
        public LocalizedText? Contents { get; init; }
        public Dictionary<string, object>? Data { get; init; }
    }

    private sealed class LocalizedText
    {
        public string En { get; init; } = string.Empty;
    }

    private sealed class OneSignalCreateNotificationResponse
    {
        public string? Id { get; init; }
    }
}
