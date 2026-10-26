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

// Shared camelCase envelope serialization options, configured once and injected (not hardcoded
// at call sites). Consumed by the exception middleware; later controllers reuse the same shape.
builder.Services.AddSingleton(EnvelopeJson.CreateOptions());

// Data layer: EF Core (SQL Server), audit interceptor, fail-closed default tenant context.
// Connection string comes from configuration (Database:ConnectionString), never source.
builder.Services.AddInfrastructure(builder.Configuration);

// Opt-in: run the notification-outbox drain INSIDE the API (Notification:RunOutboxWorkerInApi=true)
// so a single hosted API delivers push when the host can't run a separate worker process (e.g. IIS).
// The drain uses its own isolated super-admin-scoped provider (see ApiOutboxDrainService); the
// request pipeline's tenant scoping is untouched. Default off — the dedicated UrbanNest.Worker drains.
if (builder.Configuration.GetValue<bool>("Notification:RunOutboxWorkerInApi"))
{
    builder.Services.AddHostedService<UrbanNest.Api.Notifications.ApiOutboxDrainService>();
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

// Config / master-data endpoints (Task 3.1): config get/set + master-data list/update.
app.MapConfigEndpoints();

// Audit read endpoint (Task 4.1): tenant-scoped, paginated audit-log query (View-gated).
app.MapAuditEndpoints();

// File_Service endpoints (Task 6.1): multipart upload + authorized streaming download. No static
// file route exists â€” bytes are reachable only through these permission-checked endpoints (Req 7.4).
app.MapFileEndpoints();

// Identity/RBAC/Auth endpoints (Task 5.4) â€” HTTP surface over the 5.1/5.2/5.3 services.
// Auth: login/forgot/reset are anonymous (no permission metadata); logout is authenticated-only.
app.MapAuthEndpoints();
// Public self-service onboarding: anonymous create-your-own-community + admin (community-scoped).
app.MapOnboardingEndpoints();
// Resident self-onboarding (mobile): check-identifier, public community/wing/unit search, submit a
// join request (anonymous); admin list/approve/reject (permission-gated on JoinRequest).
app.MapResidentOnboardingEndpoints();
// RBAC management: every endpoint carries [RequirePermission] on its (Action, ResourceType) (Req 4.1).
app.MapUserEndpoints();
app.MapRoleEndpoints();
app.MapPermissionEndpoints();
app.MapUserRoleEndpoints();

// Community / Property module endpoints (Task 11.3) â€” HTTP surface over the 11.1/11.2 services.
// Every endpoint carries [RequirePermission] on its (Action, ResourceType); mutations use the
// "write" rate-limit policy; list endpoints are paginated and scoped to the caller's authorized
// communities by the global tenant filter (Req 12.4, 13.4, 14.5).
app.MapOrganizationEndpoints();
app.MapCommunityEndpoints();
app.MapHierarchyEndpoints();
app.MapUnitEndpoints();

// Resident module endpoints (Task 12.7) â€” HTTP surface over the Task 12.1â€“12.6 services. Every
// endpoint carries [RequirePermission] on its (Action, ResourceType) (Req 4.1); verify/reject/
// resubmit need Approve on Resident (Req 17.5); mutations use the "write" rate-limit policy;
// list/history endpoints are tenant-scoped (and resident self-scoped for Vehicle/Pet and resident-
// own communication preferences, Req 66.3) by the global tenant filter.
app.MapResidentEndpoints();
app.MapUnitHistoryEndpoints();
app.MapResidentAssetEndpoints();

// Gate module endpoints (Task 16.4) â€” HTTP surface over the Phase 3 gate services (IGateService,
// ISecurityOpsService). Visit passes / walk-ins / entry-exit / recurring visitors, and the security
// ops register (watchlist / guard shifts / incidents / SOS / access integration). Every endpoint
// carries [RequirePermission] on its seeded (Action, ResourceType); mutations use the "write" rate-
// limit policy; list endpoints are paginated and tenant-scoped (resident self-scoped where the
// entity is IUnitScoped) by the global query filter (Req 23.5, 24.2, 25.4, 26.2, 26.3, 27.x).
app.MapGateEndpoints();
app.MapSecurityOpsEndpoints();

// Parcel module endpoints (Task 18.5) â€” HTTP surface over the Phase 4 Parcel_Service
// (IParcelService). Registration + the configurable status state machine (Req 28.x), custody
// locations + custody assignment (Req 29.x), authorized collector + the verified at-most-once
// handover (the Idempotency-Key header drives at-most-once, Req 30.x) and the append-only custody
// timeline + lost/damaged/returned flows (Req 31.x). Every endpoint carries [RequirePermission] on
// its seeded (Action, ResourceType); mutations use the "write" rate-limit policy; the list endpoint
// is paginated and tenant/self-scoped by the global query filter (Req 28.5, 30.5, 31.5).
app.MapParcelEndpoints();

app.MapHelpdeskEndpoints();
app.MapMaintenanceEndpoints();

app.MapAmenityEndpoints();
app.MapParkingEndpoints();


app.MapAnnouncementEndpoints();

// In-app notification inbox (Task 20261008) — authenticated-only, self-scoped by RecipientUserId.
// Reads the recipient's own transactional-outbox rows (written by the common EnqueueAsync helper)
// as a readable list + read-state; no per-feature save path.
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

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;

