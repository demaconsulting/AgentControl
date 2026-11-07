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

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.PackageCredentials;

/// <summary>
///     A logger wrapper that removes anything that could be a secret - URL user information, query
///     strings and fragments, and authorization header values - from every message before it
///     reaches the application log.
/// </summary>
/// <remarks>
///     <para>
///     The NuGet library reports its network activity (which URLs it requested, why a request
///     failed) to the logger it is given. Those messages are valuable for diagnosing an
///     unreachable feed, but a redirect to a download service can carry a signed-URL token in a
///     query string, and an exception message can quote one. Rather than trust every message
///     source, all text is passed through <see cref="Redact"/>, so the rule "secrets,
///     Authorization headers and full URLs with query strings never reach the log" holds
///     regardless of what the library writes. Exceptions are reduced to their type and redacted
///     message (no stack trace or inner exception is forwarded, which keeps the guarantee
///     simple). The user name and password a person types are never given to this logger at all;
///     this wrapper is a second line of defense for the URL-borne and header-borne secrets.
///     Stateless apart from the wrapped logger, and thread-safe when that logger is.
///     </para>
/// </remarks>
internal sealed partial class SecretRedactingLogger : ILogger
{
    /// <summary>
    ///     The text that replaces a removed secret.
    /// </summary>
    private const string Redacted = "[redacted]";

    /// <summary>
    ///     The logger that receives the redacted messages.
    /// </summary>
    private readonly ILogger _inner;

    /// <summary>
    ///     Initializes the wrapper.
    /// </summary>
    /// <param name="inner">The logger that receives the redacted messages.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is
    ///     <see langword="null"/>.</exception>
    public SecretRedactingLogger(ILogger inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>
    ///     Matches an <c>http</c>/<c>https</c> URL inside free text, up to the next character
    ///     that cannot belong to a URL.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(@"https?://[^\s'""<>)\]]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();

    /// <summary>
    ///     Matches an authorization header or scheme followed by its credential
    ///     (<c>Authorization: Basic abc</c>, <c>Bearer abc</c>).
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(
        @"(?<head>(authorization|proxy-authorization)\s*[:=]\s*)(?<scheme>(basic|bearer|negotiate)\s+)?[^\s,;]+|(?<head2>\b(basic|bearer)\s+)[A-Za-z0-9+/=._~-]{8,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationPattern();

    /// <summary>
    ///     Removes secrets from free text.
    /// </summary>
    /// <param name="text">The message text; <see langword="null"/> is treated as empty.</param>
    /// <returns>The text with each URL reduced to scheme, host, port and path (user information,
    ///     query string and fragment removed) and each authorization credential replaced by a
    ///     placeholder.</returns>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Header values first, then URLs, so a URL inside a header value is not half-processed
        var withoutHeaders = AuthorizationPattern().Replace(
            text,
            match => match.Groups["head"].Success
                ? $"{match.Groups["head"].Value}{match.Groups["scheme"].Value}{Redacted}"
                : $"{match.Groups["head2"].Value}{Redacted}");
        return UrlPattern().Replace(withoutHeaders, match => RedactUrl(match.Value));
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => _inner.BeginScope(state);

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

        // Format first, redact the final text, and forward only text: structured values could
        // carry the secrets this wrapper exists to remove
        var message = Redact(formatter(state, null));
        if (exception is not null)
        {
            message += $" ({exception.GetType().Name}: {Redact(exception.Message)})";
        }

        _inner.Log(logLevel, eventId, message, null, static (text, _) => text);
    }

    /// <summary>
    ///     Reduces one URL to scheme, host, port and path.
    /// </summary>
    /// <param name="url">The URL text found in a message.</param>
    /// <returns>The URL without user information, query string or fragment; when the text cannot
    ///     be parsed, everything from the first <c>?</c>, <c>#</c> or <c>@</c> is dropped.</returns>
    private static string RedactUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
            return $"{uri.Scheme}://{uri.Host}{port}{uri.AbsolutePath}";
        }

        var cut = url.IndexOfAny(['?', '#', '@']);
        return cut < 0 ? url : url[..cut] + Redacted;
    }
}
