using System.ComponentModel.DataAnnotations;

namespace UrbanNest.Application.Notifications;


public sealed class NotificationOptions
{
    public const string SectionName = "Notification";

    /// <summary>OneSignal application id. From configuration/secrets — never source. Used by the Task 7.2 client.</summary>
    public string OneSignalAppId { get; init; } = string.Empty;

    /// <summary>OneSignal REST API key. From configuration/secrets — never source, never logged. Used by the Task 7.2 client.</summary>
    public string OneSignalApiKey { get; init; } = string.Empty;


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

    public bool RunOutboxWorkerInApi { get; init; }


    public bool OptionalCategoriesAllowedByDefault { get; init; } = true;
}
