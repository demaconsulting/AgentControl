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

using System.Text;
using System.Text.RegularExpressions;
using DemaConsulting.NuGet.Caching;

namespace DemaConsulting.AgentControl.PackageCredentials;

/// <summary>
///     Removes anything that could be a secret from text and exceptions before it can reach a log
///     or a status message: the values of authorization headers (whatever the scheme), URL user
///     information, query strings and fragments, well-known secret-bearing keys, and - exactly -
///     every secret value AgentControl currently holds.
/// </summary>
/// <remarks>
///     <para>
///     Redaction works in layers, so that a secret missed by one layer is caught by another.
///     (1) <b>Registered secrets</b>: the passwords or tokens in use (session credentials and the
///     credential being tried) are registered here and every verbatim occurrence - also their
///     Base64 <c>user:secret</c> form and their URL-encoded form - is replaced, whatever text
///     surrounds it. (2) <b>Headers</b>: the complete value of an <c>Authorization</c>,
///     <c>Proxy-Authorization</c>, API-key or cookie header is replaced regardless of its scheme
///     (Basic, Bearer, Digest, NTLM, Negotiate, a custom scheme, or none), up to the end of the
///     line or the closing quote. (3) <b>URLs</b>: found with a small scanner rather than a
///     pattern (so IPv6 literals such as <c>[::1]</c> are understood), each URL is reduced to
///     scheme, host, port and path. (4) <b>Keys</b>: values of keys such as <c>password</c>,
///     <c>token</c> or <c>sig</c>. Redaction is deliberately conservative: it may remove more than
///     strictly necessary but never less.
///     </para>
///     <para>
///     The registry is process-wide because the secrets it protects are (the NuGet credential
///     service is process-wide too). It is thread-safe, reference-counted (the same secret
///     registered twice needs two removals), never enumerated or logged, and holds the secrets only
///     in memory. A secret of any length is registered; a very short one will also be replaced
///     where it occurs inside unrelated text, which is the safe direction.
///     </para>
/// </remarks>
internal static partial class SecretRedactor
{
    /// <summary>
    ///     The text that replaces a removed secret.
    /// </summary>
    internal const string Redacted = "[redacted]";

    /// <summary>
    ///     The most nested inner exceptions that are described by <see cref="Sanitize"/>.
    /// </summary>
    private const int MaxExceptionDepth = 10;

    /// <summary>
    ///     Guards <see cref="_secrets"/> and <see cref="_snapshot"/>.
    /// </summary>
    private static readonly object SecretsLock = new();

    /// <summary>
    ///     The registered secrets with how many times each was registered.
    /// </summary>
    private static readonly Dictionary<string, int> _secrets = new(StringComparer.Ordinal);

    /// <summary>
    ///     The registered secrets, longest first, published for lock-free reads.
    /// </summary>
    private static volatile string[] _snapshot = [];

    /// <summary>
    ///     Registers the secret values of a credential - its password or token, and the forms in
    ///     which that value appears on the wire - so every occurrence is redacted until
    ///     <see cref="Unregister(NuGetCredential)"/> is called.
    /// </summary>
    /// <param name="credential">The credential in use.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="credential"/> is
    ///     <see langword="null"/>.</exception>
    public static void Register(NuGetCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        lock (SecretsLock)
        {
            foreach (var form in GetForms(credential))
            {
                _secrets[form] = _secrets.GetValueOrDefault(form) + 1;
            }

            Publish();
        }
    }

    /// <summary>
    ///     Registers a credential's secret on behalf of one holder - an operation that uses it - and
    ///     returns the lease that withdraws exactly that registration.
    /// </summary>
    /// <remarks>
    ///     Registrations are reference-counted, so each holder's lease keeps the secret redacted
    ///     for as long as the holder lives, independent of every other holder: the session store
    ///     withdrawing its own registration (a credential forgotten after another operation's
    ///     rejection) cannot end redaction for an operation that is still running and may still
    ///     log or throw the secret.
    /// </remarks>
    /// <param name="credential">The credential in use.</param>
    /// <returns>The lease; disposing it more than once withdraws the registration only once.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="credential"/> is
    ///     <see langword="null"/>.</exception>
    public static IDisposable Lease(NuGetCredential credential)
    {
        Register(credential);
        return new RedactionLease(credential);
    }

    /// <summary>
    ///     Withdraws one registration made by <see cref="Register"/>.
    /// </summary>
    /// <param name="credential">The credential that is no longer in use.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="credential"/> is
    ///     <see langword="null"/>.</exception>
    public static void Unregister(NuGetCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        lock (SecretsLock)
        {
            foreach (var form in GetForms(credential))
            {
                if (!_secrets.TryGetValue(form, out var count))
                {
                    continue;
                }

                if (count <= 1)
                {
                    _secrets.Remove(form);
                }
                else
                {
                    _secrets[form] = count - 1;
                }
            }

            Publish();
        }
    }

    /// <summary>
    ///     Removes secrets from free text.
    /// </summary>
    /// <param name="text">The text; <see langword="null"/> is treated as empty.</param>
    /// <returns>The text with registered secrets, authorization header values, URL user
    ///     information, query strings and fragments, and secret-bearing key values removed.</returns>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Exact secrets first: they need no pattern, so they are removed even from text no rule
        // anticipated, and removing them first stops a URL or header rule seeing half a secret
        var result = text;
        foreach (var secret in _snapshot)
        {
            result = result.Replace(secret, Redacted, StringComparison.Ordinal);
        }

        result = RedactValues(result, HeaderPattern(), toEndOfLine: true);
        result = RedactUrls(result);
        result = RedactValues(result, SecretKeyPattern(), toEndOfLine: false);
        result = BareCredentialPattern().Replace(result, Redacted);
        return result;
    }

    /// <summary>
    ///     Redacts a value for use as a log property or scope value.
    /// </summary>
    /// <param name="value">The value; <see langword="null"/> stays <see langword="null"/>.</param>
    /// <returns>Numbers, booleans, enums, dates and similar value types unchanged (they cannot carry
    ///     a secret and keep their type for structured logging); everything else as redacted
    ///     text.</returns>
    public static object? RedactValue(object? value)
    {
        return value switch
        {
            null => null,
            string text => Redact(text),
            bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
                or char or Guid or DateTime or DateTimeOffset or TimeSpan or Enum => value,
            _ => Redact(value.ToString())
        };
    }

    /// <summary>
    ///     Creates a secret-free description of an exception.
    /// </summary>
    /// <remarks>
    ///     The original exception object is not kept: it, its inner exceptions or its
    ///     <see cref="Exception.Data"/> could carry headers, URLs or credentials that no
    ///     message-level redaction can reach. What survives is the redacted type name, message and
    ///     stack trace of every exception in the chain.
    /// </remarks>
    /// <param name="exception">The exception to describe.</param>
    /// <returns>A new exception whose text (message, <c>ToString</c>, stack trace) is
    ///     redacted.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="exception"/> is
    ///     <see langword="null"/>.</exception>
    public static SanitizedException Sanitize(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var message = new StringBuilder();
        AppendChain(message, exception, 0);
        return SanitizedException.FromRedactedText(
            exception.GetType().FullName ?? exception.GetType().Name,
            message.ToString(),
            Redact(exception.StackTrace));
    }

    /// <summary>
    ///     Gets the redacted message of an exception, for status text.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>The exception's message with secrets removed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="exception"/> is
    ///     <see langword="null"/>.</exception>
    public static string SafeMessage(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Redact(exception.Message);
    }

    /// <summary>
    ///     Matches the name of a secret-bearing header and the separator that follows it; the
    ///     value itself is found by <see cref="FindValueEnd"/>, which understands quoting and
    ///     escaping.
    /// </summary>
    /// <returns>The compiled expression; group <c>head</c> is the header name and separator that
    ///     stay. The name may be followed by a quote in any of its written forms (<c>"</c>,
    ///     <c>\"</c>, <c>\u0022</c>, <c>&amp;quot;</c>).</returns>
    [GeneratedRegex(
        """(?<head>(?:\b|(?<=u00(?:22|27)))(?:proxy-authorization|authorization|x-nuget-apikey|x-api-key|set-cookie|cookie)\b(?:[ \t]+header)?(?:\\*["']|\\+u00(?:22|27)|&quot;|&#3[49];)?[ \t]*[:=][ \t]*)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeaderPattern();

    /// <summary>
    ///     Matches the name of a key whose name says its value is secret, and the separator that
    ///     follows it; the value itself is found by <see cref="FindValueEnd"/>.
    /// </summary>
    /// <returns>The compiled expression; group <c>head</c> is the key and separator that
    ///     stay.</returns>
    [GeneratedRegex(
        """(?<head>(?:\b|(?<=u00(?:22|27)))(?:password|passwd|pwd|secret|client[_-]?secret|token|access[_-]?token|refresh[_-]?token|api[_-]?key|apikey|sig|signature)\b(?:\\*["']|\\+u00(?:22|27)|&quot;|&#3[49];)?[ \t]*[:=][ \t]*)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretKeyPattern();

    /// <summary>
    ///     Matches a credential that appears without a header or key around it: a scheme word
    ///     followed by a token, or a JSON web token.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(
        @"\b(?:basic|bearer|negotiate|ntlm)[ \t]+(?:[A-Za-z0-9+/=._~-]|\\.){8,}|\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BareCredentialPattern();

    /// <summary>
    ///     Replaces the value that follows each match of a header or key pattern.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="head">The pattern that finds the name and separator.</param>
    /// <param name="toEndOfLine">Whether an unquoted value runs to the end of the line (headers,
    ///     whose values may contain spaces and commas) rather than to the next delimiter.</param>
    /// <returns>The text with each value replaced by the placeholder.</returns>
    private static string RedactValues(string text, Regex head, bool toEndOfLine)
    {
        var builder = new StringBuilder(text.Length);
        var position = 0;
        var searchFrom = 0;
        while (searchFrom <= text.Length)
        {
            var match = head.Match(text, searchFrom);
            if (!match.Success)
            {
                break;
            }

            var valueStart = match.Index + match.Length;
            var valueEnd = FindValueEnd(text, valueStart, toEndOfLine);
            searchFrom = valueEnd;
            if (valueEnd == valueStart && !toEndOfLine)
            {
                // A key with no value after it has nothing to hide
                continue;
            }

            builder.Append(text, position, valueStart - position).Append(Redacted);
            position = valueEnd;
        }

        return builder.Append(text, position, text.Length - position).ToString();
    }

    /// <summary>
    ///     Finds where the value that starts at <paramref name="start"/> ends, understanding
    ///     quoting and escaping, and redacting to the end of the line whenever it cannot be sure.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The index of the first character of the value.</param>
    /// <param name="toEndOfLine">Whether an unquoted value runs to the end of the line.</param>
    /// <returns>The index just after the value. A value in plain quotes ends after its closing
    ///     quote, skipping backslash escapes (<c>\"</c>, <c>\\</c>, <c>\u0022</c>) and doubled
    ///     quotes; a value that starts with an escaped or entity-encoded quote (nested JSON, HTML)
    ///     runs to the end of the line, as does an unterminated one.</returns>
    private static int FindValueEnd(string text, int start, bool toEndOfLine)
    {
        if (start >= text.Length)
        {
            return start;
        }

        var c = text[start];
        if (c is '"' or '\'')
        {
            return ScanQuoted(text, start + 1, c);
        }

        if (c == '\\' || text.AsSpan(start).StartsWith("&quot;", StringComparison.OrdinalIgnoreCase)
            || text.AsSpan(start).StartsWith("&#3", StringComparison.Ordinal))
        {
            return EndOfLine(text, start);
        }

        return toEndOfLine ? EndOfLine(text, start) : ScanUnquoted(text, start);
    }

    /// <summary>
    ///     Finds the end of a quoted value.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="from">The index just after the opening quote.</param>
    /// <param name="quote">The quote character that opened the value.</param>
    /// <returns>The index just after the closing quote - one that is neither escaped by a backslash
    ///     nor doubled - or the end of the line when there is none.</returns>
    private static int ScanQuoted(string text, int from, char quote)
    {
        var index = from;
        while (index < text.Length)
        {
            var c = text[index];
            if (c is '\r' or '\n')
            {
                return index;
            }

            if (c == '\\')
            {
                // The escaped character belongs to the value, a quote included (a line break does not)
                var escapesLineBreak = index + 1 < text.Length && text[index + 1] is '\r' or '\n';
                index += escapesLineBreak || index + 1 >= text.Length ? 1 : 2;
                continue;
            }

            if (c == quote)
            {
                if (index + 1 < text.Length && text[index + 1] == quote)
                {
                    index += 2;
                    continue;
                }

                return index + 1;
            }

            index++;
        }

        return text.Length;
    }

    /// <summary>
    ///     Finds the end of an unquoted key value: the next delimiter, where a quote preceded by an
    ///     odd number of backslashes is an escaped part of the value rather than a delimiter.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The index of the first character of the value.</param>
    /// <returns>The index of the delimiter, or the end of the text.</returns>
    private static int ScanUnquoted(string text, int start)
    {
        var index = start;
        for (; index < text.Length; index++)
        {
            var c = text[index];
            // A closing parenthesis or bracket ends the value only when it is plausibly the end of
            // surrounding text; inside a value it is part of the (secret) value
            if (!(char.IsWhiteSpace(c) || c is '&' or ';' or ',' or '\'' or '"' ||
                  (c is ')' or ']' && IsClosingContext(text, index))))
            {
                continue;
            }

            if (c is '\'' or '"' && CountBackslashesBefore(text, index, start) % 2 == 1)
            {
                continue;
            }

            break;
        }

        return index;
    }

    /// <summary>
    ///     Counts the backslashes immediately before an index.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="index">The index.</param>
    /// <param name="limit">The lowest index that is looked at.</param>
    /// <returns>The length of the run of backslashes that ends just before <paramref name="index"/>.</returns>
    private static int CountBackslashesBefore(string text, int index, int limit)
    {
        var count = 0;
        for (var i = index - 1; i >= limit && text[i] == '\\'; i--)
        {
            count++;
        }

        return count;
    }

    /// <summary>
    ///     Finds the end of the line that contains an index.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The index.</param>
    /// <returns>The index of the next line break, or the end of the text.</returns>
    private static int EndOfLine(string text, int start)
    {
        var end = text.AsSpan(start).IndexOfAny('\r', '\n');
        return end < 0 ? text.Length : start + end;
    }

    /// <summary>
    ///     Reduces every URL in the text to scheme, host, port and path.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The text with each URL reduced.</returns>
    private static string RedactUrls(string text)
    {
        var builder = new StringBuilder(text.Length);
        var position = 0;
        while (true)
        {
            var separator = text.IndexOf("://", position, StringComparison.Ordinal);
            if (separator < 0)
            {
                break;
            }

            // The scheme is the run of scheme characters before "://", starting with a letter
            var start = separator;
            while (start > position && IsSchemeCharacter(text[start - 1]))
            {
                start--;
            }

            while (start < separator && !char.IsAsciiLetter(text[start]))
            {
                start++;
            }

            if (start == separator)
            {
                builder.Append(text, position, separator + 3 - position);
                position = separator + 3;
                continue;
            }

            var end = FindUrlEnd(text, separator + 3);
            builder.Append(text, position, start - position).Append(ReduceUrl(text[start..end]));
            position = end;
        }

        return builder.Append(text, position, text.Length - position).ToString();
    }

    /// <summary>
    ///     Finds where a URL that starts at <paramref name="from"/> ends.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="from">The index just after <c>://</c>.</param>
    /// <returns>The index of the first character that cannot belong to the URL. Square brackets
    ///     are part of it only as a balanced pair, which is how an IPv6 host is written;
    ///     parentheses are part of it when balanced (a legal path or query character, as in
    ///     <c>/wiki/Foo_(bar)</c>), and an unmatched <c>)</c> ends it only when it is followed by
    ///     the end of the text, whitespace or closing or sentence punctuation (see
    ///     <see cref="IsClosingContext"/>), which is how a parenthesized or markdown-style link
    ///     ends.</returns>
    private static int FindUrlEnd(string text, int from)
    {
        var inBrackets = false;
        var parenthesisDepth = 0;
        var index = from;
        while (index < text.Length)
        {
            var c = text[index];
            if (c == '\\')
            {
                // A backslash run before a quote: an odd run escapes the quote, which then belongs
                // to the URL (it is a delimiter of a surrounding string that the URL runs through,
                // e.g. a JSON string whose address is followed by more escaped text); an even run
                // leaves the quote as a real delimiter. Other escapes (\u0022, \n) are ordinary text.
                var run = index;
                while (run < text.Length && text[run] == '\\')
                {
                    run++;
                }

                var escapesQuote = run < text.Length && text[run] is '"' or '\'' && (run - index) % 2 == 1;
                index = escapesQuote ? run + 1 : run;
                continue;
            }

            if (c is '"' or '\'' && index + 1 < text.Length && text[index + 1] == c)
            {
                // A doubled quote is an escaped quote
                index += 2;
                continue;
            }

            if (c == '[')
            {
                inBrackets = true;
            }
            else if (c == ']')
            {
                if (!inBrackets)
                {
                    break;
                }

                inBrackets = false;
            }
            else if (c == '(')
            {
                parenthesisDepth++;
            }
            else if (c == ')' && parenthesisDepth > 0)
            {
                // The partner of a '(' inside the URL (a legal path or query character)
                parenthesisDepth--;
            }
            else if (char.IsWhiteSpace(c) || c is '\'' or '"' or '<' or '>' or '`' or '|' or '{' or '}' ||
                     (c == ')' && !inBrackets && IsClosingContext(text, index)))
            {
                break;
            }

            index++;
        }

        return index;
    }

    /// <summary>
    ///     Tells whether an unmatched closing parenthesis or bracket is the end of surrounding text
    ///     (a parenthesized or markdown-style URL) rather than a character inside a value.
    /// </summary>
    /// <remarks>
    ///     An unmatched closer followed by more value characters (<c>?a=b)c=d</c>) is treated as
    ///     part of the value, so that the remainder of a query or secret is never left in the
    ///     output; the cost is that a URL immediately followed by a letter after its closing
    ///     parenthesis is reduced a little more than strictly necessary, which is the safe direction.
    /// </remarks>
    /// <param name="text">The text.</param>
    /// <param name="index">The index of the closing character.</param>
    /// <returns><see langword="true"/> when the closer is last in the text or is followed by
    ///     whitespace, a sentence or closing punctuation, or a backslash.</returns>
    private static bool IsClosingContext(string text, int index)
    {
        if (index + 1 >= text.Length)
        {
            return true;
        }

        var next = text[index + 1];
        return char.IsWhiteSpace(next) || next is '.' or ',' or ';' or ':' or '!' or '?' or ')' or ']' or '}' or '>' or
            '\'' or '"' or '\\';
    }

    /// <summary>
    ///     Reduces one URL to scheme, host, port and path.
    /// </summary>
    /// <param name="url">The URL text, from its scheme to its last character.</param>
    /// <returns>The URL without user information, query string or fragment. When an <c>@</c>
    ///     appears anywhere before the query, everything up to the last one is treated as user
    ///     information, even if that also removes a host, so a password containing <c>/</c>,
    ///     <c>?</c> or <c>#</c> cannot survive.</returns>
    private static string ReduceUrl(string url)
    {
        // Sentence punctuation after the URL is not part of it, and is kept
        var trimmed = url.TrimEnd('.', ',', ';', ':', '!');
        var trailing = url[trimmed.Length..];

        var schemeEnd = trimmed.IndexOf("://", StringComparison.Ordinal);
        var scheme = trimmed[..schemeEnd];
        var rest = trimmed[(schemeEnd + 3)..];

        // User information: an '@' in the authority (up to the first '/'), or anywhere before the
        // query or fragment
        var queryStart = rest.IndexOfAny(['?', '#']);
        var beforeQuery = queryStart < 0 ? rest : rest[..queryStart];
        var authorityEnd = rest.IndexOf('/');
        var authority = authorityEnd < 0 ? rest : rest[..authorityEnd];
        var lastAt = Math.Max(beforeQuery.LastIndexOf('@'), authority.LastIndexOf('@'));
        if (lastAt >= 0)
        {
            rest = rest[(lastAt + 1)..];
            queryStart = rest.IndexOfAny(['?', '#']);
        }

        var withoutQuery = queryStart < 0 ? rest : rest[..queryStart];
        return $"{scheme}://{withoutQuery}{trailing}";
    }

    /// <summary>
    ///     Appends the redacted description of an exception and its inner exceptions.
    /// </summary>
    /// <param name="builder">Receives the text.</param>
    /// <param name="exception">The exception.</param>
    /// <param name="depth">How deeply nested the exception is.</param>
    private static void AppendChain(StringBuilder builder, Exception exception, int depth)
    {
        if (depth > 0)
        {
            builder.Append(" ---> ");
        }

        builder.Append(exception.GetType().Name).Append(": ").Append(Redact(exception.Message));
        if (depth >= MaxExceptionDepth)
        {
            return;
        }

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                AppendChain(builder, inner, depth + 1);
            }
        }
        else if (exception.InnerException is not null)
        {
            AppendChain(builder, exception.InnerException, depth + 1);
        }
    }

    /// <summary>
    ///     Gets the text forms of a credential that must be redacted.
    /// </summary>
    /// <param name="credential">The credential.</param>
    /// <returns>The secret itself, its URL-encoded form and the Base64 form of
    ///     <c>user:secret</c> used by HTTP Basic authentication; empty when the secret is
    ///     empty.</returns>
    private static IEnumerable<string> GetForms(NuGetCredential credential)
    {
        var secret = credential.Secret;
        if (string.IsNullOrEmpty(secret))
        {
            return [];
        }

        var forms = new HashSet<string>(StringComparer.Ordinal) { secret };
        var encoded = Uri.EscapeDataString(secret);
        if (encoded.Length > 0)
        {
            forms.Add(encoded);
        }

        forms.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credential.UserName}:{secret}")));
        return forms;
    }

    /// <summary>
    ///     Publishes the registered secrets, longest first so a secret that contains another is
    ///     replaced whole. Call with <see cref="SecretsLock"/> held.
    /// </summary>
    private static void Publish() =>
        _snapshot = [.. _secrets.Keys.OrderByDescending(secret => secret.Length)];

    /// <summary>
    ///     Tells whether a character can be part of a URL scheme.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><see langword="true"/> for letters, digits, <c>+</c>, <c>-</c> and <c>.</c>.</returns>
    private static bool IsSchemeCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.';

    /// <summary>
    ///     One holder's registration of a secret; withdraws it exactly once.
    /// </summary>
    private sealed class RedactionLease : IDisposable
    {
        /// <summary>
        ///     The credential, or <see langword="null"/> once the lease has been disposed.
        /// </summary>
        private NuGetCredential? _credential;

        /// <summary>
        ///     Initializes the lease over an already registered credential.
        /// </summary>
        /// <param name="credential">The credential that was registered for this lease.</param>
        public RedactionLease(NuGetCredential credential) => _credential = credential;

        /// <inheritdoc />
        public void Dispose()
        {
            var credential = Interlocked.Exchange(ref _credential, null);
            if (credential is not null)
            {
                Unregister(credential);
            }
        }
    }
}

/// <summary>
///     A secret-free stand-in for an exception: it keeps the original type name, a redacted message
///     (including the redacted messages of every inner exception) and a redacted stack trace, and
///     nothing else.
/// </summary>
/// <remarks>
///     Created by <see cref="SecretRedactor.Sanitize"/>. It never wraps the original exception, so
///     the original's headers, URLs, <see cref="Exception.Data"/> or inner exceptions cannot be
///     reached through it, and <see cref="ToString"/> is safe to log.
/// </remarks>
public sealed class SanitizedException : Exception
{
    /// <summary>
    ///     The redacted stack trace of the original exception.
    /// </summary>
    private readonly string _stackTrace;

    /// <summary>
    ///     Initializes the stand-in. Internal so that every instance comes from
    ///     <see cref="SecretRedactor.Sanitize"/> or from a caller in this assembly; whatever text it
    ///     is given here is redacted by the constructor, so the type's secret-free contract does not
    ///     depend on every caller remembering to redact.
    /// </summary>
    /// <param name="originalTypeName">The type name of the original exception.</param>
    /// <param name="message">The message chain; redacted by the constructor.</param>
    /// <param name="stackTrace">The stack trace; redacted by the constructor.</param>
    internal SanitizedException(string originalTypeName, string message, string stackTrace)
        : this(originalTypeName, message, stackTrace, alreadyRedacted: false)
    {
    }

    /// <summary>
    ///     Initializes the stand-in, redacting the text unless it has already been redacted piece
    ///     by piece.
    /// </summary>
    /// <remarks>
    ///     A message chain is assembled from separately redacted messages; redacting the assembled
    ///     text again would let a rest-of-line rule (an authorization header) swallow the exceptions
    ///     that follow it on the same line, so <see cref="SecretRedactor.Sanitize"/> uses
    ///     <see cref="FromRedactedText"/>, which skips the second pass.
    /// </remarks>
    /// <param name="originalTypeName">The type name of the original exception.</param>
    /// <param name="message">The message chain.</param>
    /// <param name="stackTrace">The stack trace.</param>
    /// <param name="alreadyRedacted"><see langword="true"/> when the text has been redacted by the
    ///     caller; <see langword="false"/> to redact it here.</param>
    private SanitizedException(string originalTypeName, string message, string stackTrace, bool alreadyRedacted)
        : base(alreadyRedacted ? message : SecretRedactor.Redact(message))
    {
        OriginalTypeName = alreadyRedacted ? originalTypeName : SecretRedactor.Redact(originalTypeName);
        _stackTrace = alreadyRedacted ? stackTrace : SecretRedactor.Redact(stackTrace);
    }

    /// <summary>
    ///     Creates the stand-in from text that the caller has already redacted piece by piece.
    /// </summary>
    /// <param name="originalTypeName">The redacted type name.</param>
    /// <param name="message">The redacted message chain.</param>
    /// <param name="stackTrace">The redacted stack trace.</param>
    /// <returns>The stand-in, holding the text as given.</returns>
    internal static SanitizedException FromRedactedText(string originalTypeName, string message, string stackTrace) =>
        new(originalTypeName, message, stackTrace, alreadyRedacted: true);

    /// <summary>
    ///     Gets the type name of the exception this one stands in for.
    /// </summary>
    public string OriginalTypeName { get; }

    /// <inheritdoc />
    public override string? StackTrace => _stackTrace.Length == 0 ? null : _stackTrace;

    /// <inheritdoc />
    public override string ToString() =>
        _stackTrace.Length == 0
            ? $"{OriginalTypeName}: {Message}"
            : $"{OriginalTypeName}: {Message}{Environment.NewLine}{_stackTrace}";
}
