using System.Diagnostics;

namespace UrbanNest.Api.Middleware;


public sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger,
    UrbanNest.Api.Observability.ApiMetrics metrics)
{
    /// <summary>Header carrying a client/upstream correlation id to reuse when present.</summary>
    public const string CorrelationIdHeader = "X-Correlation-Id";

    /// <summary><see cref="HttpContext.Items"/> key the correlation id is stored under for the request.</summary>
    public const string CorrelationIdKey = "UrbanNest.CorrelationId";

    private readonly RequestDelegate _next = next;
    private readonly ILogger<CorrelationIdMiddleware> _logger = logger;
    private readonly UrbanNest.Api.Observability.ApiMetrics _metrics = metrics;

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
