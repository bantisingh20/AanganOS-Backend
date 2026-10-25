using System.ComponentModel.DataAnnotations;
using UrbanNest.Api.Security;
using UrbanNest.Api.Validation;
using UrbanNest.Application.Exceptions;
using UrbanNest.Application.Querying;
using UrbanNest.Application.Residents;
using UrbanNest.Contracts.Envelope;
using UrbanNest.Domain.Identity;

namespace UrbanNest.Api.Endpoints;

/// <summary>
/// HTTP surface over the Task 12 resident services — <see cref="IResidentService"/> (Req 15.1–15.5,
/// verification 17.x, move 18.x) and <see cref="ICommunicationPreferenceService"/> (Req 20.1, 20.2).
/// Every endpoint uses the <see cref="ApiResult{T}"/> envelope (Req 9.1), carries a
/// <see cref="RequirePermissionAttribute"/> on the correct (Action, ResourceType) (Req 4.1),
/// validates write bodies (Req 9.2), applies the "write" rate-limit policy to mutations (Req 65.5),
/// and the list is paginated/filter(by verificationStatus + residentType)/sort/search through the
/// shared query infra (Req 15.4, 9.3). Lists are scoped to the caller's authorized communities by
/// the EF global tenant query filter — not a bespoke predicate here.
///
/// <para><b>Verify / reject / resubmit need <c>Approve</c></b> on <c>Resident</c> (Req 17.5); the
/// service re-checks the same permission as defense-in-depth. CONFLICT (invalid transition /
/// concurrency), VALIDATION_ERROR (bad type / blank reason), NOT_FOUND and FORBIDDEN are surfaced
/// by the services through the central exception envelope — the endpoints only shape the request.</para>
///
/// <para><b>Resident-own communication preferences (Req 66.3).</b> Preferences are gated on
/// <c>(View|Edit, Resident)</c> rather than a new resource type: reusing the existing seeded
/// <c>Resident</c> catalog entry avoids a schema/seed change, and the resident self-scope global
/// query filter already restricts a resident principal to their own resident row — so a resident
/// holding Edit-on-Resident can only read/write their OWN preferences (a foreign resident id is
/// invisible under their scope and the service returns NOT_FOUND), while an operator with wider
/// Resident scope can manage others. The permission gate + tenant/self-scope together realize
/// Req 66.3 without bespoke endpoint logic.</para>
/// </summary>
public static class ResidentEndpoints
{
    public static IEndpointRouteBuilder MapResidentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/residents");

        // POST /api/v1/residents — create (Req 15.1). Invalid type → VALIDATION_ERROR.
        group.MapPost("/", async (
                CreateResidentBody body,
                IResidentService residents,
                CancellationToken ct) =>
            {
                var dto = await residents.CreateAsync(new CreateResidentRequest(
                    body.CommunityId,
                    body.UserId,
                    body.Name!,
                    body.Email,
                    body.Phone,
                    body.ResidentType!,
                    body.PhotoFileId), ct);
                return Results.Ok(ApiResult<ResidentDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "Resident"));

        // GET /api/v1/residents — paginated/filter(by verificationStatus + residentType)/sort/search
        // list, scoped to authorized communities by the tenant filter (Req 15.4). Non-archived only.
        group.MapGet("/", async (
                string? search,
                string? sort,
                string? verificationStatus,
                string? residentType,
                int? page,
                int? pageSize,
                IResidentService residents,
                CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                if (!string.IsNullOrWhiteSpace(verificationStatus))
                {
                    filters.Add(new FilterTerm("verificationStatus", FilterOperator.Equals, verificationStatus));
                }
                if (!string.IsNullOrWhiteSpace(residentType))
                {
                    filters.Add(new FilterTerm("residentType", FilterOperator.Equals, residentType));
                }

                var parameters = new ListQueryParameters
                {
                    Page = page,
                    PageSize = pageSize,
                    Search = string.IsNullOrWhiteSpace(search) ? null : search,
                    Filters = filters,
                    Sort = EndpointSort.Parse(sort),
                };

                var data = await residents.ListAsync(parameters, ct);
                return Results.Ok(ApiResult<PagedData<ResidentDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Resident"));

        // GET /api/v1/residents/me — the SIGNED-IN principal's own resident row + own unit memberships
        // (Resident.UserId == token user). One self-targeted call for the app's "who am I" (header
        // name, self-service unit pickers) — no list-and-filter. No linked resident (e.g. superadmin)
        // → { resident: null, units: [] }, not NOT_FOUND. Registered BEFORE /{id:guid} so the literal
        // "me" segment is matched here, never treated as an id.
        group.MapGet("/me", async (
                IResidentService residents,
                CancellationToken ct) =>
            {
                var dto = await residents.GetMeAsync(ct);
                return Results.Ok(ApiResult<ResidentMeDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Resident"));

        // GET /api/v1/residents/{id} — fetch one, tenant-scoped. Unknown/foreign id → NOT_FOUND.
        group.MapGet("/{id:guid}", async (
                Guid id,
                IResidentService residents,
                CancellationToken ct) =>
            {
                var dto = await residents.GetAsync(id, includeArchived: false, ct)
                    ?? throw new NotFoundException("Resident not found.");
                return Results.Ok(ApiResult<ResidentDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Resident"));

        // PUT /api/v1/residents/{id} — edit profile. Changed type re-validated; concurrency → CONFLICT.
        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdateResidentBody body,
                IResidentService residents,
                CancellationToken ct) =>
            {
                var dto = await residents.UpdateAsync(id, new UpdateResidentRequest(
                    body.Name,
                    body.ResidentType,
                    body.Email,
                    body.UpdateEmail,
                    body.Phone,
                    body.UpdatePhone,
                    body.UserId,
                    body.UpdateUserId,
                    body.PhotoFileId,
                    body.UpdatePhotoFileId), ct);
                return Results.Ok(ApiResult<ResidentDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Resident"));

        // DELETE /api/v1/residents/{id} — soft-delete (Req 15.5).
        group.MapDelete("/{id:guid}", async (
                Guid id,
                IResidentService residents,
                CancellationToken ct) =>
            {
                await residents.DeleteAsync(id, ct);
                return Results.Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Delete, "Resident"));

        // --- Verification workflow (Req 17.x, Task 12.3) — all need Approve on Resident (Req 17.5). ---

        // POST /api/v1/residents/{id}/verify — approve a pending resident → verified (Req 17.2).
        group.MapPost("/{id:guid}/verify", async (
                Guid id,
                IResidentService residents,
                CancellationToken ct) =>
            {
                var dto = await residents.ApproveAsync(id, ct);
                return Results.Ok(ApiResult<ResidentDto>.Ok(dto, string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Approve, "Resident"));

        // POST /api/v1/residents/{id}/reject — reject a pending resident with a reason (Req 17.3).
        group.MapPost("/{id:guid}/reject", async (
                Guid id,
                RejectResidentBody body,
                IResidentService residents,
                CancellationToken ct) =>
            {
                var dto = await residents.RejectAsync(id, new RejectResidentRequest(body.Reason!), ct);
                return Results.Ok(ApiResult<ResidentDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Approve, "Resident"));

        // POST /api/v1/residents/{id}/resubmit — resubmit a rejected resident → pending (Req 17.1).
        group.MapPost("/{id:guid}/resubmit", async (
                Guid id,
                IResidentService residents,
                CancellationToken ct) =>
            {
                var dto = await residents.ResubmitForVerificationAsync(id, ct);
                return Results.Ok(ApiResult<ResidentDto>.Ok(dto, string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Approve, "Resident"));

        // --- Move in/out (Req 18.x, Task 12.4). Move-in is the Create verb, move-out the Edit verb
        //     on Resident — matching the service's defense-in-depth permission checks. ---

        // POST /api/v1/residents/{id}/move-in (Req 18.1).
        group.MapPost("/{id:guid}/move-in", async (
                Guid id,
                MoveInBody body,
                IResidentService residents,
                CancellationToken ct) =>
            {
                var dto = await residents.MoveInAsync(id, new MoveInRequest(
                    body.UnitId, body.MoveInDate, body.Relationship), ct);
                return Results.Ok(ApiResult<HouseholdMemberDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "Resident"));

        // POST /api/v1/residents/{id}/move-out (Req 18.2, 18.3).
        group.MapPost("/{id:guid}/move-out", async (
                Guid id,
                MoveOutBody body,
                IResidentService residents,
                CancellationToken ct) =>
            {
                var dto = await residents.MoveOutAsync(id, new MoveOutRequest(body.UnitId, body.MoveOutDate), ct);
                return Results.Ok(ApiResult<HouseholdMemberDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Resident"));

        // --- Household association (Req 15.2) — Create on HouseholdMember. ---
        group.MapPost("/{id:guid}/household", async (
                Guid id,
                AssociateHouseholdBody body,
                IResidentService residents,
                CancellationToken ct) =>
            {
                var dto = await residents.AssociateHouseholdAsync(id, new AssociateHouseholdRequest(
                    body.UnitId, body.Relationship!, body.StartDate), ct);
                return Results.Ok(ApiResult<HouseholdMemberDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "HouseholdMember"));

        // GET /{id}/household — the resident's OWN active unit memberships + unit details (Req 15.2).
        // Lets a resident's self-service screens resolve their own unit(s), not the whole community.
        group.MapGet("/{id:guid}/household", async (
                Guid id,
                IResidentService residents,
                CancellationToken ct) =>
            {
                var data = await residents.GetResidentHouseholdAsync(id, ct);
                return Results.Ok(ApiResult<IReadOnlyList<ResidentHouseholdUnitDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "HouseholdMember"));

        // --- Create login for a resident (Req 3.1, 15.1) — Create on User. Hashes the password
        //     server-side, creates the User, links Resident.UserId and assigns a role scoped to the
        //     community so the login can use resident features. ---
        group.MapPost("/{id:guid}/login", async (
                Guid id,
                CreateResidentLoginBody body,
                IResidentService residents,
                CancellationToken ct) =>
            {
                var result = await residents.CreateLoginAsync(id,
                    new CreateResidentLoginRequest(body.Identifier!, body.Password!, body.RoleName), ct);
                return Results.Ok(ApiResult<ResidentLoginResult>.Ok(result, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "User"));

        // --- Emergency contacts (Req 15.3) — Create / View on EmergencyContact. ---
        group.MapPost("/{id:guid}/emergency-contacts", async (
                Guid id,
                AddEmergencyContactBody body,
                IResidentService residents,
                CancellationToken ct) =>
            {
                var dto = await residents.AddEmergencyContactAsync(id, new AddEmergencyContactRequest(
                    body.Name!, body.Phone!, body.Relationship!), ct);
                return Results.Ok(ApiResult<EmergencyContactDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "EmergencyContact"));

        group.MapGet("/{id:guid}/emergency-contacts", async (
                Guid id,
                IResidentService residents,
                CancellationToken ct) =>
            {
                var data = await residents.ListEmergencyContactsAsync(id, ct);
                return Results.Ok(ApiResult<IReadOnlyList<EmergencyContactDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "EmergencyContact"));

        // --- Communication preferences (Req 20.1, 20.2, 66.3) — View/Edit on Resident; the resident
        //     self-scope filter restricts a resident principal to their OWN preferences. ---
        group.MapGet("/{id:guid}/communication-preferences", async (
                Guid id,
                ICommunicationPreferenceService preferences,
                CancellationToken ct) =>
            {
                var data = await preferences.GetAsync(id, ct);
                return Results.Ok(ApiResult<IReadOnlyList<CommunicationPreferenceDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Resident"));

        group.MapPut("/{id:guid}/communication-preferences", async (
                Guid id,
                SetCommunicationPreferencesBody body,
                ICommunicationPreferenceService preferences,
                CancellationToken ct) =>
            {
                var request = new SetCommunicationPreferencesRequest(
                    (body.Preferences ?? [])
                        .Select(p => new CommunicationPreferenceUpdate(p.Category!, p.OptedIn))
                        .ToList());
                var data = await preferences.SetAsync(id, request, ct);
                return Results.Ok(ApiResult<IReadOnlyList<CommunicationPreferenceDto>>.Ok(data, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Resident"));

        return app;
    }

    public sealed class CreateResidentBody
    {
        [Required(ErrorMessage = "communityId is required")]
        public Guid CommunityId { get; init; }

        public Guid? UserId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "name is required")]
        [MaxLength(256)]
        public string? Name { get; init; }

        [MaxLength(256)]
        public string? Email { get; init; }

        [MaxLength(50)]
        public string? Phone { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "residentType is required")]
        [MaxLength(100)]
        public string? ResidentType { get; init; }

        // Optional File_Service photo reference; null when none captured.
        [MaxLength(200)]
        public string? PhotoFileId { get; init; }
    }

    public sealed class UpdateResidentBody
    {
        [MaxLength(256)]
        public string? Name { get; init; }

        [MaxLength(100)]
        public string? ResidentType { get; init; }

        [MaxLength(256)]
        public string? Email { get; init; }

        // When true Email is applied (allowing null = clear); when false untouched.
        public bool UpdateEmail { get; init; }

        [MaxLength(50)]
        public string? Phone { get; init; }

        // When true Phone is applied (allowing null = clear); when false untouched.
        public bool UpdatePhone { get; init; }

        public Guid? UserId { get; init; }

        // When true UserId is applied (allowing null = unlink); when false untouched.
        public bool UpdateUserId { get; init; }

        [MaxLength(200)]
        public string? PhotoFileId { get; init; }

        // When true PhotoFileId is applied (allowing null = clear); when false untouched.
        public bool UpdatePhotoFileId { get; init; }
    }

    public sealed class RejectResidentBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "reason is required")]
        [MaxLength(1000)]
        public string? Reason { get; init; }
    }

    public sealed class MoveInBody
    {
        [Required(ErrorMessage = "unitId is required")]
        public Guid UnitId { get; init; }

        public DateTime? MoveInDate { get; init; }

        [MaxLength(100)]
        public string? Relationship { get; init; }
    }

    public sealed class MoveOutBody
    {
        [Required(ErrorMessage = "unitId is required")]
        public Guid UnitId { get; init; }

        public DateTime? MoveOutDate { get; init; }
    }

    public sealed class AssociateHouseholdBody
    {
        [Required(ErrorMessage = "unitId is required")]
        public Guid UnitId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "relationship is required")]
        [MaxLength(100)]
        public string? Relationship { get; init; }

        public DateTime? StartDate { get; init; }
    }

    public sealed class CreateResidentLoginBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "identifier is required")]
        [MaxLength(256)]
        public string? Identifier { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "password is required")]
        [MaxLength(256)]
        public string? Password { get; init; }

        /// <summary>Optional role name to assign (scoped to the community); null = the default "Resident" role.</summary>
        [MaxLength(128)]
        public string? RoleName { get; init; }
    }

    public sealed class AddEmergencyContactBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "name is required")]
        [MaxLength(256)]
        public string? Name { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "phone is required")]
        [MaxLength(50)]
        public string? Phone { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "relationship is required")]
        [MaxLength(100)]
        public string? Relationship { get; init; }
    }

    public sealed class SetCommunicationPreferencesBody
    {
        [Required(ErrorMessage = "preferences is required")]
        public List<CommunicationPreferenceItem>? Preferences { get; init; }
    }

    public sealed class CommunicationPreferenceItem
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "category is required")]
        [MaxLength(128)]
        public string? Category { get; init; }

        public bool OptedIn { get; init; }
    }
}
