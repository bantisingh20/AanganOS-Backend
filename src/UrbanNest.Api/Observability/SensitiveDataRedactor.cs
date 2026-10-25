using System.Text.RegularExpressions;

namespace UrbanNest.Api.Observability;

/// <summary>
/// Reusable secret-redaction mechanism (Req 10.4). Scrubs known-sensitive values from any text
/// before it reaches a log sink, and from structured log state/scope values. The set of sensitive
/// key names is config-driven (no hardcoded policy — <see cref="ObservabilityOptions"/> supplies
/// the defaults and lets a deployment add more) so passwords, OTP values and session tokens are
/// never written to logs even if some call site logs them by accident.
///
/// Two complementary strategies:
/// <list type="bullet">
///   <item><description><see cref="RedactValueForKey"/> — when a structured field's KEY is sensitive
///   (e.g. <c>password</c>, <c>otp</c>, <c>token</c>), the whole value is replaced.</description></item>
///   <item><description><see cref="RedactText"/> — scans rendered text for <c>key=value</c> /
///   <c>"key":"value"</c> pairs whose key is sensitive and for <c>Authorization: Bearer &lt;token&gt;</c>
///   headers, masking the value in place. This is the fail-safe net at the sink.</description></item>
/// </list>
/// The redactor is immutable and thread-safe, so it is registered as a singleton and shared by the
/// decorating logger provider and any call site that wants to pre-scrub request data before logging.
/// </summary>
public sealed class SensitiveDataRedactor
{
    /// <summary>Placeholder written in place of any redacted secret value.</summary>
    public const string Mask = "***REDACTED***";

    private readonly HashSet<string> _sensitiveKeys;
    private readonly Regex _keyValueRegex;
    private readonly Regex _authHeaderRegex;

    public SensitiveDataRedactor(IEnumerable<string> sensitiveKeys)
    {
        // Case-insensitive, trimmed, deduped. Compared with Contains(substring) so "password",
        // "pwd", "token" also catch "newPassword", "sessionToken", "X-Api-Key", etc.
        _sensitiveKeys = sensitiveKeys
            .Where(static k => !string.IsNullOrWhiteSpace(k))
            .Select(static k => k.Trim().ToLowerInvariant())
            .ToHashSet();

        // Matches  key = value ,  "key": "value" ,  key: value  — capturing the key so we can decide
        // whether to mask. Value runs until a delimiter (quote, comma, semicolon, &, whitespace end).
        _keyValueRegex = new Regex(
            "(?<key>[A-Za-z0-9_.\\-]+)(?<sep>\"?\\s*[:=]\\s*\"?)(?<val>[^\"\\r\\n,;&}]*)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Matches an Authorization header value (optionally with a scheme like Bearer/Basic).
        _authHeaderRegex = new Regex(
            "(?<prefix>authorization\\s*[:=]\\s*\"?)(?<scheme>bearer\\s+|basic\\s+)?(?<val>[^\"\\r\\n,;}]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    /// <summary>True when a structured field name should have its value masked wholesale.</summary>
    public bool IsSensitiveKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var lower = key.Trim().ToLowerInvariant();
        foreach (var sensitive in _sensitiveKeys)
        {
            if (lower.Contains(sensitive, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns the mask when <paramref name="key"/> is sensitive, else the original value.</summary>
    public object? RedactValueForKey(string? key, object? value)
        => IsSensitiveKey(key) ? Mask : value;

    /// <summary>
    /// Scrubs sensitive <c>key=value</c>/<c>"key":"value"</c> pairs and Authorization headers from
    /// arbitrary rendered text. Non-sensitive content is returned unchanged.
    /// </summary>
    public string RedactText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        // Mask Authorization header values first (keep the scheme word for traceability).
        var result = _authHeaderRegex.Replace(text, m =>
            $"{m.Groups["prefix"].Value}{m.Groups["scheme"].Value}{Mask}");

        // Then mask any sensitive key=value / "key":"value" pair.
        result = _keyValueRegex.Replace(result, m =>
        {
            if (!IsSensitiveKey(m.Groups["key"].Value))
            {
                return m.Value;
            }

            return $"{m.Groups["key"].Value}{m.Groups["sep"].Value}{Mask}";
        });

        return result;
    }
}
