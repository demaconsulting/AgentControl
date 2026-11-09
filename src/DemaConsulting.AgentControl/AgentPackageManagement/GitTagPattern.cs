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

using DemaConsulting.AgentControl.GitIntegration;

namespace DemaConsulting.AgentControl.AgentPackageManagement;

/// <summary>
///     A pattern that turns the tags of a Git repository into package versions: a tag name with
///     exactly one <c>{version}</c> placeholder, for example <c>v{version}</c> or
///     <c>release/{version}</c>.
/// </summary>
/// <remarks>
///     <para>
///     The placeholder is written exactly <c>{version}</c> (case-sensitive) and occurs exactly
///     once. The literal text around it is compared case-sensitively, uses only the characters
///     <c>A-Z a-z 0-9 . _ / + -</c> (the characters AgentControl accepts in a tag name that a
///     server reports, see <see cref="IsSafeTagName"/>), and must follow git's ref-name rules (no
///     empty, dot-leading or <c>.lock</c>-ending path component, no <c>..</c>, no leading
///     <c>-</c>) so that a tag formatted from a version is a legal tag name. A tag
///     matches when it is the prefix, a strict Semantic Versioning 2.0.0 version
///     (<see cref="IsStrictSemanticVersion"/>: exactly three numeric parts without leading zeros and
///     an optional prerelease whose numeric identifiers have no leading zeros, no <c>+metadata</c>)
///     and the suffix; every other tag is simply not a package version. A version whose formatted
///     tag would not be a safe tag name that the pattern reads back as the same version (for
///     example one with a very long prerelease) has no tag: <see cref="Format"/> returns
///     <see langword="null"/>.
///     </para>
///     <para>
///     Pure and immutable; thread-safe. Messages never repeat the pattern text.
///     </para>
/// </remarks>
internal sealed class GitTagPattern
{
    /// <summary>
    ///     The placeholder that stands for the version.
    /// </summary>
    public const string Placeholder = "{version}";

    /// <summary>
    ///     The longest pattern accepted. The tag built from a pattern and a version may be longer,
    ///     up to the 255-character limit of <see cref="IsSafeTagName"/>; <see cref="Format"/>
    ///     refuses a version whose tag would exceed it.
    /// </summary>
    private const int MaxLength = 200;

    /// <summary>
    ///     The text before the placeholder.
    /// </summary>
    private readonly string _prefix;

    /// <summary>
    ///     The text after the placeholder.
    /// </summary>
    private readonly string _suffix;

    /// <summary>
    ///     Initializes a validated pattern.
    /// </summary>
    /// <param name="text">The pattern text.</param>
    /// <param name="prefix">The text before the placeholder.</param>
    /// <param name="suffix">The text after the placeholder.</param>
    private GitTagPattern(string text, string prefix, string suffix)
    {
        Text = text;
        _prefix = prefix;
        _suffix = suffix;
    }

    /// <summary>
    ///     Gets the pattern text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    ///     Gets the glob handed to <c>git ls-remote</c> to list the candidate tags:
    ///     <c>refs/tags/</c>, the literal text before the placeholder, and <c>*</c>. It only
    ///     narrows the listing; matching is done by <see cref="TryMatch"/>.
    /// </summary>
    public string ListingGlob => "refs/tags/" + _prefix + "*";

    /// <summary>
    ///     Validates a pattern.
    /// </summary>
    /// <param name="text">The pattern text; may be <see langword="null"/>.</param>
    /// <param name="pattern">The pattern when the method returns <see langword="true"/>;
    ///     otherwise <see langword="null"/>.</param>
    /// <param name="problem">A message the user can act on when the method returns
    ///     <see langword="false"/> (it never repeats <paramref name="text"/>); otherwise
    ///     <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the pattern is acceptable.</returns>
    public static bool TryCreate(string? text, out GitTagPattern? pattern, out string? problem)
    {
        pattern = null;
        problem = null;
        if (string.IsNullOrEmpty(text))
        {
            problem = "Enter a tag pattern that contains " + Placeholder + ", for example v" + Placeholder + ".";
            return false;
        }

        if (text.Length > MaxLength)
        {
            problem = "The tag pattern is too long.";
            return false;
        }

        var first = text.IndexOf(Placeholder, StringComparison.Ordinal);
        if (first < 0 || text.IndexOf(Placeholder, first + Placeholder.Length, StringComparison.Ordinal) >= 0)
        {
            problem = "The tag pattern must contain " + Placeholder + " exactly once (written in lower case), " +
                      "for example v" + Placeholder + " or release/" + Placeholder + ".";
            return false;
        }

        var prefix = text[..first];
        var suffix = text[(first + Placeholder.Length)..];
        if (prefix.Contains('{', StringComparison.Ordinal) || prefix.Contains('}', StringComparison.Ordinal) ||
            suffix.Contains('{', StringComparison.Ordinal) || suffix.Contains('}', StringComparison.Ordinal))
        {
            problem = "The tag pattern must not contain braces other than the " + Placeholder + " placeholder.";
            return false;
        }

        // A sample tag built from the pattern must be a legal tag name, which checks the literal
        // text and how it joins the version together (a prefix ending in '.' for example)
        if (!IsSafeTagName(prefix + "1.0.0" + suffix))
        {
            problem = "The tag pattern may contain only letters, digits and . _ / + - and must be a valid " +
                      "Git tag name (no '..', no component starting with '.' or ending in '.lock', not starting with '-').";
            return false;
        }

        pattern = new GitTagPattern(text, prefix, suffix);
        return true;
    }

    /// <summary>
    ///     Gets the problem with a pattern, if any.
    /// </summary>
    /// <param name="text">The pattern text; may be <see langword="null"/>.</param>
    /// <returns><see langword="null"/> when the pattern is acceptable; otherwise a message.</returns>
    public static string? GetProblem(string? text) => TryCreate(text, out _, out var problem) ? null : problem;

    /// <summary>
    ///     Determines whether a tag name is one AgentControl is willing to use in a git command:
    ///     only the characters <c>A-Z a-z 0-9 . _ / + -</c>, at most 255 characters, and a legal
    ///     git ref name (no empty path component, none starting with <c>.</c> or ending in
    ///     <c>.lock</c>, no <c>..</c>, not ending in <c>.</c>, not starting with <c>-</c>). The rule
    ///     itself lives in <see cref="GitRemoteClient.IsSafeTagName"/>, so the client applies
    ///     exactly the same check before a name reaches a command line.
    /// </summary>
    /// <param name="tagName">The tag name without the <c>refs/tags/</c> prefix, as a server
    ///     reported it or as formatted from a pattern.</param>
    /// <returns><see langword="true"/> when the name is acceptable. A server is untrusted, so a
    ///     name that fails is never put on a command line.</returns>
    public static bool IsSafeTagName(string? tagName) => GitRemoteClient.IsSafeTagName(tagName);

    /// <summary>
    ///     Determines whether a tag is a package version under this pattern.
    /// </summary>
    /// <param name="tagName">The tag name without the <c>refs/tags/</c> prefix.</param>
    /// <param name="version">The version the tag stands for when the method returns
    ///     <see langword="true"/>; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the name is acceptable (see
    ///     <see cref="IsSafeTagName"/>), starts with the pattern's prefix, ends with its suffix
    ///     (both compared case-sensitively) and what lies between is a strict semantic
    ///     version.</returns>
    public bool TryMatch(string? tagName, out PackageVersion? version)
    {
        version = null;
        if (!IsSafeTagName(tagName) ||
            tagName!.Length <= _prefix.Length + _suffix.Length ||
            !tagName.StartsWith(_prefix, StringComparison.Ordinal) ||
            !tagName.EndsWith(_suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var middle = tagName[_prefix.Length..(tagName.Length - _suffix.Length)];
        return IsStrictSemanticVersion(middle) && PackageVersion.TryParseStrict(middle, out version) && version is not null;
    }

    /// <summary>
    ///     Determines whether a text is a Semantic Versioning 2.0.0 version without build metadata:
    ///     exactly three numeric parts without leading zeros, then an optional <c>-</c> prerelease of
    ///     dot-separated, non-empty identifiers of letters, digits and hyphens in which a numeric
    ///     identifier has no leading zero. Stricter than <see cref="PackageVersion.TryParseStrict"/>,
    ///     which also serves zip file names and so must keep accepting leading zeros.
    /// </summary>
    /// <param name="text">The text between the pattern's prefix and suffix.</param>
    /// <returns><see langword="true"/> when the text is a strict semantic version.</returns>
    internal static bool IsStrictSemanticVersion(string text)
    {
        var dash = text.IndexOf('-', StringComparison.Ordinal);
        var core = dash < 0 ? text : text[..dash];
        var parts = core.Split('.');
        if (parts.Length != 3 || parts.Any(part => !IsNumericIdentifier(part)))
        {
            return false;
        }

        // No '+': build metadata is not part of a tag's version, and a '+' cannot be in an identifier
        if (dash < 0)
        {
            return true;
        }

        var prerelease = text[(dash + 1)..];
        return prerelease.Length > 0 && prerelease.Split('.').All(identifier =>
            identifier.Length > 0 &&
            identifier.All(character => char.IsAsciiLetterOrDigit(character) || character == '-') &&
            (!identifier.All(char.IsAsciiDigit) || IsNumericIdentifier(identifier)));
    }

    /// <summary>
    ///     Determines whether a text is a numeric identifier without a leading zero (<c>0</c> itself
    ///     is allowed).
    /// </summary>
    /// <param name="text">The identifier.</param>
    /// <returns><see langword="true"/> for digits only, not starting with <c>0</c> unless it is
    ///     exactly <c>0</c>.</returns>
    private static bool IsNumericIdentifier(string text) =>
        text.Length > 0 && text.All(char.IsAsciiDigit) && (text.Length == 1 || text[0] != '0');

    /// <summary>
    ///     Builds the tag name that stands for a version.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <returns>The prefix, the version text and the suffix, when that is a safe tag name (see
    ///     <see cref="IsSafeTagName"/>) that <see cref="TryMatch"/> maps back to the same version;
    ///     otherwise <see langword="null"/> (a prerelease so long that the tag would exceed the
    ///     tag-name limit, a version with build metadata or a fourth part, or one with a leading
    ///     zero).</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="version"/> is
    ///     <see langword="null"/>.</exception>
    public string? Format(PackageVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var tag = _prefix + version.ToString() + _suffix;

        // A tag that is not a legal name (too long, for example), or that this pattern would not
        // read back as this version, is never offered: it could not be listed or fetched anyway
        return IsSafeTagName(tag) && TryMatch(tag, out var parsed) && parsed == version ? tag : null;
    }
}
