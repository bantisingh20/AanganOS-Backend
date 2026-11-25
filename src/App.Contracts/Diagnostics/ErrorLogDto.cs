namespace App.Contracts.Diagnostics;

public sealed class ErrorLogSummaryDto
{
    public Guid Id { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public string? CorrelationId { get; init; }
    public string ApiPath { get; init; } = string.Empty;
    public string HttpMethod { get; init; } = string.Empty;
    public int StatusCode { get; init; }
    public string ExceptionType { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public string? FileName { get; init; }
    public string? ControllerOrFunction { get; init; }
    public int? LineNumber { get; init; }
    public Guid? UserId { get; init; }
    public string? UserName { get; init; }
    public string? UserRole { get; init; }
    public Guid? OrganizationId { get; init; }
    public Guid? CommunityId { get; init; }
}

public sealed class ErrorLogDetailDto
{
    public Guid Id { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public string? CorrelationId { get; init; }
    public string ApiPath { get; init; } = string.Empty;
    public string HttpMethod { get; init; } = string.Empty;
    public int StatusCode { get; init; }
    public string ExceptionType { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public string? StackTrace { get; init; }
    public string? FileName { get; init; }
    public string? ControllerOrFunction { get; init; }
    public int? LineNumber { get; init; }
    public Guid? UserId { get; init; }
    public string? UserName { get; init; }
    public string? UserRole { get; init; }
    public Guid? OrganizationId { get; init; }
    public Guid? CommunityId { get; init; }
    public string? RequestBody { get; init; }
    public string? QueryParams { get; init; }
    public string? ClientIp { get; init; }
    public string? UserAgent { get; init; }
}
