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

using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DemaConsulting.AgentControl.AgentPackageManagement;

/// <summary>
///     A user-configured agent package source: where packages come from and how they are
///     fetched, persisted in the per-user settings file.
/// </summary>
/// <remarks>
///     <para>
///     A definition is identified by its <see cref="Alias"/>, which repos record in their pin so a
///     pinned package is always resolved against the source it came from. Aliases are unique
///     case-insensitively within a settings file and are not editable once a source exists.
///     </para>
///     <para>
///     By design this type has no password, token, key, or any other credential member: sources
///     that need credentials obtain them from the user or from the operating system's own
///     mechanisms at the moment of use and never persist them here. A reflection test guards that
///     property. <see cref="AllowInsecureHttp"/> is enforced for server sources;
///     <see cref="RequireSignedPackages"/> and <see cref="SignerFingerprint"/> are enforced for
///     NuGet sources (folder and server) and ignored by zip sources. Mutable with public
///     getters/setters so
///     <see cref="System.Text.Json.JsonSerializer"/> round-trips it without custom converters. Not
///     thread-safe.
///     </para>
/// </remarks>
internal sealed partial class PackageSourceDefinition
{
    /// <summary>
    ///     The alias given to the single source migrated from the legacy single-folder setting, and
    ///     the alias a pre-source-aware pin file is resolved against.
    /// </summary>
    public const string DefaultAlias = "Default";

    /// <summary>
    ///     Maximum length of an alias, chosen to keep aliases readable in pins and on cards.
    /// </summary>
    private const int MaxAliasLength = 64;

    /// <summary>
    ///     Gets or sets the unique, case-insensitive name of this source, recorded in each repo's
    ///     pin file.
    /// </summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the package format of this source.
    /// </summary>
    public PackageSourceType Type { get; set; }

    /// <summary>
    ///     Gets or sets the source location: a rooted folder or UNC path, or - for NuGet sources
    ///     - an absolute <c>http</c>/<c>https</c> feed URL.
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the package IDs offered from a NuGet source (a NuGet feed cannot be
    ///     enumerated cheaply, so the user names the packages of interest); unused by zip
    ///     sources, which discover their packages from the folder contents.
    /// </summary>
    public List<string> PackageIds { get; set; } = [];

    /// <summary>
    ///     Gets or sets whether a plain <c>http</c> server location is permitted. Defaults to
    ///     <see langword="false"/>, which makes a server location that is not <c>https</c> a
    ///     validation error (see <see cref="GetServerLocationProblem"/>).
    /// </summary>
    public bool AllowInsecureHttp { get; set; }

    /// <summary>
    ///     Gets or sets whether packages from this source must be validly signed. Defaults to
    ///     <see langword="false"/>. Applies to NuGet sources (a folder or a server); a zip
    ///     package carries no signature, so a zip source ignores it. Enforced by
    ///     <see cref="NuGetSourceProvider"/>, which refuses an unsigned or invalidly signed
    ///     package and, where verification is not supported, refuses to acquire packages at all.
    /// </summary>
    public bool RequireSignedPackages { get; set; }

    /// <summary>
    ///     Gets or sets the SHA-256 fingerprint of the signing certificate of the publisher
    ///     (the package author) whose packages are trusted, or <see langword="null"/> for none: any
    ///     package that is validly signed and chains to a trusted root is then accepted, including
    ///     one signed by an unrelated publisher. Not a secret: it is a trust decision owned by the
    ///     user and so lives here rather than in a repo. Only used when
    ///     <see cref="RequireSignedPackages"/> is set; stored as 64 upper-case hexadecimal
    ///     characters (see <see cref="TryNormalizeFingerprint"/>).
    /// </summary>
    public string? SignerFingerprint { get; set; }

    /// <summary>
    ///     Gets whether <see cref="Location"/> is a filesystem folder or a remote server, derived
    ///     from the location text: an absolute <c>http</c>/<c>https</c> URI is a server;
    ///     everything else (rooted, drive-letter, or UNC path) is a folder.
    /// </summary>
    /// <remarks>
    ///     Derived rather than persisted so the location text is the single source of truth.
    ///     Not serialized.
    /// </remarks>
    [JsonIgnore]
    public PackageSourceLocationKind LocationKind =>
        Uri.TryCreate(Location, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? PackageSourceLocationKind.Server
            : PackageSourceLocationKind.Folder;

    /// <summary>
    ///     Checks that <see cref="Location"/> carries no credential, because a location is
    ///     persisted in the settings file and AgentControl never stores secrets.
    /// </summary>
    /// <returns><see langword="null"/> when the location is acceptable; otherwise a message
    ///     (which never repeats the location itself, so the secret cannot leak into an error
    ///     display or log) saying that the location embeds a credential. An absolute URI with
    ///     user information (<c>https://user:password@host/...</c>, <c>https://token@host/...</c>)
    ///     is rejected, as is a server URL carrying a query string or fragment, which is where
    ///     access tokens and signed-URL secrets travel.</returns>
    public string? GetCredentialProblem()
    {
        if (string.IsNullOrWhiteSpace(Location) ||
            !Uri.TryCreate(Location.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var embedsCredential = !string.IsNullOrEmpty(uri.UserInfo) ||
                               (LocationKind == PackageSourceLocationKind.Server &&
                                (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)));
        return embedsCredential
            ? $"The location of source '{Alias}' must not contain a user name, password, token or query string; " +
              "AgentControl never stores credentials."
            : null;
    }

    /// <summary>
    ///     Checks that <see cref="Location"/> is an acceptable NuGet server feed address: an
    ///     absolute <c>https</c> URL (or <c>http</c> only when <see cref="AllowInsecureHttp"/> is
    ///     set) of a V3 service index, carrying no credential.
    /// </summary>
    /// <remarks>
    ///     The V3 service index URL must end in <c>index.json</c> because NuGet treats only URLs
    ///     ending in <c>.json</c> as V3 and uses anything else (including <c>index.json</c> followed
    ///     by a query string) as the legacy V2 protocol, which AgentControl does not rely on.
    ///     Plain <c>http</c> is refused by default because the credential the user types would
    ///     cross the network unprotected. The messages never repeat the location, so a secret
    ///     embedded in it cannot reach an error display or a log.
    /// </remarks>
    /// <returns><see langword="null"/> when the location is acceptable; otherwise a message the
    ///     user can act on.</returns>
    public string? GetServerLocationProblem()
    {
        var location = Location?.Trim() ?? string.Empty;
        if (location.Length == 0)
        {
            return "Enter the address of the NuGet feed, for example https://host/path/index.json.";
        }

        // Anything that is not an absolute web address is not a server feed
        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "Enter a web address that starts with https:// and ends in index.json.";
        }

        // Credentials in the address are refused before anything else, with their own message
        var credentialProblem = GetCredentialProblem();
        if (credentialProblem is not null)
        {
            return credentialProblem;
        }

        if (!uri.AbsolutePath.EndsWith("/index.json", StringComparison.OrdinalIgnoreCase))
        {
            return "The address of a NuGet feed must end in index.json (the V3 service index), " +
                   "for example https://host/path/index.json.";
        }

        return uri.Scheme == Uri.UriSchemeHttp && !AllowInsecureHttp
            ? "Plain http would send your password unprotected. Use an https address, or tick " +
              "\"Allow insecure HTTP\" if you are sure this feed is on a trusted network."
            : null;
    }

    /// <summary>
    ///     The number of hexadecimal characters in a SHA-256 fingerprint.
    /// </summary>
    public const int FingerprintLength = 64;

    /// <summary>
    ///     Normalizes the text of a SHA-256 certificate fingerprint.
    /// </summary>
    /// <param name="text">The text a user typed or pasted: 64 hexadecimal characters, optionally
    ///     separated by colons, spaces or hyphens (as certificate viewers and
    ///     <c>dotnet nuget verify</c> print them), in any letter case.</param>
    /// <param name="normalized">The fingerprint as 64 upper-case hexadecimal characters when the
    ///     method returns <see langword="true"/>; otherwise an empty string.</param>
    /// <returns><see langword="true"/> when the text holds exactly 64 hexadecimal digits once the
    ///     separators are removed.</returns>
    public static bool TryNormalizeFingerprint(string? text, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var digits = new System.Text.StringBuilder(FingerprintLength);
        foreach (var character in text)
        {
            if (character is ':' or '-' || char.IsWhiteSpace(character))
            {
                continue;
            }

            if (!Uri.IsHexDigit(character))
            {
                return false;
            }

            digits.Append(char.ToUpperInvariant(character));
        }

        if (digits.Length != FingerprintLength)
        {
            return false;
        }

        normalized = digits.ToString();
        return true;
    }

    /// <summary>
    ///     Checks the signature settings of a NuGet source: a signer fingerprint, when one is
    ///     given for a source that requires signatures, must be a valid SHA-256 fingerprint.
    /// </summary>
    /// <remarks>
    ///     A fingerprint that cannot be read must stop the source from being used rather than be
    ///     ignored, because ignoring it would silently widen trust from "this publisher" to "any
    ///     signer". The message never repeats the text of the fingerprint.
    /// </remarks>
    /// <returns><see langword="null"/> when the settings are acceptable (including a source that
    ///     does not require signatures, or a zip source); otherwise a message the user can act
    ///     on.</returns>
    public string? GetSignaturePolicyProblem()
    {
        if (Type != PackageSourceType.NuGet || !RequireSignedPackages || string.IsNullOrWhiteSpace(SignerFingerprint))
        {
            return null;
        }

        return TryNormalizeFingerprint(SignerFingerprint, out _)
            ? null
            : $"The trusted signer fingerprint of source '{Alias}' is not a SHA-256 fingerprint " +
              $"({FingerprintLength} hexadecimal characters).";
    }

    /// <summary>
    ///     Gets the pattern of characters an alias may contain: a letter or digit followed by any
    ///     number of letters, digits, spaces, dots, underscores or hyphens (length and trailing
    ///     characters are checked separately by <see cref="IsValidAlias"/>).
    /// </summary>
    /// <returns>The compiled alias pattern.</returns>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 ._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex AliasPattern();

    /// <summary>
    ///     Determines whether <paramref name="alias"/> is acceptable as a source alias.
    /// </summary>
    /// <param name="alias">The candidate alias; may be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the alias is 1-64 characters, starts with a letter or
    ///     digit, contains only letters, digits, spaces, dots, underscores and hyphens, and has no
    ///     trailing space or dot (so it is safe to show, to store in a pin, and to use as part of
    ///     a folder name).</returns>
    public static bool IsValidAlias(string? alias) =>
        !string.IsNullOrEmpty(alias) &&
        alias.Length <= MaxAliasLength &&
        !alias.EndsWith(' ') &&
        !alias.EndsWith('.') &&
        AliasPattern().IsMatch(alias);

    /// <summary>
    ///     Creates the source that a legacy single package-folder setting migrates to.
    /// </summary>
    /// <param name="folder">The legacy package source folder.</param>
    /// <returns>A zip source with alias <see cref="DefaultAlias"/> at <paramref name="folder"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folder"/> is
    ///     <see langword="null"/>.</exception>
    public static PackageSourceDefinition CreateDefaultZip(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return new PackageSourceDefinition { Alias = DefaultAlias, Type = PackageSourceType.Zip, Location = folder };
    }

    /// <summary>
    ///     Replaces a <see langword="null"/> alias, location or package-ID list (which an explicit
    ///     JSON <c>null</c> produces despite the non-nullable declarations) with an empty value and
    ///     drops null package IDs.
    /// </summary>
    /// <remarks>
    ///     Called for each source by <see cref="Settings.AppSettings.NormalizeNulls"/> after
    ///     deserialization. Idempotent.
    /// </remarks>
    internal void NormalizeNulls()
    {
        Alias ??= string.Empty;
        Location ??= string.Empty;
        PackageIds = PackageIds is null ? [] : [.. PackageIds.Where(id => id is not null)];
    }

    /// <summary>
    ///     Creates an independent copy so an editor can change a working copy without mutating
    ///     the live settings.
    /// </summary>
    /// <returns>A deep copy of this definition.</returns>
    public PackageSourceDefinition Clone() => new()
    {
        Alias = Alias,
        Type = Type,
        Location = Location,
        PackageIds = [.. PackageIds],
        AllowInsecureHttp = AllowInsecureHttp,
        RequireSignedPackages = RequireSignedPackages,
        SignerFingerprint = SignerFingerprint
    };

    /// <summary>
    ///     Determines whether another definition would make a source behave identically: same
    ///     alias (case-insensitively), type, location, package IDs and trust options.
    /// </summary>
    /// <param name="other">The definition to compare with.</param>
    /// <returns><see langword="true"/> if nothing that affects what the source offers or how it is
    ///     fetched differs. Used to decide whether cached results for the source are still
    ///     valid after settings change.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="other"/> is
    ///     <see langword="null"/>.</exception>
    public bool IsEquivalentTo(PackageSourceDefinition other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return string.Equals(Alias, other.Alias, StringComparison.OrdinalIgnoreCase) &&
               Type == other.Type &&
               string.Equals(Location, other.Location, StringComparison.Ordinal) &&
               PackageIds.SequenceEqual(other.PackageIds, StringComparer.Ordinal) &&
               AllowInsecureHttp == other.AllowInsecureHttp &&
               RequireSignedPackages == other.RequireSignedPackages &&
               string.Equals(SignerFingerprint, other.SignerFingerprint, StringComparison.Ordinal);
    }
}
