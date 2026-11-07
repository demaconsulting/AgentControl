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

using DemaConsulting.AgentControl.RepoSync;

namespace DemaConsulting.AgentControl.AgentPackageManagement;

/// <summary>
///     <see cref="IPackageSourceProvider"/> over a folder of <c>{name}-{semver}.zip</c> package
///     files: an adapter that exposes the existing <see cref="PackageSource"/> scanning logic
///     through the provider contract without changing its behavior.
/// </summary>
/// <remarks>
///     Discovery rules (file-name splitting, strict three-part versions, skipping unrelated files)
///     are exactly those of <see cref="PackageSource"/>; this class only adds the source alias,
///     asynchronous execution (folder scans run on a worker thread so a slow network share never
///     blocks the caller) and a check that content is only ever acquired from this source's own
///     folder. No authentication, checksum or signature verification is performed - that trust
///     boundary belongs to whoever maintains the shared folder. Stateless and thread-safe.
/// </remarks>
internal sealed class ZipFolderSourceProvider : IPackageSourceProvider
{
    /// <summary>
    ///     The folder scanned for package zips.
    /// </summary>
    private readonly string _folder;

    /// <summary>
    ///     Initializes a provider for one zip-package-folder source.
    /// </summary>
    /// <param name="definition">The source definition; its <see cref="PackageSourceDefinition.Type"/>
    ///     must be <see cref="PackageSourceType.Zip"/> and its location must not be blank.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="definition"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the definition is not a zip source or has a
    ///     blank alias or location.</exception>
    public ZipFolderSourceProvider(PackageSourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Alias);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Location);
        if (definition.Type != PackageSourceType.Zip)
        {
            throw new ArgumentException(
                $"Source '{definition.Alias}' is not a zip-package folder source.", nameof(definition));
        }

        Alias = definition.Alias;
        _folder = definition.Location;
    }

    /// <inheritdoc />
    public string Alias { get; }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken) =>
        Task.Run(() => PackageSource.EnumeratePackageNames(_folder), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(
        string packageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        // Stamp the alias so every discovered package carries the identity of the source it was
        // found at; the static scan itself is alias-agnostic
        return Task.Run<IReadOnlyList<DiscoveredPackage>>(
            () => PackageSource.EnumeratePackages(_folder, packageId)
                .Select(package => package with { SourceAlias = Alias })
                .ToList(),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IPackageContentReader> AcquireAsync(DiscoveredPackage package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);

        // Refuse a package from another source, or one whose zip is not in this source's folder:
        // acquisition must never be redirected to content this source did not report
        if (!string.Equals(package.SourceAlias, Alias, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Package '{package.PackageName}' was discovered at source '{package.SourceAlias}', not at '{Alias}'.",
                nameof(package));
        }

        var zipFolder = Path.GetDirectoryName(Path.GetFullPath(package.Location));
        if (!string.Equals(
                Path.TrimEndingDirectorySeparator(zipFolder ?? string.Empty),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(_folder)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Package '{package.PackageName}' is not located in the folder of source '{Alias}'.",
                nameof(package));
        }

        return Task.Run<IPackageContentReader>(() => ZipPackageContentReader.Open(package.Location), cancellationToken);
    }
}
