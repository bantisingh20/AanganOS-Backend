namespace App.Domain.Diagnostics;

/// <summary>
/// Detailed record of errors and exceptions occurring during backend API calls.
/// Stores exception information (type, message, stack trace, file, function, line number),
/// request metadata (API path, HTTP method, query, body, IP), and user context (user id,
/// organization id, community id, role).
/// </summary>
public class ErrorLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>UTC timestamp when the error occurred.</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Correlation / trace id for tracking across services and logs.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>API endpoint path (e.g. /api/v1/societies/register).</summary>
    public string ApiPath { get; set; } = string.Empty;

    /// <summary>HTTP method (GET, POST, PUT, DELETE, etc.).</summary>
    public string HttpMethod { get; set; } = string.Empty;

    /// <summary>HTTP response status code (e.g. 500, 400, 404).</summary>
    public int StatusCode { get; set; }

    /// <summary>Exception type name (e.g. System.NullReferenceException, AppException).</summary>
    public string ExceptionType { get; set; } = string.Empty;

    /// <summary>Primary error message.</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>Full stack trace if available.</summary>
    public string? StackTrace { get; set; }

    /// <summary>Source code file name where the exception was thrown.</summary>
    public string? FileName { get; set; }

    /// <summary>Controller or function / method name where the exception occurred.</summary>
    public string? ControllerOrFunction { get; set; }

    /// <summary>Source code line number where the exception occurred.</summary>
    public int? LineNumber { get; set; }

    /// <summary>ID of the logged-in user at the time of the error (null if anonymous).</summary>
    public Guid? UserId { get; set; }

    /// <summary>Username, display name or email of the logged-in user.</summary>
    public string? UserName { get; set; }

    /// <summary>Role of the logged-in user (e.g. SuperAdmin, CommunityAdmin).</summary>
    public string? UserRole { get; set; }

    /// <summary>Organization ID associated with the request or user.</summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>Community ID associated with the request or user.</summary>
    public Guid? CommunityId { get; set; }

    /// <summary>Request body / payload summary (with sensitive data redacted).</summary>
    public string? RequestBody { get; set; }

    /// <summary>Query string parameters of the request.</summary>
    public string? QueryParams { get; set; }

    /// <summary>Client IP address.</summary>
    public string? ClientIp { get; set; }

    /// <summary>User agent header of the client.</summary>
    public string? UserAgent { get; set; }
}
