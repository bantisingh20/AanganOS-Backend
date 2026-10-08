using System.Text.Json;
using CommunityOS.Application.Exceptions;
using CommunityOS.Contracts.Envelope;
using CommunityOS.Contracts.Errors;
using CommunityOS.Contracts.Serialization;

namespace CommunityOS.Api.Middleware;

/// <summary>
/// Terminal middleware that catches unhandled exceptions and renders the standard error
/// envelope. Known <see cref="AppException"/>s map to their carried <see cref="ErrorCode"/>,
/// HTTP status, and (for validation) per-field reasons. Any other exception is logged with the
/// correlation id and collapsed to a generic INTERNAL_ERROR whose body exposes no stack trace,
/// SQL, or internal message (Req 10.2). Every response carries a correlationId (Req 10.1).
///
/// The correlation id is established once by <see cref="CorrelationIdMiddleware"/> and read back
/// from <see cref="HttpContext.Items"/>; when this middleware runs alone (e.g. unit tests with no
/// correlation stage) it falls back to reusing the incoming header or generating one.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    /// <summary>Header carrying a client/upstream correlation id to reuse when present.</summary>
    public const string CorrelationIdHeader = "X-Correlation-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly JsonSerializerOptions _json;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        JsonSerializerOptions json)
    {
        _next = next;
        _logger = logger;
        _json = json;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        // Make the correlation id observable on the response headers too (traceability, Req 10.1).
        if (!context.Response.Headers.ContainsKey(CorrelationIdHeader))
        {
            context.Response.Headers[CorrelationIdHeader] = correlationId;
        }

        try
        {
            await _next(context);
        }
        catch (AppException ex)
        {
            // Known, semantic failure: safe to surface its code + client-facing message.
            _logger.LogWarning(ex, "Handled application exception {Code} (correlationId {CorrelationId})",
                ErrorCodes.ToWire(ex.Code), correlationId);

            var error = new ApiError
            {
                Code = ex.Code,
                Message = ex.Message,
                FieldErrors = ex.FieldErrors,
            };
            await WriteEnvelopeAsync(context, ErrorCodes.ToHttpStatus(ex.Code), error, correlationId);
        }
        catch (Exception ex)
        {
            // Unknown fault: log full detail server-side; return nothing revealing to the client.
            _logger.LogError(ex, "Unhandled exception (correlationId {CorrelationId})", correlationId);

            var error = new ApiError
            {
                Code = ErrorCode.InternalError,
                Message = "An unexpected error occurred.",
            };
            await WriteEnvelopeAsync(context, ErrorCodes.ToHttpStatus(ErrorCode.InternalError), error, correlationId);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        // Prefer the id the correlation middleware established for this request so logs, headers
        // and the envelope all agree. Fall back to the incoming header / a fresh id when this
        // middleware runs without the correlation stage (unit tests).
        if (CorrelationIdMiddleware.GetCorrelationId(context) is { Length: > 0 } established)
        {
            return established;
        }

        if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out var incoming))
        {
            var value = incoming.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return Guid.NewGuid().ToString();
    }

    private async Task WriteEnvelopeAsync(HttpContext context, int statusCode, ApiError error, string correlationId)
    {
        if (context.Response.HasStarted)
        {
            // Response already flushed; can't rewrite it. Log and bail to avoid a second fault.
            _logger.LogError("Cannot write error envelope; response already started (correlationId {CorrelationId})",
                correlationId);
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        var envelope = ApiResult.Fail(error, correlationId);
        await context.Response.WriteAsJsonAsync(envelope, _json);
    }
}
