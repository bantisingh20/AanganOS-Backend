using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace App.Api.Observability;

/// <summary>
/// Lightweight API metrics (Req 10.1, 65.6) using the built-in <see cref="Meter"/> API — no heavy
/// observability stack. Records request count and duration tagged by method/endpoint/status, which
/// any metrics backend (OpenTelemetry exporter, Prometheus, dotnet-counters) can scrape later
/// without touching call sites. A singleton; the <see cref="Meter"/> name is the stable instrument
/// scope.
///
/// Job-monitoring signals (background worker outcomes) and security-event signals flow through the
/// same structured-log channel; this meter is the API request-metrics surface specifically.
/// </summary>
public sealed class ApiMetrics : IDisposable
{
    /// <summary>Instrument scope name a metrics listener subscribes to.</summary>
    public const string MeterName = "App.Api";

    private readonly Meter _meter;
    private readonly Counter<long> _requests;
    private readonly Histogram<double> _requestDuration;

    public ApiMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);
        _requests = _meter.CreateCounter<long>(
            "communityos.api.requests",
            unit: "{request}",
            description: "Count of API requests processed, tagged by method, endpoint and status.");
        _requestDuration = _meter.CreateHistogram<double>(
            "communityos.api.request.duration",
            unit: "ms",
            description: "API request processing duration in milliseconds.");
    }

    /// <summary>Records one completed request's outcome and timing (no secrets in tags — route/method/status only).</summary>
    public void RecordRequest(string method, string endpoint, int statusCode, double elapsedMs)
    {
        var tags = new TagList
        {
            { "method", method },
            { "endpoint", endpoint },
            { "status", statusCode },
        };
        _requests.Add(1, tags);
        _requestDuration.Record(elapsedMs, tags);
    }

    public void Dispose() => _meter.Dispose();
}
