using App.Contracts.Errors;

namespace App.Contracts.Envelope;

/// <summary>
/// The consistent response envelope every API response uses so clients handle success and
/// failure uniformly (Req 9.1, 9.6, 63.1). Success carries <see cref="Data"/>; failure carries
/// <see cref="Error"/>; both always carry a <see cref="CorrelationId"/> (Req 10.1).
/// </summary>
/// <typeparam name="T">The success payload type (a resource or a <see cref="PagedData{T}"/>).</typeparam>
public sealed class ApiResult<T>
{
    public bool Success { get; init; }

    public T? Data { get; init; }

    public ApiError? Error { get; init; }

    public string CorrelationId { get; init; } = string.Empty;

    public static ApiResult<T> Ok(T data, string correlationId) => new()
    {
        Success = true,
        Data = data,
        Error = null,
        CorrelationId = correlationId,
    };

    public static ApiResult<T> Fail(ApiError error, string correlationId) => new()
    {
        Success = false,
        Data = default,
        Error = error,
        CorrelationId = correlationId,
    };
}

/// <summary>
/// Non-generic helpers for building error envelopes where the success payload type is unknown
/// (e.g. the global exception middleware). Serializes to the same shape with <c>data: null</c>.
/// </summary>
public static class ApiResult
{
    public static ApiResult<object> Fail(ApiError error, string correlationId)
        => ApiResult<object>.Fail(error, correlationId);
}
