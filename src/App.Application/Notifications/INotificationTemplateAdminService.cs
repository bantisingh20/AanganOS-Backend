using App.Domain.Notifications;

namespace App.Application.Notifications;

public sealed record NotificationTemplateDto(
    Guid Id,
    string Key,
    string Category,
    NotificationCategoryKind CategoryKind,
    NotificationChannel Channels,
    string TitleTemplate,
    string BodyTemplate,
    string? EmailSubjectTemplate,
    string? EmailHtmlTemplate,
    string? SmsTemplate,
    string? WhatsappTemplateName,
    string? WhatsappVarsJson,
    bool IsActive,
    Guid? CommunityId);

public sealed record UpsertNotificationTemplateRequest(
    string Key,
    int Channels,
    string TitleTemplate,
    string BodyTemplate,
    string? EmailSubjectTemplate,
    string? EmailHtmlTemplate,
    string? SmsTemplate,
    string? WhatsappTemplateName,
    string? WhatsappVarsJson,
    bool IsActive,
    Guid? CommunityId);

public sealed record NotificationTemplatePreviewDto(
    string? PushTitle,
    string? PushBody,
    string? EmailSubject,
    string? EmailHtml,
    string? Sms,
    string? Whatsapp,
    IReadOnlyList<string> Placeholders);

public interface INotificationTemplateAdminService
{
    Task<IReadOnlyList<NotificationTemplateDto>> ListAsync(string? search, CancellationToken cancellationToken = default);

    Task<NotificationTemplateDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<NotificationTemplateDto> UpsertAsync(UpsertNotificationTemplateRequest request, CancellationToken cancellationToken = default);

    Task<NotificationTemplatePreviewDto> PreviewAsync(UpsertNotificationTemplateRequest request, IReadOnlyDictionary<string, string>? sampleValues, CancellationToken cancellationToken = default);
}
