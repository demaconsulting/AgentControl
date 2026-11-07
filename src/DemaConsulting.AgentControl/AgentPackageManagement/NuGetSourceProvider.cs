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

using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using DemaConsulting.AgentControl.Logging;
using DemaConsulting.AgentControl.RepoSync;
using DemaConsulting.NuGet.Caching;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.AgentPackageManagement;

/// <summary>
///     <see cref="IPackageSourceProvider"/> over NuGet packages held in a local or network folder
///     feed, using the DemaConsulting.NuGet.Caching library to list versions and to unpack the
///     chosen package into a private package cache.
/// </summary>
/// <remarks>
///     <para>
///     A NuGet package carries its agent content under its <c>content</c> folder
///     (<c>content/.github/...</c>, <c>content/release-notes.md</c>, <c>content/AGENTS.md</c>),
///     which <see cref="DirectoryPackageContentReader"/> exposes exactly as a zip's root. Only
///     folder and UNC locations are accepted in this release; a server (<c>http</c>/<c>https</c>)
///     location is rejected with a clear error because credential handling and transport
///     safeguards for servers are separate work.
///     </para>
///     <para>
///     <b>Isolation.</b> Every call passes an explicit AgentControl-owned root folder (one per
///     source, under the per-user local application data folder unless a base folder is supplied)
///     to the NuGet library and never the target repository or the current directory, so no
///     <c>nuget.config</c> belonging to a repo is discovered and nothing about AgentControl's
///     sources is ever written to a repo. That per-source root holds a generated
///     <c>nuget.config</c> that only redirects NuGet's global packages folder to a
///     <c>packages</c> sub-folder of the root, so each source has a private package cache: a
///     package that another source (or the developer's own NuGet cache) holds under the same
///     ID and version can never be served as this source's package. Because the
///     <c>NUGET_PACKAGES</c> environment variable overrides that setting, acquisition fails
///     closed (with an explanatory message) rather than accept a package from a cache outside the
///     per-source root. The unpacked package is only ever read, never modified.
///     </para>
///     <para>
///     This provider never registers a NuGet credential service and carries no credentials. The
///     <see cref="PackageSourceDefinition.RequireSignedPackages"/>,
///     <see cref="PackageSourceDefinition.SignerFingerprint"/> and
///     <see cref="PackageSourceDefinition.AllowInsecureHttp"/> settings are not enforced yet (they
///     apply to server sources and signing support). Thread-safe: shared state is limited to a
///     one-time, locked preparation of the per-source root.
///     </para>
/// </remarks>
internal sealed class NuGetSourceProvider : IPackageSourceProvider
{
    /// <summary>
    ///     File name of the generated per-source NuGet configuration, in the casing every
    ///     supported platform's NuGet configuration discovery finds.
    /// </summary>
    private const string NuGetConfigFileName = "nuget.config";

    /// <summary>
    ///     Name of the per-source sub-folder used as NuGet's global packages folder.
    /// </summary>
    private const string PackagesFolderName = "packages";

    /// <summary>
    ///     Number of hexadecimal characters of the location hash used to disambiguate per-source
    ///     root folder names.
    /// </summary>
    private const int KeyHashLength = 12;

    /// <summary>
    ///     The package IDs the user configured for this source, trimmed and de-duplicated.
    /// </summary>
    private readonly IReadOnlyList<string> _packageIds;

    /// <summary>
    ///     The folder feed location.
    /// </summary>
    private readonly string _location;

    /// <summary>
    ///     The AgentControl-owned folder that NuGet configuration discovery starts from and that
    ///     holds this source's private package cache.
    /// </summary>
    private readonly string _root;

    /// <summary>
    ///     Serializes the one-time preparation of <see cref="_root"/>.
    /// </summary>
    private readonly object _prepareLock = new();

    /// <summary>
    ///     Whether <see cref="_root"/> and its generated configuration have been prepared.
    /// </summary>
    private bool _prepared;

    /// <summary>
    ///     Initializes a provider for one NuGet-package-folder source.
    /// </summary>
    /// <param name="definition">The source definition; its
    ///     <see cref="PackageSourceDefinition.Type"/> must be <see cref="PackageSourceType.NuGet"/>
    ///     and its location a rooted folder or UNC path.</param>
    /// <param name="rootBase">The absolute folder under which per-source roots are created, or
    ///     <see langword="null"/> for <c>{LocalApplicationData}/AgentControl/nuget</c>. Never the
    ///     target repository, and never relative to the current directory.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="definition"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the definition is not a NuGet source, has
    ///     a blank alias or location, or <paramref name="rootBase"/> is not an absolute
    ///     path.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the location is a server URL
    ///     (not yet supported) or is not an absolute folder path.</exception>
    public NuGetSourceProvider(PackageSourceDefinition definition, string? rootBase = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Alias);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Location);
        if (definition.Type != PackageSourceType.NuGet)
        {
            throw new ArgumentException(
                $"Source '{definition.Alias}' is not a NuGet source.", nameof(definition));
        }

        // Server feeds need credential handling and transport safeguards that are not part of
        // this release, so refuse them clearly rather than half-supporting them
        if (definition.LocationKind == PackageSourceLocationKind.Server)
        {
            throw new InvalidOperationException(
                $"NuGet server sources are not supported yet; source '{definition.Alias}' must use a folder or network share.");
        }

        if (!Path.IsPathFullyQualified(definition.Location))
        {
            throw new InvalidOperationException(
                $"The location of NuGet source '{definition.Alias}' must be an absolute folder path.");
        }

        Alias = definition.Alias;
        _location = definition.Location;
        _packageIds = definition.PackageIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        // The cache root must never depend on the process current directory (which could be a
        // target repo, whose NuGet configuration is not ours to read), so a relative or
        // drive-relative base is refused rather than resolved
        var baseFolder = rootBase ?? GetDefaultRootBase();
        if (!Path.IsPathFullyQualified(baseFolder))
        {
            throw new ArgumentException(
                $"The NuGet cache root base '{baseFolder}' must be an absolute path.", nameof(rootBase));
        }

        _root = Path.Combine(baseFolder, BuildSourceKey(definition.Alias, definition.Location));
    }

    /// <inheritdoc />
    public string Alias { get; }

    /// <summary>
    ///     Gets the folder under which per-source NuGet roots are created by default:
    ///     <c>{LocalApplicationData}/AgentControl/nuget</c>.
    /// </summary>
    /// <returns>The default base folder. A cache belongs in local (non-roaming) application data;
    ///     the folder is not created here.</returns>
    public static string GetDefaultRootBase() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentControl", "nuget");

    /// <summary>
    ///     Gets the AgentControl-owned root folder this provider passes to the NuGet library.
    /// </summary>
    /// <remarks>
    ///     Exposed so tests and diagnostics can verify that the root is never the target repo;
    ///     the folder is created on first use, not at construction.
    /// </remarks>
    internal string RootFolder => _root;

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // A NuGet feed cannot be enumerated cheaply, so the package IDs offered are the ones the
        // user configured for the source
        return Task.FromResult<IReadOnlyList<string>>(
            _packageIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(
        string packageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        EnsureLocationExists();
        PrepareRoot();

        // Ask the library for every listed version (prereleases included, as pins may be
        // prereleases); the folder is named explicitly in Sources, so it is always queried
        var versions = await NuGetCache.ListVersionsAsync(
            packageId, includePrerelease: true, _root, CreateOptions(), cancellationToken);

        // Skip versions our version type cannot represent rather than failing the whole list;
        // the library reports normalized strings, so this only drops exotic ones
        var results = new List<DiscoveredPackage>();
        foreach (var version in versions.Where(v => v.IsListed))
        {
            if (PackageVersion.TryParse(version.Version, out var parsed) && parsed is not null)
            {
                results.Add(new DiscoveredPackage(packageId, parsed, parsed.Original, Alias));
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IPackageContentReader> AcquireAsync(DiscoveredPackage package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!string.Equals(package.SourceAlias, Alias, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Package '{package.PackageName}' was discovered at source '{package.SourceAlias}', not at '{Alias}'.",
                nameof(package));
        }

        EnsureLocationExists();
        PrepareRoot();

        // The library unpacks the package into the global packages folder configured in the
        // generated per-source nuget.config and returns that package's folder
        var packageFolder = await NuGetCache.EnsureCachedAsync(
            package.PackageName, package.Version.Original, _root, CreateOptions(), cancellationToken);

        // Fail closed if the package did not land inside this source's private cache (the
        // NUGET_PACKAGES environment variable overrides the generated configuration): a shared
        // cache could hold a same-ID, same-version package that came from a different source
        var packagesFolder = Path.GetFullPath(Path.Combine(_root, PackagesFolderName));
        var relative = Path.GetRelativePath(packagesFolder, Path.GetFullPath(packageFolder));
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException(
                $"Package '{package.PackageName}' from source '{Alias}' was unpacked outside AgentControl's " +
                "private package cache, so it cannot be trusted to come from this source. " +
                "Remove the NUGET_PACKAGES environment variable and try again.");
        }

        return DirectoryPackageContentReader.Open(packageFolder);
    }

    /// <summary>
    ///     Builds the name of this source's private root folder from its alias and location, so
    ///     editing a source's location starts a fresh cache and two sources never share one.
    /// </summary>
    /// <param name="alias">The source alias.</param>
    /// <param name="location">The source location.</param>
    /// <returns>A folder-name-safe key of the form <c>{alias}-{hash}</c>.</returns>
    internal static string BuildSourceKey(string alias, string location)
    {
        // Keep the alias recognizable in the folder name but make it filesystem-safe
        var safeAlias = new string(alias.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());

        // The hash covers the case-folded alias and a normalized location; case-folding matches
        // the case-insensitive alias uniqueness rule
        var normalizedLocation = location.Trim().TrimEnd('\\', '/').ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(alias.ToUpperInvariant() + "\n" + normalizedLocation));
        return $"{safeAlias}-{Convert.ToHexString(hash)[..KeyHashLength].ToLowerInvariant()}";
    }

    /// <summary>
    ///     Creates the per-call NuGet library options: only this source, non-interactive, with no
    ///     credential callback, so a call can neither prompt nor consult any other source.
    /// </summary>
    /// <returns>The options for one library call.</returns>
    private NuGetCacheOptions CreateOptions() => new()
    {
        Sources = [_location],
        Interactive = false,
        Logger = AppLogging.Factory.CreateLogger<NuGetSourceProvider>()
    };

    /// <summary>
    ///     Verifies the feed folder exists so a missing or offline folder is reported as such, in
    ///     the same way a missing zip folder is, instead of as an opaque NuGet failure.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">Thrown when the folder does not exist or is
    ///     unreachable.</exception>
    private void EnsureLocationExists()
    {
        if (!Directory.Exists(_location))
        {
            throw new DirectoryNotFoundException($"The NuGet package folder for source '{Alias}' was not found.");
        }
    }

    /// <summary>
    ///     Creates this source's private root folder and its generated <c>nuget.config</c> (which
    ///     only redirects the global packages folder into the root) the first time it is needed.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the root cannot be created or the
    ///     configuration cannot be written.</exception>
    private void PrepareRoot()
    {
        lock (_prepareLock)
        {
            if (_prepared)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(_root);
                WriteConfigIfChanged(Path.Combine(_root, NuGetConfigFileName));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    $"Failed to prepare the package cache for source '{Alias}': {ex.Message}", ex);
            }

            _prepared = true;
        }
    }

    /// <summary>
    ///     Writes the generated per-source NuGet configuration unless the file already holds the
    ///     same content, replacing it atomically so concurrent processes never see a partial file.
    /// </summary>
    /// <param name="configPath">Path of the <c>nuget.config</c> to write.</param>
    private void WriteConfigIfChanged(string configPath)
    {
        // Only the global packages folder is set: no sources, credentials or other trust data
        var packagesFolder = Path.GetFullPath(Path.Combine(_root, PackagesFolderName));
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                "configuration",
                new XElement(
                    "config",
                    new XElement(
                        "add",
                        new XAttribute("key", "globalPackagesFolder"),
                        new XAttribute("value", packagesFolder)))));
        var content = document.Declaration + Environment.NewLine + document.Root;

        if (File.Exists(configPath) && File.ReadAllText(configPath) == content)
        {
            return;
        }

        var temporaryPath = configPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaryPath, content);
        File.Move(temporaryPath, configPath, overwrite: true);
    }
}
