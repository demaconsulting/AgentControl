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

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DemaConsulting.AgentControl.AgentPackageManagement;

/// <summary>
///     The kind of transport a Git repository address uses, which decides the one transport git is
///     allowed to use for it.
/// </summary>
internal enum GitAddressKind
{
    /// <summary>
    ///     An <c>https://host/path</c> address with no user information.
    /// </summary>
    Https,

    /// <summary>
    ///     An <c>ssh://[user@]host[:port]/path</c> address or a <c>user@host:path</c> address.
    /// </summary>
    Ssh,

    /// <summary>
    ///     An absolute local or UNC path to a repository.
    /// </summary>
    Local
}

/// <summary>
///     A validated Git repository address: the text git is given, the one transport it may use,
///     and a stable key for the cache folder of the repository.
/// </summary>
/// <remarks>
///     <para>
///     An address is untrusted input (a settings file can be edited by hand) that ends up on a
///     git command line, so it is accepted only in a few exact forms: <c>https</c> with no user
///     information, <c>ssh</c> (URL or <c>user@host:path</c> form, a user name but never a
///     password), and an absolute local or UNC path. Everything else is refused: plain
///     <c>http</c>, <c>git://</c>, <c>file://</c>, any other scheme, remote-helper forms
///     (<c>ext::</c>, <c>fd::</c>, <c>x::y</c>), relative paths, a leading <c>-</c> (which git
///     could read as an option), control characters, a query or fragment, and credentials
///     embedded in the address.
///     </para>
///     <para>
///     Parsing never throws and the messages it produces never repeat the input, so a secret that
///     was pasted into the address cannot reach an error display or a log. Instances are
///     immutable and thread-safe. Existence of a local path is not checked here: an offline share
///     must not make a settings file unusable.
///     </para>
/// </remarks>
internal sealed partial class GitRepositoryAddress
{
    /// <summary>
    ///     The longest address accepted.
    /// </summary>
    private const int MaxLength = 2048;

    /// <summary>
    ///     The number of hexadecimal characters of the cache key.
    /// </summary>
    private const int CacheKeyLength = 16;

    /// <summary>
    ///     The text of the message for an address that is not one of the accepted forms.
    /// </summary>
    private const string FormsMessage =
        "Use an https:// address, an ssh:// or user@host:path address, or the full path of a local repository.";

    /// <summary>
    ///     Initializes an address that has been validated.
    /// </summary>
    /// <param name="kind">The transport kind.</param>
    /// <param name="value">The text to give to git.</param>
    /// <param name="normalized">The normalized text the cache key is derived from.</param>
    private GitRepositoryAddress(GitAddressKind kind, string value, string normalized)
    {
        Kind = kind;
        Value = value;
        CacheKey = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(kind.ToString() + "|" + normalized)))[..CacheKeyLength];
    }

    /// <summary>
    ///     Gets the transport kind.
    /// </summary>
    public GitAddressKind Kind { get; }

    /// <summary>
    ///     Gets the address text to give to git (the input, trimmed).
    /// </summary>
    public string Value { get; }

    /// <summary>
    ///     Gets a stable key that names the cache folder of this repository: 16 lower-case
    ///     hexadecimal digits derived from the kind and the normalized address. Addresses that
    ///     differ only in the case of the scheme or host (or, on Windows, of a local path) share a
    ///     key.
    /// </summary>
    public string CacheKey { get; }

    /// <summary>
    ///     Gets the name of the one transport git may use for this address, for git's
    ///     <c>GIT_ALLOW_PROTOCOL</c> setting: <c>https</c>, <c>ssh</c> or <c>file</c>.
    /// </summary>
    public string GitAllowProtocolName => Kind switch
    {
        GitAddressKind.Https => "https",
        GitAddressKind.Ssh => "ssh",
        _ => "file"
    };

    /// <summary>
    ///     Gets the pattern of a host name: letters, digits, dots and hyphens.
    /// </summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex HostPattern();

    /// <summary>
    ///     Gets the pattern of a bracketed IPv6 host literal.
    /// </summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex(@"^\[[0-9A-Fa-f:.]+\]\z", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv6Pattern();

    /// <summary>
    ///     Gets the pattern of a user name in an ssh address: letters, digits, dots, underscores
    ///     and hyphens, not starting with a hyphen.
    /// </summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex(@"^[A-Za-z0-9._][A-Za-z0-9._-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex UserPattern();

    /// <summary>
    ///     Gets the pattern of a <c>scheme://</c> prefix.
    /// </summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex(@"^([A-Za-z][A-Za-z0-9+.-]*)://", RegexOptions.CultureInvariant)]
    private static partial Regex SchemePattern();

    /// <summary>
    ///     Gets the pattern of a scp-like <c>user@host:path</c> address.
    /// </summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex(@"^([A-Za-z0-9._][A-Za-z0-9._-]*)@([A-Za-z0-9][A-Za-z0-9.-]*):(.+)\z", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex ScpPattern();

    /// <summary>
    ///     Validates the text of a repository address.
    /// </summary>
    /// <param name="text">The address as typed or read from settings; may be <see langword="null"/>.</param>
    /// <param name="address">The validated address when the method returns <see langword="true"/>;
    ///     otherwise <see langword="null"/>.</param>
    /// <param name="problem">A message the user can act on when the method returns
    ///     <see langword="false"/> (it never repeats <paramref name="text"/>); otherwise
    ///     <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the address is acceptable.</returns>
    public static bool TryParse(string? text, out GitRepositoryAddress? address, out string? problem)
    {
        problem = Validate(text, out address);
        return problem is null;
    }

    /// <summary>
    ///     Gets the problem with an address, if any.
    /// </summary>
    /// <param name="text">The address text; may be <see langword="null"/>.</param>
    /// <returns><see langword="null"/> when the address is acceptable; otherwise a message that
    ///     never repeats the text.</returns>
    public static string? GetProblem(string? text) => Validate(text, out _);

    /// <summary>
    ///     Determines whether an address text embeds a credential, which must never be persisted:
    ///     any user information in a URL other than a plain ssh user name, or a query string or
    ///     fragment (where access tokens travel) on a URL.
    /// </summary>
    /// <param name="text">The address text; may be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the text looks like a URL that carries a user name
    ///     and password, a token, or a query/fragment. Judged on the text alone, so it also holds
    ///     for an address that is invalid for another reason (for example <c>http://user:pw@host</c>).</returns>
    public static bool EmbedsCredential(string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        var scheme = SchemePattern().Match(value);
        if (!scheme.Success)
        {
            return false;
        }

        var rest = value[scheme.Length..];
        if (rest.Contains('?', StringComparison.Ordinal) || rest.Contains('#', StringComparison.Ordinal))
        {
            return true;
        }

        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        var authority = slash >= 0 ? rest[..slash] : rest;
        var at = authority.LastIndexOf('@');
        if (at < 0)
        {
            return false;
        }

        // A bare ssh user name (git@host) is not a secret; anything else in front of '@' may be
        return !scheme.Groups[1].Value.Equals("ssh", StringComparison.OrdinalIgnoreCase) ||
               authority[..at].Contains(':', StringComparison.Ordinal);
    }

    /// <summary>
    ///     Validates an address and builds it.
    /// </summary>
    /// <param name="text">The address text.</param>
    /// <param name="address">The address, or <see langword="null"/> on a problem.</param>
    /// <returns>The problem message, or <see langword="null"/> when the address is acceptable.</returns>
    private static string? Validate(string? text, out GitRepositoryAddress? address)
    {
        address = null;
        var value = text?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return "Enter the address of the Git repository. " + FormsMessage;
        }

        if (value.Length > MaxLength)
        {
            return "The repository address is too long.";
        }

        if (value.Any(char.IsControl))
        {
            return "The repository address must not contain control characters or line breaks.";
        }

        // A leading '-' would be read by git (or ssh) as an option, and '::' is git's syntax for
        // a remote helper (ext::, fd::, ...) that runs a program. A scheme://... URL cannot be
        // one (its first '::' would come after the scheme's colon-slash-slash), and may legitimately
        // contain '::' in an IPv6 host
        if (value.StartsWith('-'))
        {
            return "The repository address must not start with '-'.";
        }

        var scheme = SchemePattern().Match(value);
        if (!scheme.Success && value.Contains("::", StringComparison.Ordinal))
        {
            return "Remote-helper addresses (such as ext:: or fd::) are not supported. " + FormsMessage;
        }

        if (scheme.Success)
        {
            return ValidateUrl(value, scheme.Groups[1].Value, scheme.Length, out address);
        }

        var scp = ScpPattern().Match(value);
        if (scp.Success)
        {
            return ValidateScp(value, scp, out address);
        }

        if (Path.IsPathFullyQualified(value))
        {
            address = new GitRepositoryAddress(GitAddressKind.Local, value, NormalizeLocalPath(value));
            return null;
        }

        return "A relative or ambiguous repository address is not supported. " + FormsMessage;
    }

    /// <summary>
    ///     Validates a <c>scheme://</c> address.
    /// </summary>
    /// <param name="value">The trimmed address.</param>
    /// <param name="scheme">The scheme as written.</param>
    /// <param name="schemeLength">The length of the <c>scheme://</c> prefix.</param>
    /// <param name="address">The address, or <see langword="null"/> on a problem.</param>
    /// <returns>The problem message, or <see langword="null"/>.</returns>
    private static string? ValidateUrl(string value, string scheme, int schemeLength, out GitRepositoryAddress? address)
    {
        address = null;
        var lowerScheme = scheme.ToLowerInvariant();
        if (lowerScheme is not ("https" or "ssh"))
        {
            return "Only https and ssh addresses are supported (not http, git, file or other schemes). " + FormsMessage;
        }

        if (value.Contains('?', StringComparison.Ordinal) || value.Contains('#', StringComparison.Ordinal))
        {
            return "The repository address must not contain a query string or fragment.";
        }

        if (value.Any(char.IsWhiteSpace) || value.Contains('\\', StringComparison.Ordinal))
        {
            return "The repository address must not contain spaces or backslashes.";
        }

        var rest = value[schemeLength..];
        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash == rest.Length - 1)
        {
            return "The repository address must include a host and a path, for example https://host/org/repo.git.";
        }

        var authority = rest[..slash];
        var path = rest[slash..];
        var at = authority.LastIndexOf('@');
        var userInfo = at >= 0 ? authority[..at] : null;
        var hostAndPort = at >= 0 ? authority[(at + 1)..] : authority;

        // Credentials never belong in the address: it is persisted, shown and logged
        if (lowerScheme == "https" && at >= 0)
        {
            return "An https repository address must not contain a user name, password or token; " +
                   "AgentControl never stores credentials. Sign in through your Git credential helper.";
        }

        if (userInfo is not null)
        {
            if (userInfo.Contains(':', StringComparison.Ordinal))
            {
                return "An ssh repository address must not contain a password; AgentControl never stores credentials.";
            }

            if (!UserPattern().IsMatch(userInfo))
            {
                return "The user name in the repository address contains characters that are not allowed.";
            }
        }

        if (!TrySplitHostAndPort(hostAndPort, out var host, out var port))
        {
            return "The host (and port) in the repository address is not valid.";
        }

        var kind = lowerScheme == "https" ? GitAddressKind.Https : GitAddressKind.Ssh;
        var normalized = lowerScheme + "://" + (userInfo is null ? string.Empty : userInfo + "@") +
                         host.ToLowerInvariant() + port + path;
        address = new GitRepositoryAddress(kind, value, normalized);
        return null;
    }

    /// <summary>
    ///     Validates a scp-like <c>user@host:path</c> address.
    /// </summary>
    /// <param name="value">The trimmed address.</param>
    /// <param name="match">The match of <see cref="ScpPattern"/>.</param>
    /// <param name="address">The address, or <see langword="null"/> on a problem.</param>
    /// <returns>The problem message, or <see langword="null"/>.</returns>
    private static string? ValidateScp(string value, Match match, out GitRepositoryAddress? address)
    {
        address = null;
        var path = match.Groups[3].Value;
        if (path.StartsWith("//", StringComparison.Ordinal) || path.StartsWith('-') ||
            path.Any(char.IsWhiteSpace) || path.Contains('?', StringComparison.Ordinal) ||
            path.Contains('#', StringComparison.Ordinal))
        {
            return "The path in the repository address is not valid. " + FormsMessage;
        }

        var normalized = match.Groups[1].Value + "@" + match.Groups[2].Value.ToLowerInvariant() + ":" + path;
        address = new GitRepositoryAddress(GitAddressKind.Ssh, value, normalized);
        return null;
    }

    /// <summary>
    ///     Splits <c>host[:port]</c> and validates both parts.
    /// </summary>
    /// <param name="text">The host and optional port.</param>
    /// <param name="host">The host.</param>
    /// <param name="port">The port including its leading colon, or empty.</param>
    /// <returns><see langword="true"/> when both parts are valid.</returns>
    private static bool TrySplitHostAndPort(string text, out string host, out string port)
    {
        host = string.Empty;
        port = string.Empty;
        string portText;
        bool hasPortSeparator;
        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']', StringComparison.Ordinal);
            if (close < 0)
            {
                return false;
            }

            host = text[..(close + 1)];
            var tail = text[(close + 1)..];
            if (tail.Length > 0 && !tail.StartsWith(':'))
            {
                return false;
            }

            hasPortSeparator = tail.Length > 0;
            portText = hasPortSeparator ? tail[1..] : string.Empty;
            if (!Ipv6Pattern().IsMatch(host))
            {
                return false;
            }
        }
        else
        {
            var colon = text.IndexOf(':', StringComparison.Ordinal);
            hasPortSeparator = colon >= 0;
            host = hasPortSeparator ? text[..colon] : text;
            portText = hasPortSeparator ? text[(colon + 1)..] : string.Empty;
            if (!HostPattern().IsMatch(host))
            {
                return false;
            }
        }

        // No port at all is fine; a separator with nothing after it is not
        if (!hasPortSeparator)
        {
            return true;
        }

        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
            number is < 1 or > 65535)
        {
            return false;
        }

        port = ":" + number.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>
    ///     Normalizes a local path for the cache key: full path, no trailing separator, and
    ///     upper-cased on Windows where paths are case-insensitive.
    /// </summary>
    /// <param name="path">The fully qualified path.</param>
    /// <returns>The normalized text.</returns>
    private static string NormalizeLocalPath(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
    }
}
