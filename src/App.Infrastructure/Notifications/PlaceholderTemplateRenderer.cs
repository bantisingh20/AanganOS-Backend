using System.Text;
using App.Application.Notifications;

namespace App.Infrastructure.Notifications;

/// <summary>
/// Default <see cref="INotificationTemplateRenderer"/>: a safe literal <c>{token}</c> → value
/// substitution over the template string. A single left-to-right scan (O(n)) replaces each
/// <c>{key}</c> whose key is present in the supplied values; an unmatched <c>{token}</c> is left
/// verbatim so a missing placeholder degrades the text rather than throwing and breaking the
/// enqueue of a (possibly mandatory) notification. No format-string parsing, so a literal brace in
/// user data can never trigger a format exception.
/// </summary>
public sealed class PlaceholderTemplateRenderer : INotificationTemplateRenderer
{
    public string Render(string template, IReadOnlyDictionary<string, string>? placeholders)
    {
        if (string.IsNullOrEmpty(template) || placeholders is null || placeholders.Count == 0)
        {
            return template ?? string.Empty;
        }

        var sb = new StringBuilder(template.Length);
        var i = 0;
        while (i < template.Length)
        {
            var open = template.IndexOf('{', i);
            if (open < 0)
            {
                sb.Append(template, i, template.Length - i);
                break;
            }

            var close = template.IndexOf('}', open + 1);
            if (close < 0)
            {
                // No closing brace — append the remainder literally.
                sb.Append(template, i, template.Length - i);
                break;
            }

            // Append the literal text before the token.
            sb.Append(template, i, open - i);

            var token = template.Substring(open + 1, close - open - 1);
            if (placeholders.TryGetValue(token, out var value))
            {
                sb.Append(value);
            }
            else
            {
                // Unknown token: keep it verbatim so the message is still sent.
                sb.Append(template, open, close - open + 1);
            }

            i = close + 1;
        }

        return sb.ToString();
    }
}
