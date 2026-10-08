using System.Text.Json;
using CommunityOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CommunityOS.Api.Observability;

/// <summary>
/// Composition for Task 8.1 observability: structured + redacted logging, API metrics and
/// app/database health checks. Kept in the Api layer (middleware/logging/health config belongs to
/// the host); the <see cref="SensitiveDataRedactor"/> is reusable and registered for injection.
/// </summary>
public static class ObservabilityExtensions
{
    private const string ReadinessTag = "ready"; // readiness = app + DB
    private const string LivenessTag = "live";   // liveness  = app only

    /// <summary>
    /// Registers observability services and reconfigures logging to emit structured (JSON) output
    /// with secret redaction at the sink (Req 10.1, 10.4, 65.6), plus app + database health checks
    /// (Req 10.3). Call before <c>builder.Build()</c>.
    /// </summary>
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<ObservabilityOptions>()
            .Bind(builder.Configuration.GetSection(ObservabilityOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = builder.Configuration
            .GetSection(ObservabilityOptions.SectionName)
            .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        // Reusable redactor (singleton, immutable). The sensitive-key policy is config-driven.
        var redactor = new SensitiveDataRedactor(options.SensitiveKeys);
        builder.Services.AddSingleton(redactor);

        // --- Logging: structured console + sink-level redaction decorator -------------------------
        // Log LEVELS stay governed by the standard "Logging:LogLevel" config (ClearProviders keeps
        // that filter config, it only drops the provider instances). We re-add either the JSON or
        // simple console, then wrap EVERY provider with the redacting decorator so no sink can emit
        // a password/OTP/token even if some call site logs one by mistake.
        builder.Logging.ClearProviders();
        if (options.UseJsonConsole)
        {
            builder.Logging.AddJsonConsole(json =>
            {
                json.IncludeScopes = true; // so correlationId (a scope) rides every line
            });
        }
        else
        {
            builder.Logging.AddSimpleConsole(simple => simple.IncludeScopes = true);
        }

        // Decorate each already-registered provider with redaction: swap each ILoggerProvider
        // registration for a RedactingLoggerProvider wrapping it, so redaction runs at every sink.
        DecorateLoggerProviders(builder.Services, redactor);

        // --- Metrics ------------------------------------------------------------------------------
        builder.Services.AddMetrics();
        builder.Services.AddSingleton<ApiMetrics>();

        // --- Health checks: self (liveness) + database connection (readiness) ---------------------
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy("API host is running."), tags: [LivenessTag])
            .AddDbContextCheck<AppDbContext>(
                name: "database",
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadinessTag]);

        return builder;
    }

    /// <summary>
    /// Maps the health endpoints: liveness (app up, no DB), readiness (app + DB) and the aggregate
    /// endpoint reporting both. Paths come from configuration (Req 10.3). Call after routing is set
    /// up (these replace the old ad-hoc <c>/health</c>).
    /// </summary>
    public static WebApplication MapObservabilityEndpoints(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<ObservabilityOptions>>().Value;

        app.MapHealthChecks(options.LivenessPath, new HealthCheckOptions
        {
            Predicate = reg => reg.Tags.Contains(LivenessTag),
            ResponseWriter = WriteHealthJsonAsync,
        }).WithName("HealthLiveness");

        app.MapHealthChecks(options.ReadinessPath, new HealthCheckOptions
        {
            Predicate = reg => reg.Tags.Contains(ReadinessTag),
            ResponseWriter = WriteHealthJsonAsync,
        }).WithName("HealthReadiness");

        // Aggregate: all checks (app + DB), so a single probe reports overall operational status.
        app.MapHealthChecks(options.HealthPath, new HealthCheckOptions
        {
            ResponseWriter = WriteHealthJsonAsync,
        }).WithName("Health");

        return app;
    }

    /// <summary>
    /// Writes a compact JSON health payload: overall status plus each check (name + status) so an
    /// operator/orchestrator sees app and database connectivity at a glance. No internal detail
    /// (exception text) is leaked — only the check name and status (Req 10.2 spirit).
    /// </summary>
    private static Task WriteHealthJsonAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
            }),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }

    /// <summary>
    /// Replaces each registered <see cref="Microsoft.Extensions.Logging.ILoggerProvider"/> with a
    /// <see cref="RedactingLoggerProvider"/> wrapping it, so redaction runs at every sink.
    /// </summary>
    private static void DecorateLoggerProviders(IServiceCollection services, SensitiveDataRedactor redactor)
    {
        for (var i = 0; i < services.Count; i++)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType != typeof(Microsoft.Extensions.Logging.ILoggerProvider))
            {
                continue;
            }

            services[i] = ServiceDescriptor.Describe(
                typeof(Microsoft.Extensions.Logging.ILoggerProvider),
                sp =>
                {
                    var inner = (Microsoft.Extensions.Logging.ILoggerProvider)CreateInner(sp, descriptor);
                    return new RedactingLoggerProvider(inner, redactor);
                },
                descriptor.Lifetime);
        }
    }

    private static object CreateInner(IServiceProvider sp, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is not null)
        {
            return descriptor.ImplementationInstance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return descriptor.ImplementationFactory(sp);
        }

        return ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);
    }
}
