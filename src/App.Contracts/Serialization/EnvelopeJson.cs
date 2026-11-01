using System.Text.Json;

namespace App.Contracts.Serialization;

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

    /// <summary>Apply the envelope naming/null policy onto an existing options instance (e.g. MVC's).</summary>
    public static void CopyInto(JsonSerializerOptions target)
    {
        target.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        target.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        target.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    }
}
