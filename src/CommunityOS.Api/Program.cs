using CommunityOS.Api.Endpoints;
using CommunityOS.Api.Middleware;
using CommunityOS.Api.Observability;
using CommunityOS.Api.Security;
using CommunityOS.Api.Validation;
using CommunityOS.Application.Abstractions;
using CommunityOS.Application.Abstractions.Security;
using CommunityOS.Contracts.Serialization;
using CommunityOS.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Shared camelCase envelope serialization options, configured once and injected (not hardcoded
// at call sites). Consumed by the exception middleware; later controllers reuse the same shape.
builder.Services.AddSingleton(EnvelopeJson.CreateOptions());

// Data layer: EF Core (SQL Server), audit interceptor, fail-closed default tenant context.
// Connection string comes from configuration (Database:ConnectionString), never source.
builder.Services.AddInfrastructure(builder.Configuration);

// Request-pipeline security: JWT bearer authentication (config-driven), principal-derived
// request-scoped ITenantContext (replaces the fail-closed default), permission-evaluator seam.
builder.Services.AddRequestPipelineSecurity(builder.Configuration);

// Observability (Task 8.1): structured JSON logging with sink-level secret redaction (Req 10.1,
// 10.4), lightweight API metrics (Req 65.6), and app + database health checks (Req 10.3). The
// logging/health/metrics policy is config-driven (Observability + Logging:LogLevel sections).
builder.AddObservability();

// Rate limiting (Task 8.2, Req 65.5): two config-driven fixed-window policies â€” a strict "auth"
// policy for login/forgot/reset (brute-force / enumeration targets) and a general "write" policy
// for mutating endpoints â€” partitioned by authenticated user id else client IP. A rejection renders
// the RATE_LIMITED envelope (429) with no limiter internals leaked. Limits come from the
// "RateLimiting" section (no hardcoded permit counts / windows).
builder.Services.AddApiRateLimiting(builder.Configuration);

// CORS for the browser-based superadmin portal (CommunityOS-Admin). Origins come from the "Cors"
// config section (no wildcard — credentials/auth header require explicit origins). The portal sends
// the Authorization header and reads X-Correlation-Id, so both are allowed/exposed. Same-origin
// callers (mobile app over native fetch) are unaffected; CORS only gates browser cross-origin calls.
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

// --- Middleware pipeline, outermost â†’ innermost (design "Request lifecycle"): ---
// exception envelope â†’ correlation â†’ authentication â†’ tenant context (DI) â†’ authorization â†’
// validation (endpoint filter) â†’ endpoints.

// 0. Terminal exception envelope â€” outermost so it wraps everything (Req 9.1, 10.2).
app.UseExceptionEnvelope();

// 1. Correlation id established once; shared via HttpContext + response header + log scope (Req 10.1).
app.UseCorrelationId();

// 1b. CORS — before HTTPS redirect/auth so the browser preflight (OPTIONS) is answered directly and
//     isn't 307-redirected or rejected by the permission gate. Only the configured portal origins
//     pass; everything else gets no CORS headers (browser blocks it).
app.UseCors(CorsPolicy);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// 2. Authentication â€” validate the bearer token and build the ClaimsPrincipal. The request-scoped
//    ITenantContext (3) is then derived from that principal on first use by the data layer.
app.UseAuthentication();

// 3. Authorization â€” framework pass + our API-level permission gate (401/403 plumbing, Req 1.5/4.2).
app.UseAuthorization();
app.UsePermissionAuthorization();

// 4. Rate limiting (Task 8.2, Req 65.5). After authentication so the per-user partition key is
//    available (falls back to client IP for anonymous/auth endpoints); endpoints opt in to the
//    "auth"/"write" policies via RequireRateLimiting. A rejected request short-circuits here with
//    the RATE_LIMITED envelope (429), still wrapped by the outer exception/correlation stages.
app.UseRateLimiter();

// Health checks (Task 8.1, Req 10.3): liveness (app up), readiness (app + DB connection) and an
// aggregate endpoint reporting both. Public, lightweight (DB check is a cheap CanConnect), paths
// come from configuration. Replaces the old ad-hoc /health stub.
app.MapObservabilityEndpoints();

// Demo endpoints exercise the protected + validation seams end-to-end for the integration tests.
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
    var devSeed = new CommunityOS.Api.DevSeed.DevSeedOptions();
    app.Configuration.GetSection(CommunityOS.Api.DevSeed.DevSeedOptions.SectionName).Bind(devSeed);
    if (devSeed.Enabled)
    {
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        await CommunityOS.Api.DevSeed.DevDataSeeder.SeedAsync(app.Configuration, devSeed, loggerFactory);
    }
}

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;

