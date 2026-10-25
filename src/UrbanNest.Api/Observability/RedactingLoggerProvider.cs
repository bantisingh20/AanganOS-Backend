using Microsoft.Extensions.Logging;

namespace UrbanNest.Api.Observability;

/// <summary>
/// Decorating <see cref="ILoggerProvider"/> that wraps an inner provider (the console) and runs
/// every log entry through <see cref="SensitiveDataRedactor"/> before it is formatted and written
/// (Req 10.4). This is the fail-safe sink-level net: even if some call site logs a password, OTP or
/// token by mistake, the value is masked here on the way out.
///
/// Redaction happens two ways per entry:
/// <list type="bullet">
///   <item><description>the final rendered message (via the formatter output) is scrubbed for
///   sensitive <c>key=value</c> pairs and Authorization headers;</description></item>
///   <item><description>structured state values whose key is sensitive are replaced with the mask
///   so JSON console output never serializes the raw secret as a field.</description></item>
/// </list>
/// </summary>
public sealed class RedactingLoggerProvider(ILoggerProvider inner, SensitiveDataRedactor redactor)
    : ILoggerProvider
{
    private readonly ILoggerProvider _inner = inner;
    private readonly SensitiveDataRedactor _redactor = redactor;

    public ILogger CreateLogger(string categoryName)
        => new RedactingLogger(_inner.CreateLogger(categoryName), _redactor);

    public void Dispose() => _inner.Dispose();

    private sealed class RedactingLogger(ILogger inner, SensitiveDataRedactor redactor) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            // Scope state is usually a dictionary (e.g. correlationId). Mask sensitive field values
            // so they never reach the sink via the scope, keeping the scope's list contract intact.
            if (state is IReadOnlyList<KeyValuePair<string, object?>> pairs)
            {
                return inner.BeginScope(new RedactedState(pairs, redactor));
            }

            return inner.BeginScope(state);
        }

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // When the state is a key/value list (message-template logging + scope dictionaries),
            // wrap it so sensitive FIELD values are masked for structured (JSON) sinks. The
            // formatter is always re-run through RedactText so sensitive key=value pairs and
            // Authorization headers in the rendered message are masked too (the fail-safe net).
            if (state is IReadOnlyList<KeyValuePair<string, object?>> pairs)
            {
                var wrapped = new RedactedState(pairs, redactor);
                inner.Log(
                    logLevel,
                    eventId,
                    wrapped,
                    exception,
                    (s, ex) => redactor.RedactText(formatter(state, ex)));
                return;
            }

            inner.Log(
                logLevel,
                eventId,
                state,
                exception,
                (s, ex) => redactor.RedactText(formatter(state, ex)));
        }
    }

    /// <summary>
    /// A log-state/scope wrapper exposing the same key/value list the structured console serializes,
    /// but with sensitive field values replaced by the mask. Implements the list contract the
    /// logging infrastructure expects of template/scope state.
    /// </summary>
    private sealed class RedactedState : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private readonly IReadOnlyList<KeyValuePair<string, object?>> _inner;
        private readonly SensitiveDataRedactor _redactor;

        public RedactedState(IReadOnlyList<KeyValuePair<string, object?>> inner, SensitiveDataRedactor redactor)
        {
            _inner = inner;
            _redactor = redactor;
        }

        public int Count => _inner.Count;

        public KeyValuePair<string, object?> this[int index]
        {
            get
            {
                var pair = _inner[index];
                return _redactor.IsSensitiveKey(pair.Key)
                    ? new KeyValuePair<string, object?>(pair.Key, SensitiveDataRedactor.Mask)
                    : pair;
            }
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            for (var i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString()
            => _redactor.RedactText(_inner.ToString());
    }
}
