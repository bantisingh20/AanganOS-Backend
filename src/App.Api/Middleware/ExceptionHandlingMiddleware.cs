using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using App.Application.Abstractions.Security;
using App.Application.Exceptions;
using App.Contracts.Envelope;
using App.Contracts.Errors;
using App.Contracts.Serialization;
using App.Domain.Diagnostics;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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

        if (!context.Response.Headers.ContainsKey(CorrelationIdHeader))
        {
            context.Response.Headers[CorrelationIdHeader] = correlationId;
        }

        // Enable buffering so request body can be logged in case of exceptions
        context.Request.EnableBuffering();

        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation("Request {Path} was canceled by the client (correlationId {CorrelationId})",
                context.Request.Path.Value, correlationId);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 499;
            }
        }
        catch (AppException ex)
        {
            _logger.LogWarning(ex, "Handled application exception {Code} (correlationId {CorrelationId})",
                ErrorCodes.ToWire(ex.Code), correlationId);

            var statusCode = ErrorCodes.ToHttpStatus(ex.Code);
            await PersistErrorLogAsync(context, ex, statusCode, correlationId, _logger);

            var error = new ApiError
            {
                Code = ex.Code,
                Message = ex.Message,
                FieldErrors = ex.FieldErrors,
            };
            await WriteEnvelopeAsync(context, statusCode, error, correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception (correlationId {CorrelationId})", correlationId);

            const int statusCode = 500;
            await PersistErrorLogAsync(context, ex, statusCode, correlationId, _logger);

            var error = new ApiError
            {
                Code = ErrorCode.InternalError,
                Message = "An unexpected error occurred.",
            };
            await WriteEnvelopeAsync(context, statusCode, error, correlationId);
        }
    }

    private static async Task PersistErrorLogAsync(
        HttpContext context,
        Exception ex,
        int statusCode,
        string correlationId,
        ILogger logger)
    {
        try
        {
            var st = new StackTrace(ex, true);
            StackFrame? frame = null;
            foreach (var f in st.GetFrames() ?? [])
            {
                if (!string.IsNullOrEmpty(f.GetFileName()))
                {
                    frame = f;
                    break;
                }
            }
            frame ??= st.GetFrame(0);

            var fileName = frame?.GetFileName();
            var lineNumber = frame?.GetFileLineNumber();
            if (lineNumber == 0) lineNumber = null;

            var method = frame?.GetMethod();
            var controllerOrFunction = method != null
                ? $"{method.DeclaringType?.Name}.{method.Name}"
                : null;

            Guid? userId = null;
            var uidClaim = context.User.FindFirst(ClaimNames.UserId)?.Value
                        ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(uidClaim, out var u))
            {
                userId = u;
            }

            var userName = context.User.FindFirst(ClaimTypes.Name)?.Value
                        ?? context.User.FindFirst(ClaimTypes.Email)?.Value
                        ?? context.User.FindFirst("name")?.Value;

            var userRole = context.User.FindFirst(ClaimTypes.Role)?.Value
                        ?? context.User.FindFirst("role")?.Value;

            Guid? organizationId = null;
            if (Guid.TryParse(context.User.FindFirst(ClaimNames.OrganizationId)?.Value, out var o))
            {
                organizationId = o;
            }

            Guid? communityId = null;
            if (Guid.TryParse(context.User.FindFirst(ClaimNames.CommunityId)?.Value, out var c))
            {
                communityId = c;
            }

            string? requestBody = null;
            if (context.Request.ContentLength is > 0 and < 65536 && context.Request.Body.CanSeek)
            {
                context.Request.Body.Position = 0;
                using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
                requestBody = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0;

                if (!string.IsNullOrEmpty(requestBody))
                {
                    requestBody = Regex.Replace(
                        requestBody,
                        @"(""(?:password|adminPassword|token|secret|otp)""\s*:\s*"")[^""]*("")",
                        "$1***REDACTED***$2",
                        RegexOptions.IgnoreCase);
                }
            }

            var errorLog = new ErrorLog
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = DateTime.UtcNow,
                CorrelationId = correlationId,
                ApiPath = context.Request.Path.Value ?? string.Empty,
                HttpMethod = context.Request.Method,
                StatusCode = statusCode,
                ExceptionType = ex.GetType().FullName ?? ex.GetType().Name,
                ErrorMessage = ex.Message,
                StackTrace = ex.ToString(),
                FileName = fileName,
                ControllerOrFunction = controllerOrFunction,
                LineNumber = lineNumber,
                UserId = userId,
                UserName = userName,
                UserRole = userRole,
                OrganizationId = organizationId,
                CommunityId = communityId,
                RequestBody = requestBody,
                QueryParams = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null,
                ClientIp = context.Connection.RemoteIpAddress?.ToString(),
                UserAgent = context.Request.Headers.UserAgent.ToString(),
            };

            var dbOptions = context.RequestServices.GetService<DbContextOptions<AppDbContext>>();
            if (dbOptions != null)
            {
                using var db = new AppDbContext(dbOptions, CurrentUser.System);
                db.ErrorLogs.Add(errorLog);
                await db.SaveChangesAsync();
            }
        }
        catch (Exception saveEx)
        {
            logger.LogError(saveEx, "Failed to persist error log to database (correlationId {CorrelationId})", correlationId);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
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
