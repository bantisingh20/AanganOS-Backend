namespace CommunityOS.Contracts.Errors;

/// <summary>
/// Stable, machine-readable error codes returned in the response envelope. Clients branch on
/// these — the string values are part of the public API contract and must not change (Req 9.6,
/// 10.2). HTTP status mapping lives in <see cref="ErrorCodes"/>.
/// </summary>
public enum ErrorCode
{
    /// <summary>One or more request fields failed validation; see <c>fieldErrors</c> (Req 9.2).</summary>
    ValidationError,

    /// <summary>No valid authentication was supplied.</summary>
    Unauthenticated,

    /// <summary>Authenticated but not permitted (missing permission or tenant-isolation violation).</summary>
    Forbidden,

    /// <summary>The requested resource does not exist (within the caller's scope).</summary>
    NotFound,

    /// <summary>Concurrency or uniqueness conflict; the caller may retry.</summary>
    Conflict,

    /// <summary>An idempotent operation was replayed; the original stored result is returned.</summary>
    IdempotentReplay,

    /// <summary>The caller exceeded a configured rate limit.</summary>
    RateLimited,

    /// <summary>The request payload exceeds the allowed size.</summary>
    PayloadTooLarge,

    /// <summary>The request content type is not supported.</summary>
    UnsupportedMediaType,

    /// <summary>An external integration (gateway/provider/IoT) failed.</summary>
    IntegrationFailure,

    /// <summary>Unexpected server fault; body never leaks implementation detail (Req 10.2).</summary>
    InternalError,
}
