using System.Text.RegularExpressions;
using App.Application.Config;
using App.Application.Notifications;
using App.Domain.Config;
using App.Domain.Notifications;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Notifications;

public sealed partial class NotificationTemplateAdminService(
    AppDbContext db,
    INotificationTemplateRenderer renderer,
    IConfigService config) : INotificationTemplateAdminService
{
    private const string EmailLogoUrlKey = "notification.emailLogoUrl";
    private const string DefaultEmailLogoUrl = "https://aanganosapi.sharcnix.com/assets/email-logo.png";

    public async Task<IReadOnlyList<NotificationTemplateDto>> ListAsync(string? search, CancellationToken cancellationToken = default)
    {
        var query = db.Set<NotificationTemplate>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(t => t.Key.Contains(s) || t.TitleTemplate.Contains(s) || t.Category.Contains(s));
        }

        var rows = await query.OrderBy(t => t.Key).ThenBy(t => t.CommunityId).ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<NotificationTemplateDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.Set<NotificationTemplate>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return row is null ? null : ToDto(row);
    }

    public async Task<NotificationTemplateDto> UpsertAsync(UpsertNotificationTemplateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = request.Key.Trim();

        var row = await db.Set<NotificationTemplate>()
            .FirstOrDefaultAsync(t => t.Key == key && t.CommunityId == request.CommunityId, cancellationToken);

        if (row is null)
        {
            row = new NotificationTemplate
            {
                Key = key,
                Scope = request.CommunityId is null ? ConfigScope.Platform : ConfigScope.Community,
                CommunityId = request.CommunityId,
                Category = NotificationCategories.Account,
                CategoryKind = NotificationCategoryKind.Optional,
            };
            db.Set<NotificationTemplate>().Add(row);
        }

        row.Channels = (NotificationChannel)request.Channels;
        row.TitleTemplate = request.TitleTemplate;
        row.BodyTemplate = request.BodyTemplate;
        row.EmailSubjectTemplate = Blank(request.EmailSubjectTemplate);
        row.EmailHtmlTemplate = Blank(request.EmailHtmlTemplate);
        row.SmsTemplate = Blank(request.SmsTemplate);
        row.WhatsappTemplateName = Blank(request.WhatsappTemplateName);
        row.WhatsappVarsJson = Blank(request.WhatsappVarsJson);
        row.IsActive = request.IsActive;

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(row);
    }

    public async Task<NotificationTemplatePreviewDto> PreviewAsync(
        UpsertNotificationTemplateRequest request,
        IReadOnlyDictionary<string, string>? sampleValues,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tokens = ExtractTokens(request);
        var values = BuildSampleValues(tokens, sampleValues);

        var channels = (NotificationChannel)request.Channels;

        string? pushTitle = null, pushBody = null, emailSubject = null, emailHtml = null, sms = null, whatsapp = null;

        if (channels.HasFlag(NotificationChannel.Push))
        {
            pushTitle = renderer.Render(request.TitleTemplate, values);
            pushBody = renderer.Render(request.BodyTemplate, values);
        }

        if (channels.HasFlag(NotificationChannel.Email))
        {
            emailSubject = renderer.Render(FallbackTo(request.EmailSubjectTemplate, request.TitleTemplate), values);
            var inner = renderer.Render(FallbackTo(request.EmailHtmlTemplate, HtmlEscape(request.BodyTemplate)), values);
            emailHtml = await WrapInLayoutAsync(inner, values, cancellationToken);
        }

        if (channels.HasFlag(NotificationChannel.Sms))
        {
            sms = renderer.Render(FallbackTo(request.SmsTemplate, request.BodyTemplate), values);
        }

        if (channels.HasFlag(NotificationChannel.WhatsApp))
        {
            whatsapp = renderer.Render(FallbackTo(request.SmsTemplate, request.BodyTemplate), values);
        }

        return new NotificationTemplatePreviewDto(pushTitle, pushBody, emailSubject, emailHtml, sms, whatsapp, tokens);
    }

    private async Task<string> WrapInLayoutAsync(string innerHtml, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken)
    {
        var layout = await db.Set<NotificationTemplate>().AsNoTracking()
            .Where(t => t.Key == NotificationTemplateKeys.EmailLayout && t.CommunityId == null && t.IsActive)
            .Select(t => t.EmailHtmlTemplate)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(layout))
        {
            return innerHtml;
        }

        var logoUrl = await config.GetStringAsync(EmailLogoUrlKey, DefaultEmailLogoUrl, cancellationToken: cancellationToken);
        var shell = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["content"] = innerHtml,
            ["logoUrl"] = logoUrl,
            ["communityName"] = values.TryGetValue("communityName", out var cn) ? cn : "your community",
        };
        return renderer.Render(layout, shell);
    }

    private static IReadOnlyList<string> ExtractTokens(UpsertNotificationTemplateRequest r)
    {
        var fields = new[]
        {
            r.TitleTemplate, r.BodyTemplate, r.EmailSubjectTemplate, r.EmailHtmlTemplate, r.SmsTemplate,
        };
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (string.IsNullOrEmpty(field)) continue;
            foreach (Match m in TokenRegex().Matches(field))
            {
                var t = m.Groups[1].Value;
                if (t is "content" or "logoUrl") continue;
                set.Add(t);
            }
        }
        return set.OrderBy(t => t, StringComparer.Ordinal).ToList();
    }

    private static Dictionary<string, string> BuildSampleValues(IReadOnlyList<string> tokens, IReadOnlyDictionary<string, string>? supplied)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            values[token] = supplied is not null && supplied.TryGetValue(token, out var v) && !string.IsNullOrWhiteSpace(v)
                ? v
                : SampleFor(token);
        }
        return values;
    }

    private static string SampleFor(string token) => token.ToLowerInvariant() switch
    {
        var t when t.Contains("name") => "Ramesh Kumar",
        var t when t.Contains("unit") => "A-402",
        var t when t.Contains("date") => "28 Oct 2026",
        var t when t.Contains("time") => "6:30 PM",
        var t when t.Contains("reason") => "Documents incomplete",
        var t when t.Contains("category") => "Delivery",
        var t when t.Contains("community") => "Green Valley",
        var t when t.Contains("otp") || t.Contains("code") => "482913",
        var t when t.Contains("amount") => "1,500",
        var t when t.Contains("status") => "approved",
        _ => $"[{token}]",
    };

    private static string FallbackTo(string? primary, string fallback) =>
        string.IsNullOrWhiteSpace(primary) ? fallback : primary;

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static string HtmlEscape(string text) =>
        System.Net.WebUtility.HtmlEncode(text ?? string.Empty).Replace("\n", "<br/>");

    private static NotificationTemplateDto ToDto(NotificationTemplate t) =>
        new(t.Id, t.Key, t.Category, t.CategoryKind, t.Channels, t.TitleTemplate, t.BodyTemplate,
            t.EmailSubjectTemplate, t.EmailHtmlTemplate, t.SmsTemplate, t.WhatsappTemplateName, t.WhatsappVarsJson,
            t.IsActive, t.CommunityId);

    [GeneratedRegex(@"\{([a-zA-Z0-9_]+)\}")]
    private static partial Regex TokenRegex();
}
