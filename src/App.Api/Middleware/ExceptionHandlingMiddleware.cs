using System.Text.Json;
using App.Application.Exceptions;
using App.Contracts.Envelope;
using App.Contracts.Errors;
using App.Contracts.Serialization;

namespace App.Api.Middleware;


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
