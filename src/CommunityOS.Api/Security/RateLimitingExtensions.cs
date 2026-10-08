using System.Threading.RateLimiting;
using CommunityOS.Api.Middleware;
using CommunityOS.Contracts.Envelope;
using CommunityOS.Contracts.Errors;
using CommunityOS.Contracts.Serialization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace CommunityOS.Api.Security;

/// <summary>
/// Configures ASP.NET Core rate limiting (Req 65.5) using the built-in
/// <see cref="Microsoft.AspNetCore.RateLimiting"/> middleware — no new dependency. Two named
/// fixed-window policies are registered; endpoints opt in via <c>.RequireRateLimiting(name)</c>.
///
/// <para><b>Partition key.</b> Each policy partitions by caller identity so one abuser can't exhaust
/// the budget for everyone: the authenticated user id (claim <c>sub</c>) when present, otherwise the
/// client IP. Auth endpoints are pre-authentication, so there they partition by client IP — the
/// right axis for throttling credential-stuffing / enumeration from one source.</para>
///
/// <para><b>Rejection.</b> When a limit is exceeded the <see cref="RateLimiterOptions.OnRejected"/>
/// hook writes the standard RATE_LIMITED envelope at HTTP 429 (consistent with the error model),
/// carrying the correlation id and no internal limiter detail (steering: don't leak internals).</para>
/// </summary>
public static class RateLimitingExtensions
{
    /// <summary>Name of the strict policy applied to login / forgot / reset.</summary>
    public const string AuthPolicy = "auth";

    /// <summary>Name of the general policy applied to mutating endpoints.</summary>
    public const string WritePolicy = "write";

    public static IServiceCollection AddApiRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            var options = configuration
                .GetSection(RateLimitingOptions.SectionName)
                .Get<RateLimitingOptions>() ?? new RateLimitingOptions();

            limiter.RejectionStatusCode = ErrorCodes.ToHttpStatus(ErrorCode.RateLimited); // 429

            limiter.AddPolicy(AuthPolicy, httpContext =>
                CreateFixedWindow(options.Enabled, options.Auth, PartitionKey(httpContext)));

            limiter.AddPolicy(WritePolicy, httpContext =>
                CreateFixedWindow(options.Enabled, options.Write, PartitionKey(httpContext)));

            limiter.OnRejected = WriteRateLimitedEnvelopeAsync;
        });

        return services;
    }

    /// <summary>
    /// Partition by authenticated user id when the caller is authenticated, else by client IP.
    /// Keeps one noisy source (or one account) from consuming another's budget.
    /// </summary>
    private static string PartitionKey(HttpContext httpContext)
    {
        var userId = httpContext.User.Identity?.IsAuthenticated == true
            ? httpContext.User.FindFirst("sub")?.Value
            : null;

        if (!string.IsNullOrEmpty(userId))
        {
            return $"user:{userId}";
        }

        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"ip:{ip}";
    }

    private static RateLimitPartition<string> CreateFixedWindow(
        bool enabled,
        FixedWindowPolicyOptions policy,
        string partitionKey)
    {
        // Disabled host → an effectively unlimited partition (no behavioral limit) so the switch is
        // honored without a separate code path at every endpoint.
        if (!enabled)
        {
            return RateLimitPartition.GetNoLimiter(partitionKey);
        }

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = policy.PermitLimit,
            Window = TimeSpan.FromSeconds(policy.WindowSeconds),
            QueueLimit = policy.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });
    }

    private static async ValueTask WriteRateLimitedEnvelopeAsync(
        OnRejectedContext context,
        CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        if (http.Response.HasStarted)
        {
            return;
        }

        var correlationId = CorrelationIdMiddleware.GetCorrelationId(http)
            ?? http.TraceIdentifier;

        var json = http.RequestServices.GetService<System.Text.Json.JsonSerializerOptions>()
            ?? EnvelopeJson.Default;

        http.Response.StatusCode = ErrorCodes.ToHttpStatus(ErrorCode.RateLimited); // 429
        http.Response.ContentType = "application/json; charset=utf-8";

        var error = new ApiError
        {
            Code = ErrorCode.RateLimited,
            // Generic, no limiter internals (permit counts / window) leaked.
            Message = "Too many requests. Please slow down and try again later.",
        };
        var envelope = ApiResult.Fail(error, correlationId);
        await http.Response.WriteAsJsonAsync(envelope, json, cancellationToken);
    }
}
