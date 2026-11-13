using System.Text.Json.Serialization;

namespace App.Contracts.Errors;

/// <summary>
/// The <c>error</c> payload of a failed response envelope: a stable machine code, a safe human
/// message, and (for validation) per-field reasons (Req 9.2, 10.2).
/// </summary>
public sealed class ApiError
{
    [JsonConverter(typeof(ErrorCodeJsonConverter))]
    public ErrorCode Code { get; init; }

    public string Message { get; init; } = string.Empty;

    /// <summary>Per-field validation reasons; omitted from JSON when there are none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<FieldError>? FieldErrors { get; init; }
}
