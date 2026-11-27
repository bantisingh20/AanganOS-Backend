using App.Api.Endpoints;
using App.Api.Gate;
using App.Api.Middleware;
using App.Api.Observability;
using App.Api.Security;
using App.Api.Validation;
using App.Application.Abstractions;
using App.Application.Abstractions.Security;
using App.Contracts.Serialization;
using App.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddSingleton(EnvelopeJson.CreateOptions());

// MVC controllers (HMS-style API layer). Controller JSON uses the same envelope serializer options,
// and an invalid model state is converted into the SAME ValidationException the minimal-API filter
// threw, so the VALIDATION_ERROR envelope is identical across both surfaces during migration.
builder.Services
    .AddControllers(options => options.SuppressAsyncSuffixInActionNames = false)
    .AddJsonOptions(options => EnvelopeJson.CopyInto(options.JsonSerializerOptions));

builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var fieldErrors = context.ModelState
            .Where(kvp => kvp.Value is { Errors.Count: > 0 })
            .SelectMany(kvp => kvp.Value!.Errors.Select(e => new App.Contracts.Errors.FieldError(
                ToCamelCase(kvp.Key),
                string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage)))
            .ToList();
        throw new App.Application.Exceptions.ValidationException(fieldErrors);

        static string ToCamelCase(string name) =>
            string.IsNullOrEmpty(name) || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name[1..];
    };
});

builder.Services.AddInfrastructure(builder.Configuration);

if (builder.Configuration.GetValue<bool>("Notification:RunOutboxWorkerInApi"))
{
    builder.Services.AddHostedService<App.Api.Notifications.ApiOutboxDrainService>();
}

if (builder.Configuration.GetValue<bool>("MessageOutboxWorker:RunInApi"))
{
    builder.Services.AddHostedService<App.Infrastructure.Messaging.MessageOutboxDrainService>();
}

if (builder.Configuration.GetValue<bool?>("WalkInExpiryWorker:RunInApi") ?? true)
{
    builder.Services.AddHostedService<App.Api.Gate.ApiWalkInExpiryService>();
}

builder.Services.AddJwtLogin();

builder.AddObservability();

builder.Services.AddApiRateLimiting(builder.Configuration);

// Server-side RBAC switch for [RequirePermission] endpoints (Security:EnforcePermissions).
var permissionEnforcement = PermissionEnforcementOptions.FromConfiguration(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(permissionEnforcement);

const string CorsPolicy = "AdminPortal";
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
// Only the admin website needs CORS (the Android app calls the API natively through CapacitorHttp).
// Production must list the admin site's origin, e.g. env var Cors__AllowedOrigins__0=https://admin.example.com
// or appsettings.Production.json. Localhost-only origins outside Development mean the admin site is blocked.
var corsLocalOnly = corsOrigins.All(o => o.Contains("localhost", StringComparison.OrdinalIgnoreCase) || o.Contains("127.0.0.1", StringComparison.Ordinal));
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins)
                .AllowAnyHeader()
                .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
                .WithExposedHeaders("X-Correlation-Id");
        }
    });
});


var app = builder.Build();

if (!app.Environment.IsDevelopment() && corsLocalOnly)
{
    app.Logger.LogWarning("CORS allows only localhost origins in {Environment}; the admin website will be blocked. Set Cors:AllowedOrigins (e.g. Cors__AllowedOrigins__0).", app.Environment.EnvironmentName);
}

if (permissionEnforcement.EnforcePermissions)
{
    app.Logger.LogInformation("Security:EnforcePermissions = true in {Environment}: [RequirePermission] endpoints return 403 without a matching role grant.", app.Environment.EnvironmentName);
}
else
{
    app.Logger.LogWarning("Security:EnforcePermissions = false in {Environment}: RBAC is AUDIT ONLY. Denials are logged as '[RBAC audit] would deny' but requests are allowed.", app.Environment.EnvironmentName);
}

app.UseExceptionEnvelope();

app.UseCorrelationId();

app.UseCors(CorsPolicy);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseAuthentication();

app.UseAuthorization();
app.UsePermissionAuthorization();

app.UseRateLimiter();

app.MapControllers();

app.MapObservabilityEndpoints();

// Demo endpoints (/pipeline-demo/*) are for local testing only.
if (app.Environment.IsDevelopment())
{
    app.MapPipelineDemoEndpoints();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<App.Infrastructure.Persistence.AppDbContext>();
    await db.Database.EnsureCreatedAsync();

    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await App.Infrastructure.Persistence.DatabaseMigrationRunner.RunPendingMigrationsAsync(db, logger);
}

app.Run();

public partial class Program;

