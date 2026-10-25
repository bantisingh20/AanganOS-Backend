using System.ComponentModel.DataAnnotations;

namespace UrbanNest.Application.Notifications;

/// <summary>
/// Notification_Service tunables bound from the <c>Notification</c> configuration section (options
/// pattern — nothing hardcoded, steering). Secrets (OneSignal app id / REST API key) live here but
/// are populated from configuration/secrets (user-secrets / environment variables), never committed
/// in source. The retry policy is consumed by the Task 7.2 worker (Req 11.4); it lives here so the
/// policy is config-driven, not baked into the worker.
/// </summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notification";

    /// <summary>OneSignal application id. From configuration/secrets — never source. Used by the Task 7.2 client.</summary>
    public string OneSignalAppId { get; init; } = string.Empty;

    /// <summary>OneSignal REST API key. From configuration/secrets — never source, never logged. Used by the Task 7.2 client.</summary>
    public string OneSignalApiKey { get; init; } = string.Empty;

    /// <summary>
    /// OneSignal REST API base address. Overridable from config (e.g. to point at a sandbox/mock in
    /// tests) rather than baked into the HTTP handler. Default is the production REST endpoint.
    /// </summary>
    [Url]
    public string OneSignalApiBaseUrl { get; init; } = "https://api.onesignal.com/";

    /// <summary>Per-request HTTP timeout, in seconds, for the OneSignal client. Default 30.</summary>
    [Range(1, 600)]
    public int HttpTimeoutSeconds { get; init; } = 30;

    /// <summary>Max delivery attempts before the worker marks a message Failed (Req 11.4). Default 5.</summary>
    [Range(1, 50)]
    public int MaxDeliveryAttempts { get; init; } = 5;

    /// <summary>Base backoff, in seconds, between retry attempts (Req 11.4). Default 60.</summary>
    [Range(1, 86400)]
    public int RetryBackoffSeconds { get; init; } = 60;

    /// <summary>
    /// Default opt-in behavior for an OPTIONAL category when a resident has no stored preference and
    /// Task 12.6's preference store is not yet filling the seam. True = allow (don't silently drop);
    /// false = require explicit opt-in. Mandatory categories ignore this (Req 20.4). Default true.
    /// </summary>
    public bool OptionalCategoriesAllowedByDefault { get; init; } = true;
}
