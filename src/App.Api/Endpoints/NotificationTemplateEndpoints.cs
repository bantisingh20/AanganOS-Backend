using App.Api.Security;
using App.Api.Validation;
using App.Application.Notifications;
using App.Contracts.Envelope;
using App.Domain.Identity;

namespace App.Api.Endpoints;

public static class NotificationTemplateEndpoints
{
    public static IEndpointRouteBuilder MapNotificationTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notification-templates");

        group.MapGet("/", async (string? search, INotificationTemplateAdminService svc, CancellationToken ct) =>
            {
                var rows = await svc.ListAsync(search, ct);
                return Results.Ok(ApiResult<IReadOnlyList<NotificationTemplateDto>>.Ok(rows, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Configuration"));

        group.MapGet("/{id:guid}", async (Guid id, INotificationTemplateAdminService svc, CancellationToken ct) =>
            {
                var row = await svc.GetAsync(id, ct)
                    ?? throw new App.Application.Exceptions.NotFoundException("Template not found.");
                return Results.Ok(ApiResult<NotificationTemplateDto>.Ok(row, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Configuration"));

        group.MapPost("/", async (UpsertNotificationTemplateBody body, INotificationTemplateAdminService svc, CancellationToken ct) =>
            {
                var row = await svc.UpsertAsync(body.ToRequest(), ct);
                return Results.Ok(ApiResult<NotificationTemplateDto>.Ok(row, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Configuration"));

        group.MapPost("/preview", async (PreviewNotificationTemplateBody body, INotificationTemplateAdminService svc, CancellationToken ct) =>
            {
                var preview = await svc.PreviewAsync(body.ToRequest(), body.SampleValues, ct);
                return Results.Ok(ApiResult<NotificationTemplatePreviewDto>.Ok(preview, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Configuration"));

        return app;
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
