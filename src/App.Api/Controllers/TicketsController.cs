using System.ComponentModel.DataAnnotations;
using App.Api.Endpoints;
using App.Api.Security;
using App.Application.Exceptions;
using App.Application.Helpdesk;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
[Route("api/v1/tickets")]
public sealed class TicketsController(IHelpdeskService helpdesk) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "Ticket")]
    public async Task<IActionResult> Create([FromBody] CreateTicketBody body, CancellationToken ct)
    {
        var dto = await helpdesk.CreateTicketAsync(new CreateTicketRequest(
            body.HostUnitId, body.Title!, body.Description!, body.Category!, body.Subcategory, body.Priority, body.AttachmentFileIds), ct);
        return Ok(ApiResult<TicketDto>.Ok(dto, string.Empty));
    }

    [HttpGet]
    [RequirePermission(PermissionAction.View, "Ticket")]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] string? status,
        [FromQuery] string? category,
        [FromQuery] string? subcategory,
        [FromQuery] string? priority,
        [FromQuery] Guid? hostUnitId,
        [FromQuery] Guid? assignedToUserId,
        [FromQuery] Guid? communityId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filters = new List<FilterTerm>();
        AddEquals(filters, "status", status);
        AddEquals(filters, "category", category);
        AddEquals(filters, "subcategory", subcategory);
        AddEquals(filters, "priority", priority);
        AddEquals(filters, "hostUnitId", hostUnitId);
        AddEquals(filters, "assignedToUserId", assignedToUserId);
        AddEquals(filters, "communityId", communityId);

        var data = await helpdesk.ListTicketsAsync(BuildParameters(page, pageSize, search, sort, filters), ct);
        return Ok(ApiResult<PagedData<TicketDto>>.Ok(data, string.Empty));
    }

    [HttpGet("analytics")]
    [RequirePermission(PermissionAction.View, "Ticket")]
    public async Task<IActionResult> Analytics([FromQuery] Guid? communityId, CancellationToken ct)
    {
        var dto = await helpdesk.GetTicketAnalyticsAsync(communityId, ct);
        return Ok(ApiResult<TicketAnalyticsDto>.Ok(dto, string.Empty));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionAction.View, "Ticket")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var dto = await helpdesk.GetTicketAsync(id, ct) ?? throw new NotFoundException("Ticket not found.");
        return Ok(ApiResult<TicketDto>.Ok(dto, string.Empty));
    }

    [HttpGet("{id:guid}/attachments")]
    [RequirePermission(PermissionAction.View, "TicketAttachment")]
    public async Task<IActionResult> Attachments(Guid id, CancellationToken ct)
    {
        var items = await helpdesk.GetTicketAttachmentsAsync(id, ct);
        return Ok(ApiResult<IReadOnlyList<TicketAttachmentDto>>.Ok(items, string.Empty));
    }

    [HttpGet("{id:guid}/comments")]
    [RequirePermission(PermissionAction.View, "TicketComment")]
    public async Task<IActionResult> Comments(Guid id, CancellationToken ct)
    {
        var items = await helpdesk.GetTicketCommentsAsync(id, ct);
        return Ok(ApiResult<IReadOnlyList<TicketCommentDto>>.Ok(items, string.Empty));
    }

    [HttpPost("{id:guid}/comments")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "TicketComment")]
    public async Task<IActionResult> AddComment(Guid id, [FromBody] AddCommentBody body, CancellationToken ct)
    {
        var dto = await helpdesk.AddTicketCommentAsync(id, new AddTicketCommentRequest(body.Body!), ct);
        return Ok(ApiResult<TicketCommentDto>.Ok(dto, string.Empty));
    }

    [HttpPost("{id:guid}/assign")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Assign, "Ticket")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignTicketBody body, CancellationToken ct)
    {
        var dto = await helpdesk.AssignTicketAsync(id, new AssignTicketRequest(body.AssignedToUserId), ct);
        return Ok(ApiResult<TicketDto>.Ok(dto, string.Empty));
    }

    [HttpPost("{id:guid}/status")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Ticket")]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeTicketStatusBody body, CancellationToken ct)
    {
        var dto = await helpdesk.ChangeTicketStatusAsync(id, new ChangeTicketStatusRequest(body.ToStatus!, body.Reason), ct);
        return Ok(ApiResult<TicketDto>.Ok(dto, string.Empty));
    }

    [HttpPost("{id:guid}/feedback")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Create, "Ticket")]
    public async Task<IActionResult> SubmitFeedback(Guid id, [FromBody] SubmitFeedbackBody body, CancellationToken ct)
    {
        var dto = await helpdesk.SubmitFeedbackAsync(id, new SubmitTicketFeedbackRequest(body.Rating, body.Comment), ct);
        return Ok(ApiResult<TicketDto>.Ok(dto, string.Empty));
    }

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
