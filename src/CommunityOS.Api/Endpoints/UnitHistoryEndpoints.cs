using System.ComponentModel.DataAnnotations;
using CommunityOS.Api.Security;
using CommunityOS.Api.Validation;
using CommunityOS.Application.Residents;
using CommunityOS.Contracts.Envelope;
using CommunityOS.Domain.Identity;

namespace CommunityOS.Api.Endpoints;

/// <summary>
/// HTTP surface over <see cref="IUnitHistoryService"/> (Task 12.2) — a unit's ownership and tenancy
/// history (Req 16.1–16.4). Nested under <c>/api/v1/units/{id}</c>. Every endpoint uses the
/// <see cref="ApiResult{T}"/> envelope (Req 9.1), carries a <see cref="RequirePermissionAttribute"/>
/// on the <c>OwnershipHistory</c> / <c>TenancyHistory</c> resource type for its action (Req 4.1),
/// validates write bodies (Req 9.2), and applies the "write" rate-limit policy to mutations
/// (Req 65.5). Reads are tenant-scoped by the global query filter; CONFLICT (open-owner race /
/// concurrency), VALIDATION_ERROR (cross-community), NOT_FOUND are surfaced by the service.
///
/// <para>As append-only registers the resource types carry no Delete — a prior entry is retained,
/// never removed (Req 16.3). Assign-ownership and begin-tenancy use the <c>Create</c> verb;
/// end-tenancy (closing an open row) uses <c>Edit</c>.</para>
/// </summary>
public static class UnitHistoryEndpoints
{
    public static IEndpointRouteBuilder MapUnitHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/units/{unitId:guid}");

        // GET /api/v1/units/{id}/household — the unit's active household members (name + type), used
        // to show the household and to decide the owner-first add rule (Req 15.2).
        group.MapGet("/household", async (
                Guid unitId,
                IResidentService residents,
                CancellationToken ct) =>
            {
                var members = await residents.GetUnitHouseholdAsync(unitId, ct);
                return Results.Ok(ApiResult<IReadOnlyList<UnitHouseholdMemberDto>>.Ok(members, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "HouseholdMember"));

        // GET /api/v1/units/{id}/history — combined chronological ownership + tenancy (Req 16.4).
        group.MapGet("/history", async (
                Guid unitId,
                IUnitHistoryService history,
                CancellationToken ct) =>
            {
                var dto = await history.GetUnitHistoryAsync(unitId, ct);
                return Results.Ok(ApiResult<UnitHistoryDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "OwnershipHistory"));

        // GET /api/v1/units/{id}/ownership-history — the ownership slice (Req 16.4).
        group.MapGet("/ownership-history", async (
                Guid unitId,
                IUnitHistoryService history,
                CancellationToken ct) =>
            {
                var dto = await history.GetUnitHistoryAsync(unitId, ct);
                return Results.Ok(ApiResult<IReadOnlyList<OwnershipHistoryDto>>.Ok(dto.Ownership, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "OwnershipHistory"));

        // GET /api/v1/units/{id}/tenancy-history — the tenancy slice (Req 16.4).
        group.MapGet("/tenancy-history", async (
                Guid unitId,
                IUnitHistoryService history,
                CancellationToken ct) =>
            {
                var dto = await history.GetUnitHistoryAsync(unitId, ct);
                return Results.Ok(ApiResult<IReadOnlyList<TenancyHistoryDto>>.Ok(dto.Tenancy, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "TenancyHistory"));

        // POST /api/v1/units/{id}/ownership — assign/transfer the owner (Req 16.1, 16.3).
        group.MapPost("/ownership", async (
                Guid unitId,
                AssignOwnershipBody body,
                IUnitHistoryService history,
                CancellationToken ct) =>
            {
                var dto = await history.AssignOwnershipAsync(
                    new AssignOwnershipRequest(unitId, body.ResidentId, body.StartDate), ct);
                return Results.Ok(ApiResult<OwnershipHistoryDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "OwnershipHistory"));

        // POST /api/v1/units/{id}/tenancy — begin a tenancy (Req 16.2, 16.3).
        group.MapPost("/tenancy", async (
                Guid unitId,
                BeginTenancyBody body,
                IUnitHistoryService history,
                CancellationToken ct) =>
            {
                var dto = await history.BeginTenancyAsync(
                    new BeginTenancyRequest(unitId, body.ResidentId, body.StartDate), ct);
                return Results.Ok(ApiResult<TenancyHistoryDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "TenancyHistory"));

        // POST /api/v1/units/{id}/tenancy/end — end an active tenancy (Req 16.2, 16.3).
        group.MapPost("/tenancy/end", async (
                Guid unitId,
                EndTenancyBody body,
                IUnitHistoryService history,
                CancellationToken ct) =>
            {
                var dto = await history.EndTenancyAsync(
                    new EndTenancyRequest(unitId, body.ResidentId, body.EndDate), ct);
                return Results.Ok(ApiResult<TenancyHistoryDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "TenancyHistory"));

        return app;
    }

    public sealed class AssignOwnershipBody
    {
        [Required(ErrorMessage = "residentId is required")]
        public Guid ResidentId { get; init; }

        public DateTime? StartDate { get; init; }
    }

    public sealed class BeginTenancyBody
    {
        [Required(ErrorMessage = "residentId is required")]
        public Guid ResidentId { get; init; }

        public DateTime? StartDate { get; init; }
    }

    public sealed class EndTenancyBody
    {
        [Required(ErrorMessage = "residentId is required")]
        public Guid ResidentId { get; init; }

        public DateTime? EndDate { get; init; }
    }
}
