using CommunityOS.Contracts.Errors;

namespace CommunityOS.Application.Exceptions;

/// <summary>Caller is not authenticated. Maps to UNAUTHENTICATED / 401 (Req 1.5).</summary>
public sealed class UnauthenticatedException : AppException
{
    public UnauthenticatedException(string? message = null)
        : base(ErrorCode.Unauthenticated, message ?? "Authentication is required.")
    {
    }
}

/// <summary>
/// Caller lacks permission, or the record is outside their authorized communities — both are
/// treated as forbidden with no state change. Maps to FORBIDDEN / 403 (Req 4.2, 5.3).
/// </summary>
public sealed class ForbiddenException : AppException
{
    public ForbiddenException(string? message = null)
        : base(ErrorCode.Forbidden, message ?? "You do not have permission to perform this action.")
    {
    }
}

/// <summary>Requested resource does not exist within the caller's scope. Maps to NOT_FOUND / 404.</summary>
public sealed class NotFoundException : AppException
{
    public NotFoundException(string? message = null)
        : base(ErrorCode.NotFound, message ?? "The requested resource was not found.")
    {
    }
}

/// <summary>
/// Concurrency (row-version mismatch) or uniqueness conflict; the caller may retry.
/// Maps to CONFLICT / 409 (Req 22, 14.4).
/// </summary>
public sealed class ConflictException : AppException
{
    public ConflictException(string? message = null)
        : base(ErrorCode.Conflict, message ?? "The operation conflicts with the current state of the resource.")
    {
    }
}

/// <summary>
/// An <c>Idempotency-Key</c> was reused for the same endpoint but with a different request payload.
/// This is a client error — replaying the stored result would answer a different question — so it
/// surfaces as a conflict the caller must resolve (use a fresh key). Maps to CONFLICT / 409
/// (Req 63.3). Carries no stored result or internal detail.
/// </summary>
public sealed class IdempotencyKeyReuseException : AppException
{
    public IdempotencyKeyReuseException(string? message = null)
        : base(ErrorCode.Conflict,
            message ?? "The idempotency key was already used for a different request.")
    {
    }
}

/// <summary>An external integration failed. Maps to INTEGRATION_FAILURE / 502 (Req 62.3).</summary>
public sealed class IntegrationFailureException : AppException
{
    public IntegrationFailureException(string? message = null)
        : base(ErrorCode.IntegrationFailure, message ?? "An upstream integration failed.")
    {
    }
}
