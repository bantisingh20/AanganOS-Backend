using System.ComponentModel.DataAnnotations;
using CommunityOS.Api.Security;
using CommunityOS.Api.Validation;
using CommunityOS.Application.Exceptions;
using CommunityOS.Application.Helpdesk;
using CommunityOS.Application.Querying;
using CommunityOS.Contracts.Envelope;
using CommunityOS.Domain.Identity;

namespace CommunityOS.Api.Endpoints;

/// <summary>
/// HTTP surface over the Phase 5 Helpdesk_Service (<see cref="IHelpdeskService"/>) — ticket creation
/// with configurable classification + File_Service attachments (Req 32.x), assignment / SLA (Req
/// 33.x), the configurable status lifecycle / resident feedback (Req 34.x) and the aggregated
/// analytics (Req 34.5). Mirrors the established minimal-API style of <see cref="ParcelEndpoints"/> /
/// <see cref="GateEndpoints"/>: the <see cref="ApiResult{T}"/> envelope (Req 9.1), a
/// <see cref="RequirePermissionAttribute"/> on the correct (Action, ResourceType) (Req 4.1),
/// <see cref="DataAnnotationsValidationFilter"/> on write bodies (Req 9.2), the "write" rate-limit
/// policy on every mutation (Req 65.5) and the shared paginated list infra
/// (<see cref="ListQueryParameters"/> + the service's allow-listed <c>ListQueryConfiguration</c>,
/// Req 9.3). Lists are scoped to the caller's authorized communities (and, for a resident principal,
/// their own unit) by the EF global tenant/self-scope query filter — no bespoke predicate here.
///
/// <para><b>Permission pairs mirror the service's defense-in-depth checks (steering: security
/// first, no hardcoding).</b> Create ticket / submit feedback = <c>(Create, Ticket)</c>; change
/// status = <c>(Edit, Ticket)</c>; assign = <c>(Assign, Ticket)</c>; ticket reads + analytics =
/// <c>(View, Ticket)</c>; attachment read = <c>(View, TicketAttachment)</c>. The service re-checks the
/// same permission as defense-in-depth; CONFLICT / VALIDATION_ERROR / NOT_FOUND / FORBIDDEN surface
/// through the central exception envelope — the endpoints only shape the request.</para>
///
/// <para><b>Worker-only operations are intentionally off the HTTP surface.</b>
/// <see cref="IHelpdeskService.EscalateBreachedTicketsAsync"/> is driven by the background SLA-breach
/// sweep under a cross-tenant super-admin context, so it is NOT exposed as an endpoint here.</para>
/// </summary>
public static class HelpdeskEndpoints
{
    public static IEndpointRouteBuilder MapHelpdeskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tickets");

        // POST — a resident raises a ticket for their own unit (Req 32.1, 32.3).
        group.MapPost("/", async (
                CreateTicketBody body,
                IHelpdeskService helpdesk,
                CancellationToken ct) =>
            {
                var dto = await helpdesk.CreateTicketAsync(new CreateTicketRequest(
                    body.HostUnitId,
                    body.Title!,
                    body.Description!,
                    body.Category!,
                    body.Subcategory,
                    body.Priority,
                    body.AttachmentFileIds), ct);
                return Results.Ok(ApiResult<TicketDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "Ticket"));

        // GET — paginated/filter/sort/search list, tenant + resident-self scoped (Req 32.4).
        group.MapGet("/", async (
                string? search,
                string? sort,
                string? status,
                string? category,
                string? subcategory,
                string? priority,
                Guid? hostUnitId,
                Guid? assignedToUserId,
                int? page,
                int? pageSize,
                IHelpdeskService helpdesk,
                CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                AddEquals(filters, "status", status);
                AddEquals(filters, "category", category);
                AddEquals(filters, "subcategory", subcategory);
                AddEquals(filters, "priority", priority);
                AddEquals(filters, "hostUnitId", hostUnitId);
                AddEquals(filters, "assignedToUserId", assignedToUserId);

                var data = await helpdesk.ListTicketsAsync(BuildParameters(page, pageSize, search, sort, filters), ct);
                return Results.Ok(ApiResult<PagedData<TicketDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Ticket"));

        // GET /analytics — aggregated helpdesk analytics for the caller's authorized communities,
        // optionally narrowed to one authorized community (Req 34.5). Declared before /{id} so the
        // literal segment is not captured by the guid route.
        group.MapGet("/analytics", async (
                Guid? communityId,
                IHelpdeskService helpdesk,
                CancellationToken ct) =>
            {
                var dto = await helpdesk.GetTicketAnalyticsAsync(communityId, ct);
                return Results.Ok(ApiResult<TicketAnalyticsDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Ticket"));

        // GET /{id} — fetch one, tenant/self-scoped. Unknown/foreign id → NOT_FOUND.
        group.MapGet("/{id:guid}", async (
                Guid id,
                IHelpdeskService helpdesk,
                CancellationToken ct) =>
            {
                var dto = await helpdesk.GetTicketAsync(id, ct)
                    ?? throw new NotFoundException("Ticket not found.");
                return Results.Ok(ApiResult<TicketDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Ticket"));

        // GET /{id}/attachments — the ticket's File_Service attachment references (Req 32.3).
        group.MapGet("/{id:guid}/attachments", async (
                Guid id,
                IHelpdeskService helpdesk,
                CancellationToken ct) =>
            {
                var items = await helpdesk.GetTicketAttachmentsAsync(id, ct);
                return Results.Ok(ApiResult<IReadOnlyList<TicketAttachmentDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "TicketAttachment"));

        // GET /{id}/comments — the ticket's discussion thread, oldest-first (tenant/self-scoped).
        group.MapGet("/{id:guid}/comments", async (
                Guid id,
                IHelpdeskService helpdesk,
                CancellationToken ct) =>
            {
                var items = await helpdesk.GetTicketCommentsAsync(id, ct);
                return Results.Ok(ApiResult<IReadOnlyList<TicketCommentDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "TicketComment"));

        // POST /{id}/comments — post a comment to the ticket (resident or staff).
        group.MapPost("/{id:guid}/comments", async (
                Guid id,
                AddCommentBody body,
                IHelpdeskService helpdesk,
                CancellationToken ct) =>
            {
                var dto = await helpdesk.AddTicketCommentAsync(id, new AddTicketCommentRequest(body.Body!), ct);
                return Results.Ok(ApiResult<TicketCommentDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "TicketComment"));

        // POST /{id}/assign — assign/re-assign the ticket to maintenance staff (Req 33.1, 33.4, 33.5).
        group.MapPost("/{id:guid}/assign", async (
                Guid id,
                AssignTicketBody body,
                IHelpdeskService helpdesk,
                CancellationToken ct) =>
            {
                var dto = await helpdesk.AssignTicketAsync(id, new AssignTicketRequest(body.AssignedToUserId), ct);
                return Results.Ok(ApiResult<TicketDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Assign, "Ticket"));

        // POST /{id}/status — drive the ticket through its configurable status lifecycle (Req 34.1-34.3).
        group.MapPost("/{id:guid}/status", async (
                Guid id,
                ChangeTicketStatusBody body,
                IHelpdeskService helpdesk,
                CancellationToken ct) =>
            {
                var dto = await helpdesk.ChangeTicketStatusAsync(id, new ChangeTicketStatusRequest(body.ToStatus!, body.Reason), ct);
                return Results.Ok(ApiResult<TicketDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Ticket"));

        // POST /{id}/feedback — resident submits feedback/rating on their resolved ticket (Req 34.4).
        group.MapPost("/{id:guid}/feedback", async (
                Guid id,
                SubmitFeedbackBody body,
                IHelpdeskService helpdesk,
                CancellationToken ct) =>
            {
                var dto = await helpdesk.SubmitFeedbackAsync(id, new SubmitTicketFeedbackRequest(body.Rating, body.Comment), ct);
                return Results.Ok(ApiResult<TicketDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "Ticket"));

        return app;
    }

    // --- Shared helpers --------------------------------------------------------------------------

    /// <summary>Add an equals filter term only when the value is present (keeps the allow-list honest).</summary>
    private static void AddEquals(List<FilterTerm> filters, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            filters.Add(new FilterTerm(field, FilterOperator.Equals, value));
        }
    }

    private static void AddEquals(List<FilterTerm> filters, string field, Guid? value)
    {
        if (value is { } v)
        {
            filters.Add(new FilterTerm(field, FilterOperator.Equals, v.ToString()));
        }
    }

    private static ListQueryParameters BuildParameters(
        int? page, int? pageSize, string? search, string? sort, List<FilterTerm> filters) =>
        new()
        {
            Page = page,
            PageSize = pageSize,
            Search = string.IsNullOrWhiteSpace(search) ? null : search,
            Filters = filters,
            Sort = EndpointSort.Parse(sort),
        };

    // --- Request bodies (validated via DataAnnotationsValidationFilter) --------------------------

    public sealed class CreateTicketBody
    {
        [Required(ErrorMessage = "hostUnitId is required")]
        public Guid HostUnitId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "title is required")]
        [MaxLength(256)]
        public string? Title { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "description is required")]
        [MaxLength(4000)]
        public string? Description { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "category is required")]
        [MaxLength(100)]
        public string? Category { get; init; }

        [MaxLength(100)]
        public string? Subcategory { get; init; }

        [MaxLength(100)]
        public string? Priority { get; init; }

        public IReadOnlyList<Guid>? AttachmentFileIds { get; init; }
    }

    public sealed class AddCommentBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "body is required")]
        [MaxLength(2000)]
        public string? Body { get; init; }
    }

    public sealed class AssignTicketBody
    {
        [Required(ErrorMessage = "assignedToUserId is required")]
        public Guid AssignedToUserId { get; init; }
    }

    public sealed class ChangeTicketStatusBody
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "toStatus is required")]
        [MaxLength(100)]
        public string? ToStatus { get; init; }

        [MaxLength(1000)]
        public string? Reason { get; init; }
    }

    public sealed class SubmitFeedbackBody
    {
        [Required(ErrorMessage = "rating is required")]
        public int Rating { get; init; }

        [MaxLength(2000)]
        public string? Comment { get; init; }
    }
}
