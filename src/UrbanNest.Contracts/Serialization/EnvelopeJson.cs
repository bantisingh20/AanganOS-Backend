using System.Text.Json;

namespace UrbanNest.Contracts.Serialization;

/// <summary>
/// Factory for the <see cref="JsonSerializerOptions"/> the response envelope is serialized with:
/// camelCase property names, null fields omitted. Centralized here (rather than configured ad hoc
/// at each call site) so the API pipeline and the exception middleware share one configuration.
/// </summary>
public static class EnvelopeJson
{
    /// <summary>Builds a fresh camelCase options instance callers can register in DI or reuse.</summary>
    public static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>A shared, immutable default instance for convenience.</summary>
    public static JsonSerializerOptions Default { get; } = CreateOptions();
}
