namespace CommunityOS.Contracts.Errors;

/// <summary>
/// Single source of truth mapping each <see cref="ErrorCode"/> to its stable wire string and
/// HTTP status. Kept as explicit entries (no magic literals scattered across the codebase) so
/// the envelope serializer and the exception middleware both resolve codes the same way.
/// </summary>
public static class ErrorCodes
{
    /// <summary>The stable wire string for a code (e.g. <c>VALIDATION_ERROR</c>).</summary>
    public static string ToWire(ErrorCode code) => code switch
    {
        ErrorCode.ValidationError => "VALIDATION_ERROR",
        ErrorCode.Unauthenticated => "UNAUTHENTICATED",
        ErrorCode.Forbidden => "FORBIDDEN",
        ErrorCode.NotFound => "NOT_FOUND",
        ErrorCode.Conflict => "CONFLICT",
        ErrorCode.IdempotentReplay => "IDEMPOTENT_REPLAY",
        ErrorCode.RateLimited => "RATE_LIMITED",
        ErrorCode.PayloadTooLarge => "PAYLOAD_TOO_LARGE",
        ErrorCode.UnsupportedMediaType => "UNSUPPORTED_MEDIA_TYPE",
        ErrorCode.IntegrationFailure => "INTEGRATION_FAILURE",
        ErrorCode.InternalError => "INTERNAL_ERROR",
        _ => "INTERNAL_ERROR",
    };

    /// <summary>The HTTP status code the API returns for a given error code (design error table).</summary>
    public static int ToHttpStatus(ErrorCode code) => code switch
    {
        ErrorCode.ValidationError => 400,
        ErrorCode.Unauthenticated => 401,
        ErrorCode.Forbidden => 403,
        ErrorCode.NotFound => 404,
        ErrorCode.Conflict => 409,
        // Replay returns 200 with the original stored result (design error table, Req 44.2/63.3).
        ErrorCode.IdempotentReplay => 200,
        ErrorCode.RateLimited => 429,
        ErrorCode.PayloadTooLarge => 413,
        ErrorCode.UnsupportedMediaType => 415,
        ErrorCode.IntegrationFailure => 502,
        ErrorCode.InternalError => 500,
        _ => 500,
    };
}
