using System.Text.Json;
using System.Text.Json.Serialization;

namespace UrbanNest.Contracts.Errors;

/// <summary>
/// Serializes <see cref="ErrorCode"/> as its stable wire string (e.g. <c>VALIDATION_ERROR</c>)
/// rather than the enum member name or ordinal, so the envelope matches the API contract.
/// </summary>
public sealed class ErrorCodeJsonConverter : JsonConverter<ErrorCode>
{
    public override ErrorCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var wire = reader.GetString();
        foreach (var code in Enum.GetValues<ErrorCode>())
        {
            if (ErrorCodes.ToWire(code) == wire)
            {
                return code;
            }
        }

        return ErrorCode.InternalError;
    }

    public override void Write(Utf8JsonWriter writer, ErrorCode value, JsonSerializerOptions options)
        => writer.WriteStringValue(ErrorCodes.ToWire(value));
}
