namespace App.Application.Notifications;

/// <summary>
/// Fills a template string's <c>{token}</c> placeholders from the supplied values (Req 11.1). Kept
/// behind an interface so the substitution strategy is one swappable piece and the service stays
/// agnostic to it. The default implementation does a safe literal <c>{key}</c> → value replacement;
/// an unmatched token is left as-is (never throws) so a missing placeholder degrades the message
/// rather than breaking the enqueue of a (possibly mandatory) notification.
/// </summary>
public interface INotificationTemplateRenderer
{
    /// <summary>Replace <c>{token}</c> occurrences in <paramref name="template"/> with their values.</summary>
    string Render(string template, IReadOnlyDictionary<string, string>? placeholders);
}
