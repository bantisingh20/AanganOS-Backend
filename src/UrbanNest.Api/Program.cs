using UrbanNest.Api.Endpoints;
using UrbanNest.Api.Middleware;
using UrbanNest.Api.Observability;
using UrbanNest.Api.Security;
using UrbanNest.Api.Validation;
using UrbanNest.Application.Abstractions;
using UrbanNest.Application.Abstractions.Security;
using UrbanNest.Contracts.Serialization;
using UrbanNest.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddSingleton(EnvelopeJson.CreateOptions());

builder.Services.AddInfrastructure(builder.Configuration);

if (builder.Configuration.GetValue<bool>("Notification:RunOutboxWorkerInApi"))
{
    builder.Services.AddHostedService<UrbanNest.Api.Notifications.ApiOutboxDrainService>();
}

if (builder.Configuration.GetValue<bool>("MessageOutboxWorker:RunInApi"))
{
    builder.Services.AddHostedService<UrbanNest.Infrastructure.Messaging.MessageOutboxDrainService>();
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

app.UseAuthentication();

app.UseAuthorization();
app.UsePermissionAuthorization();

app.UseRateLimiter();

app.MapObservabilityEndpoints();

app.MapPipelineDemoEndpoints();

app.MapConfigEndpoints();

// Audit read endpoint (Task 4.1): tenant-scoped, paginated audit-log query (View-gated).
app.MapAuditEndpoints();
app.MapMessageOutboxEndpoints();
app.MapNotificationOutboxAdminEndpoints();
app.MapMenuEndpoints();
app.MapAdminUserEndpoints();

app.MapFileEndpoints();

app.MapAuthEndpoints();
app.MapOnboardingEndpoints();
app.MapResidentOnboardingEndpoints();
// RBAC management: every endpoint carries [RequirePermission] on its (Action, ResourceType) (Req 4.1).
app.MapUserEndpoints();
app.MapRoleEndpoints();
app.MapPermissionEndpoints();
app.MapUserRoleEndpoints();

app.MapOrganizationEndpoints();
app.MapCommunityEndpoints();
app.MapHierarchyEndpoints();
app.MapUnitEndpoints();

app.MapResidentEndpoints();
app.MapUnitHistoryEndpoints();
app.MapResidentAssetEndpoints();

app.MapGateEndpoints();
app.MapSecurityOpsEndpoints();

app.MapParcelEndpoints();

app.MapHelpdeskEndpoints();
app.MapMaintenanceEndpoints();

app.MapAmenityEndpoints();
app.MapParkingEndpoints();


app.MapAnnouncementEndpoints();
app.MapNotificationEndpoints();

if (app.Environment.IsDevelopment())
{
    var devSeed = new UrbanNest.Api.DevSeed.DevSeedOptions();
    app.Configuration.GetSection(UrbanNest.Api.DevSeed.DevSeedOptions.SectionName).Bind(devSeed);
    if (devSeed.Enabled)
    {
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        await UrbanNest.Api.DevSeed.DevDataSeeder.SeedAsync(app.Configuration, devSeed, loggerFactory);
    }
}

app.Run();

public partial class Program;

