using System.ComponentModel.DataAnnotations;
using CommunityOS.Api.Security;
using CommunityOS.Api.Validation;
using CommunityOS.Application.Abstractions;
using CommunityOS.Application.Abstractions.Security;
using CommunityOS.Domain.Identity;
using CommunityOS.Contracts.Envelope;
using CommunityOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommunityOS.Api.Endpoints;

/// <summary>
/// Thin demo endpoints that exercise the Task 2.2 pipeline seams end-to-end so the integration
/// tests can prove behaviour without waiting for Phase 2 feature endpoints:
/// <list type="bullet">
///   <item><description><c>GET /pipeline-demo/my-communities</c> — protected (RequirePermission + authentication). Reads the request-scoped <see cref="ITenantContext"/> and queries <see cref="AppDbContext"/> so the authorized-communities-from-claims flow is visible end-to-end (Req 5.1).</description></item>
///   <item><description><c>POST /pipeline-demo/echo</c> — protected + DataAnnotations validation at the trust boundary (Req 9.2).</description></item>
/// </list>
/// ponytail: demo-only surface for the foundation phase; real module endpoints replace it in Phase 2.
/// </summary>
public static class PipelineDemoEndpoints
{
    public static IEndpointRouteBuilder MapPipelineDemoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/pipeline-demo");

        // Protected read: requires authentication + View permission on "Community". Projects the
        // tenant context the pipeline built from the token's claims, plus the row count the global
        // filter actually returns for that scope.
        group.MapGet("/my-communities", async (ITenantContext tenant, AppDbContext db, CancellationToken ct) =>
        {
            var visibleRowCount = await db.SampleTenantRecords.AsNoTracking().CountAsync(ct);

            var payload = new MyCommunitiesResponse(
                tenant.UserId,
                tenant.AuthorizedCommunityIds.ToArray(),
                tenant.IsPlatformSuperAdmin,
                visibleRowCount);

            return Results.Ok(ApiResult<MyCommunitiesResponse>.Ok(payload, CorrelationOf(tenant)));
        })
        .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Community"));

        // Protected write with validation at the trust boundary.
        group.MapPost("/echo", (EchoRequest request) =>
                Results.Ok(ApiResult<EchoRequest>.Ok(request, string.Empty)))
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "Community"));

        return app;
    }

    // The envelope's correlation id is set by the pipeline/middleware; endpoints don't own it, so
    // the demo leaves it empty here (the middleware overwrites the header + logs the real id).
    private static string CorrelationOf(ITenantContext _) => string.Empty;

    public sealed record MyCommunitiesResponse(
        string? UserId,
        Guid[] AuthorizedCommunityIds,
        bool IsPlatformSuperAdmin,
        int VisibleRowCount);

    public sealed class EchoRequest
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "name is required")]
        [MaxLength(100)]
        public string Name { get; init; } = string.Empty;

        [Range(1, 120, ErrorMessage = "age must be between 1 and 120")]
        public int Age { get; init; }
    }
}
