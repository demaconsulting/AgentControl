// Copyright (c) DEMA Consulting
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Collections;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.PackageCredentials;

/// <summary>
///     A logger wrapper that removes anything that could be a secret from everything that reaches
///     the application log: the formatted message, every structured property value, scope state
///     and the exception (which is replaced by a secret-free description).
/// </summary>
/// <remarks>
///     <para>
///     The NuGet library reports its network activity (which URLs it requested, why a request
///     failed) to the logger it is given, and the application's own code logs failures whose
///     exceptions may quote a URL, a header or a credential. Rather than trust every message
///     source, each piece of text is passed through <see cref="SecretRedactor"/>, so the rule
///     "secrets, Authorization headers and full URLs with query strings never reach the log" holds
///     regardless of what the caller writes. Structure is preserved: the message template and the
///     property names are forwarded, with each property value redacted (numbers, booleans and
///     similar values pass through unchanged), so structured sinks keep working. Exceptions are
///     forwarded as <see cref="SanitizedException"/> (redacted type, message chain and stack
///     trace; the original object, its inner exceptions and its data are not forwarded).
///     </para>
///     <para>
///     The user name and password a person types are never given to this logger by AgentControl;
///     this wrapper is the safety net that makes the guarantee hold even when some other code
///     path - or a library - puts a secret into a message. Stateless apart from the wrapped
///     logger, and thread-safe when that logger is.
///     </para>
/// </remarks>
internal sealed class SecretRedactingLogger : ILogger
{
    /// <summary>
    ///     The logger that receives the redacted output.
    /// </summary>
    private readonly ILogger _inner;

    /// <summary>
    ///     Initializes the wrapper.
    /// </summary>
    /// <param name="inner">The logger that receives the redacted output.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is
    ///     <see langword="null"/>.</exception>
    public SecretRedactingLogger(ILogger inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        // Scope state is forwarded like message state: structured pairs with redacted values, or
        // redacted text for anything else
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            return _inner.BeginScope(new RedactedState(pairs, null));
        }

        return _inner.BeginScope(SecretRedactor.Redact(state.ToString()));
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!_inner.IsEnabled(logLevel))
        {
            return;
        }

        // Format first and redact the final text; redact each structured value separately so the
        // properties a sink records are clean too
        var message = SecretRedactor.Redact(formatter(state, null));
        var pairs = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
        var redactedState = new RedactedState(pairs, message);
        var sanitized = exception is null ? null : SecretRedactor.Sanitize(exception);

        _inner.Log(logLevel, eventId, redactedState, sanitized, static (s, _) => s.ToString());
    }

    /// <summary>
    ///     Structured log or scope state whose values have been redacted.
    /// </summary>
    private sealed class RedactedState : IReadOnlyList<KeyValuePair<string, object?>>
    {
        /// <summary>
        ///     The pairs, redacted.
        /// </summary>
        private readonly List<KeyValuePair<string, object?>> _pairs;

        /// <summary>
        ///     The formatted, redacted message, or <see langword="null"/> for scope state.
        /// </summary>
        private readonly string? _message;

        /// <summary>
        ///     Initializes the state from the original pairs.
        /// </summary>
        /// <param name="source">The original pairs.</param>
        /// <param name="message">The redacted message, or <see langword="null"/>.</param>
        public RedactedState(IEnumerable<KeyValuePair<string, object?>> source, string? message)
        {
            _message = message;
            _pairs = [];
            foreach (var pair in source)
            {
                // The original template (a pair named {OriginalFormat}) is text like any other
                _pairs.Add(new KeyValuePair<string, object?>(pair.Key, SecretRedactor.RedactValue(pair.Value)));
            }
        }

        /// <inheritdoc />
        public int Count => _pairs.Count;

        /// <inheritdoc />
        public KeyValuePair<string, object?> this[int index] => _pairs[index];

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _pairs.GetEnumerator();

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc />
        public override string ToString() =>
            _message ?? string.Join(", ", _pairs.Select(pair => $"{pair.Key}={pair.Value}"));
    }
}
