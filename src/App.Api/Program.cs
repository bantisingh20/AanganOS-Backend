using App.Api.Endpoints;
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

builder.Services.AddRequestPipelineSecurity(builder.Configuration);

builder.AddObservability();

builder.Services.AddApiRateLimiting(builder.Configuration);

const string CorsPolicy = "AdminPortal";
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
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

app.MapPipelineDemoEndpoints();

app.MapResidentEndpoints();

app.MapGateEndpoints();
app.MapSecurityOpsEndpoints();

app.MapParcelEndpoints();

app.MapMaintenanceEndpoints();

app.MapAmenityEndpoints();




if (app.Environment.IsDevelopment())
{
    var devSeed = new App.Api.DevSeed.DevSeedOptions();
    app.Configuration.GetSection(App.Api.DevSeed.DevSeedOptions.SectionName).Bind(devSeed);
    if (devSeed.Enabled)
    {
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        await App.Api.DevSeed.DevDataSeeder.SeedAsync(app.Configuration, devSeed, loggerFactory);
    }
}

app.Run();

public partial class Program;

