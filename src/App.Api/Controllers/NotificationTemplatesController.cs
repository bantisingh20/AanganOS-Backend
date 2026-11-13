using App.Api.Security;
using App.Application.Exceptions;
using App.Application.Notifications;
using App.Contracts.Envelope;
using App.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

[ApiController]
[Route("api/v1/notification-templates")]
public sealed class NotificationTemplatesController(INotificationTemplateAdminService svc) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionAction.View, "Configuration")]
    public async Task<IActionResult> List([FromQuery] string? search, CancellationToken ct)
    {
        var rows = await svc.ListAsync(search, ct);
        return Ok(ApiResult<IReadOnlyList<NotificationTemplateDto>>.Ok(rows, string.Empty));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionAction.View, "Configuration")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var row = await svc.GetAsync(id, ct)
            ?? throw new NotFoundException("Template not found.");
        return Ok(ApiResult<NotificationTemplateDto>.Ok(row, string.Empty));
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequirePermission(PermissionAction.Edit, "Configuration")]
    public async Task<IActionResult> Upsert([FromBody] UpsertNotificationTemplateBody body, CancellationToken ct)
    {
        var row = await svc.UpsertAsync(body.ToRequest(), ct);
        return Ok(ApiResult<NotificationTemplateDto>.Ok(row, string.Empty));
    }

    [HttpPost("preview")]
    [RequirePermission(PermissionAction.View, "Configuration")]
    public async Task<IActionResult> Preview([FromBody] PreviewNotificationTemplateBody body, CancellationToken ct)
    {
        var preview = await svc.PreviewAsync(body.ToRequest(), body.SampleValues, ct);
        return Ok(ApiResult<NotificationTemplatePreviewDto>.Ok(preview, string.Empty));
    }

    public sealed class UpsertNotificationTemplateBody
    {
        public string Key { get; init; } = string.Empty;
        public int Channels { get; init; }
        public string TitleTemplate { get; init; } = string.Empty;
        public string BodyTemplate { get; init; } = string.Empty;
        public string? EmailSubjectTemplate { get; init; }
        public string? EmailHtmlTemplate { get; init; }
        public string? SmsTemplate { get; init; }
        public string? WhatsappTemplateName { get; init; }
        public string? WhatsappVarsJson { get; init; }
        public bool IsActive { get; init; } = true;
        public Guid? CommunityId { get; init; }

        public UpsertNotificationTemplateRequest ToRequest() => new(
            Key, Channels, TitleTemplate, BodyTemplate, EmailSubjectTemplate, EmailHtmlTemplate,
            SmsTemplate, WhatsappTemplateName, WhatsappVarsJson, IsActive, CommunityId);
    }

    public sealed class PreviewNotificationTemplateBody
    {
        public string Key { get; init; } = string.Empty;
        public int Channels { get; init; }
        public string TitleTemplate { get; init; } = string.Empty;
        public string BodyTemplate { get; init; } = string.Empty;
        public string? EmailSubjectTemplate { get; init; }
        public string? EmailHtmlTemplate { get; init; }
        public string? SmsTemplate { get; init; }
        public string? WhatsappTemplateName { get; init; }
        public string? WhatsappVarsJson { get; init; }
        public Dictionary<string, string>? SampleValues { get; init; }

        public UpsertNotificationTemplateRequest ToRequest() => new(
            Key, Channels, TitleTemplate, BodyTemplate, EmailSubjectTemplate, EmailHtmlTemplate,
            SmsTemplate, WhatsappTemplateName, WhatsappVarsJson, true, null);
    }
}
