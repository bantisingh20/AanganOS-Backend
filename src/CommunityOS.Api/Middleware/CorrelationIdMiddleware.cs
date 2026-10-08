using System.Diagnostics;

namespace CommunityOS.Api.Middleware;

/// <summary>
/// First functional stage of the pipeline (just inside the terminal exception envelope).
/// Establishes the request's correlation id <b>once</b>: reuse the incoming
/// <c>X-Correlation-Id</c> header when present and non-empty, otherwise generate one. The id is
/// then shared three ways so everything downstream agrees on it:
/// <list type="bullet">
///   <item><description><see cref="HttpContext.Items"/> under <see cref="CorrelationIdKey"/> — the exception middleware reads it from here instead of generating its own.</description></item>
///   <item><description>the <c>X-Correlation-Id</c> response header (traceability back to the client, Req 10.1).</description></item>
///   <item><description>an <see cref="ILogger"/> scope so every log line for this request carries <c>correlationId</c>.</description></item>
/// </list>
/// Emits one structured log entry per request on completion with correlation id, endpoint, acting
/// user (when authenticated) and the outcome status code (Req 10.1), and records the request's
/// outcome + duration to <see cref="ApiMetrics"/> for API metrics (Req 65.6).
///
/// Secret redaction is enforced at the log sink by the Task 8.1 redacting logger provider, so even
/// though this stage logs only method/path/status/user/timing (never bodies, headers or tokens),
/// any accidental secret in a log line is scrubbed on the way out.
/// </summary>
public sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger,
    CommunityOS.Api.Observability.ApiMetrics metrics)
{
    /// <summary>Header carrying a client/upstream correlation id to reuse when present.</summary>
    public const string CorrelationIdHeader = "X-Correlation-Id";

    /// <summary><see cref="HttpContext.Items"/> key the correlation id is stored under for the request.</summary>
    public const string CorrelationIdKey = "CommunityOS.CorrelationId";

    private readonly RequestDelegate _next = next;
    private readonly ILogger<CorrelationIdMiddleware> _logger = logger;
    private readonly CommunityOS.Api.Observability.ApiMetrics _metrics = metrics;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveIncomingCorrelationId(context) ?? Guid.NewGuid().ToString();

        context.Items[CorrelationIdKey] = correlationId;
        context.Response.Headers[CorrelationIdHeader] = correlationId;

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["correlationId"] = correlationId,
        });

        var timestamp = Stopwatch.GetTimestamp();
        try
        {
            await _next(context);
        }
        finally
        {
            // Acting user resolved only when authentication succeeded; anonymous requests log no user.
            var userId = context.User.Identity?.IsAuthenticated == true
                ? context.User.FindFirst("sub")?.Value
                  ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                : null;

            var method = context.Request.Method;
            // Prefer the matched route template over the raw path for metric cardinality (no ids);
            // fall back to the path for the human-readable log line.
            var routeTemplate = context.GetEndpoint()?.DisplayName ?? context.Request.Path.Value ?? "unknown";
            var statusCode = context.Response.StatusCode;
            var elapsedMs = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;

            _logger.LogInformation(
                "Request {Method} {Endpoint} by {ActingUser} completed with {StatusCode} in {ElapsedMs} ms (correlationId {CorrelationId})",
                method,
                context.Request.Path.Value,
                userId ?? "anonymous",
                statusCode,
                (int)elapsedMs,
                correlationId);

            _metrics.RecordRequest(method, routeTemplate, statusCode, elapsedMs);
        }
    }

    /// <summary>Reads the correlation id stored for the current request, if the middleware ran.</summary>
    public static string? GetCorrelationId(HttpContext context)
        => context.Items.TryGetValue(CorrelationIdKey, out var value) ? value as string : null;

    private static string? ResolveIncomingCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out var incoming))
        {
            var value = incoming.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
