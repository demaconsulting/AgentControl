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

namespace DemaConsulting.AgentControl.PackageCredentials;

/// <summary>
///     The origin of a package feed - its scheme, host and port - which is the unit to which a
///     credential is scoped: a credential typed for one feed is only ever offered to requests for
///     the same origin.
/// </summary>
/// <remarks>
///     <para>
///     Scoping by origin (rather than by full URL) lets one sign-in cover every endpoint of a
///     feed (service index, version list, package download), while guaranteeing that a redirect
///     or a challenge from any other host - which is where a credential leak would happen - never
///     matches. The type is normalized so that two spellings of the same origin compare equal
///     (scheme and host case, an explicit default port) and it is a value: immutable, safe to use
///     as a dictionary key and from any thread.
///     </para>
///     <para>
///     An origin is never built from a URL that embeds user information, and its text form
///     (<see cref="ToString"/>) carries only scheme, host and a non-default port, so it is safe to
///     show to a user and to write to a log: it can never contain a path, query string or secret.
///     </para>
/// </remarks>
internal sealed class FeedOrigin : IEquatable<FeedOrigin>
{
    /// <summary>
    ///     Initializes an origin from already-validated, normalized parts.
    /// </summary>
    /// <param name="scheme">The lower-case scheme, <c>http</c> or <c>https</c>.</param>
    /// <param name="host">The lower-case host name or address, without brackets.</param>
    /// <param name="port">The effective port (the scheme's default when none was written).</param>
    private FeedOrigin(string scheme, string host, int port)
    {
        Scheme = scheme;
        Host = host;
        Port = port;
    }

    /// <summary>
    ///     Gets the lower-case scheme: <c>http</c> or <c>https</c>.
    /// </summary>
    public string Scheme { get; }

    /// <summary>
    ///     Gets the lower-case host name or address (an IPv6 address without brackets).
    /// </summary>
    public string Host { get; }

    /// <summary>
    ///     Gets the effective port; the scheme's default port when the URL named none.
    /// </summary>
    public int Port { get; }

    /// <summary>
    ///     Gets a value indicating whether the origin uses <c>https</c>.
    /// </summary>
    public bool IsSecure => Scheme == Uri.UriSchemeHttps;

    /// <summary>
    ///     Tries to derive the origin of an absolute <c>http</c>/<c>https</c> URI.
    /// </summary>
    /// <param name="uri">The URI; may be <see langword="null"/> or relative, which is refused.</param>
    /// <param name="origin">The origin when the method returns <see langword="true"/>;
    ///     otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="uri"/> is an absolute http or https
    ///     URI without embedded user information. A URI with a user name or password is refused
    ///     because such a URL carries a credential and a feed must not be addressed that way.</returns>
    public static bool TryCreate(Uri? uri, out FeedOrigin? origin)
    {
        origin = null;
        if (uri is null || !uri.IsAbsoluteUri)
        {
            return false;
        }

        // Only web feeds have an origin, and an embedded credential is refused outright
        if ((uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        origin = new FeedOrigin(uri.Scheme.ToLowerInvariant(), uri.IdnHost.ToLowerInvariant(), uri.Port);
        return true;
    }

    /// <summary>
    ///     Tries to derive the origin of a feed URL given as text.
    /// </summary>
    /// <param name="url">The feed URL; may be <see langword="null"/> or blank, which is
    ///     refused.</param>
    /// <param name="origin">The origin when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the text is an absolute http/https URL without user
    ///     information.</returns>
    public static bool TryParse(string? url, out FeedOrigin? origin)
    {
        origin = null;
        return !string.IsNullOrWhiteSpace(url) &&
               Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) &&
               TryCreate(uri, out origin);
    }

    /// <summary>
    ///     Derives the origin of an absolute <c>http</c>/<c>https</c> URI.
    /// </summary>
    /// <param name="uri">The URI.</param>
    /// <returns>The origin.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="uri"/> is not an absolute
    ///     http/https URI or embeds user information. The message never repeats the URI, which
    ///     could contain a credential.</exception>
    public static FeedOrigin Create(Uri uri)
    {
        if (!TryCreate(uri, out var origin) || origin is null)
        {
            throw new ArgumentException(
                "A feed origin needs an absolute http or https URL without a user name or password.", nameof(uri));
        }

        return origin;
    }

    /// <summary>
    ///     Determines whether a URI belongs to this origin.
    /// </summary>
    /// <param name="uri">The URI a request is addressed to; <see langword="null"/>, relative and
    ///     non-web URIs never match.</param>
    /// <returns><see langword="true"/> only when scheme, host and port are all equal (case
    ///     insensitively). A different port, scheme or sub-domain is a different origin, so a
    ///     redirect to any of them never receives this origin's credential.</returns>
    public bool Matches(Uri? uri) => TryCreate(uri, out var other) && Equals(other);

    /// <inheritdoc />
    public bool Equals(FeedOrigin? other) =>
        other is not null && Scheme == other.Scheme && Host == other.Host && Port == other.Port;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as FeedOrigin);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Scheme, Host, Port);

    /// <summary>
    ///     Formats the origin as <c>scheme://host[:port]</c>, omitting the port when it is the
    ///     scheme's default.
    /// </summary>
    /// <returns>The text form, safe to display and to log.</returns>
    public override string ToString() => $"{Scheme}://{HostAndPort}";

    /// <summary>
    ///     Gets the host (in brackets for an IPv6 address) followed by <c>:port</c> when the port
    ///     is not the scheme's default - the <c>host</c> form that Git's credential protocol and
    ///     URL-scoped configuration use.
    /// </summary>
    public string HostAndPort
    {
        get
        {
            var host = Host.Contains(':', StringComparison.Ordinal) ? $"[{Host}]" : Host;
            var defaultPort = IsSecure ? 443 : 80;
            return Port == defaultPort ? host : $"{host}:{Port}";
        }
    }
}
