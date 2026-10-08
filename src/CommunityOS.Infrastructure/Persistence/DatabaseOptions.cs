using System.ComponentModel.DataAnnotations;

namespace CommunityOS.Infrastructure.Persistence;

/// <summary>
/// Strongly-typed database settings bound from configuration (options pattern — no hardcoding).
/// The connection string lives in configuration (appsettings.Development.json / user-secrets /
/// environment), never in source.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>SQL Server connection string. Required at runtime; supplied via configuration.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Seconds before a command times out. Configurable, not a magic literal.</summary>
    [Range(1, 600)]
    public int CommandTimeoutSeconds { get; set; } = 30;
}
