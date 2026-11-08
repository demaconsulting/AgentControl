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
///     blocks the caller, and the caller's cancellation is honored promptly even though a scan
///     that has started cannot itself be interrupted: the caller is released at once and the
///     abandoned scan finishes in the background, any zip it opened being disposed), a uniform
///     error contract (a missing or unreachable folder is a <see cref="DirectoryNotFoundException"/>,
///     any other failure to read it an <see cref="InvalidOperationException"/>) and checks that
///     content is only ever acquired from this source's own folder - a zip that is a link to a
///     file elsewhere is refused. The link check happens immediately before the zip is opened; a
///     concurrent replacement of the file by another process of the same user in that instant is
///     a documented residual risk (see the RepoSync design). No authentication, checksum or
///     signature verification is performed - that trust boundary belongs to whoever maintains
///     the shared folder. Stateless and thread-safe.
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
        RunCancellableAsync<IReadOnlyList<string>>(
            () => ReadFolder(() => PackageSource.EnumeratePackageNames(_folder)), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(
        string packageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        // Stamp the alias so every discovered package carries the identity of the source it was
        // found at; the static scan itself is alias-agnostic
        return RunCancellableAsync<IReadOnlyList<DiscoveredPackage>>(
            () => ReadFolder(
                () => PackageSource.EnumeratePackages(_folder, packageId)
                    .Select(package => package with { SourceAlias = Alias })
                    .ToList()),
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

        if (!IsInOwnFolder(package.Location))
        {
            throw new ArgumentException(
                $"Package '{package.PackageName}' is not located in the folder of source '{Alias}'.",
                nameof(package));
        }

        // A reader that finishes after the caller gave up is nobody's to use: dispose it
        return RunCancellableAsync<IPackageContentReader>(
            () => OpenZip(package), cancellationToken, reader => reader.Dispose());
    }

    /// <summary>
    ///     Runs blocking work on a worker thread so that the caller is released as soon as its
    ///     token is canceled, even though work that has started cannot be interrupted.
    /// </summary>
    /// <remarks>
    ///     A token that is already canceled stops the call before any work starts. When the token
    ///     is canceled while the work runs, the work is left to finish in the background; whatever
    ///     it then produces is handed to <paramref name="disposeLateResult"/> so that nothing it
    ///     opened is leaked, and its failure (if any) is observed and dropped.
    /// </remarks>
    /// <typeparam name="T">The result type of the work.</typeparam>
    /// <param name="work">The blocking work.</param>
    /// <param name="cancellationToken">Token to abandon the call.</param>
    /// <param name="disposeLateResult">Releases a result that completes after the caller gave up,
    ///     or <see langword="null"/> when a result needs no release.</param>
    /// <returns>The result of the work.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     is canceled before or while the work runs.</exception>
    internal static async Task<T> RunCancellableAsync<T>(
        Func<T> work, CancellationToken cancellationToken, Action<T>? disposeLateResult = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The work itself is not given the token (it cannot observe it); the wait is
        var running = Task.Run(work, CancellationToken.None);
        try
        {
            return await running.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Abandoned: release a late result, and observe a late failure so it is not unobserved
            _ = running.ContinueWith(
                finished =>
                {
                    if (finished.IsCompletedSuccessfully)
                    {
                        disposeLateResult?.Invoke(finished.Result);
                    }
                    else
                    {
                        _ = finished.Exception;
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
    }

    /// <summary>
    ///     Runs a read of the source's folder and normalizes its failures to the provider's
    ///     contract.
    /// </summary>
    /// <typeparam name="T">The result type of the read.</typeparam>
    /// <param name="read">The read.</param>
    /// <returns>The result of the read.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when the folder does not exist or is
    ///     unreachable - also when the platform reported that as another I/O error.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the folder exists but cannot be
    ///     read (for example for lack of permission); the message is safe to show.</exception>
    private T ReadFolder<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (DirectoryNotFoundException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or System.Security.SecurityException or ArgumentException)
        {
            throw Normalize(ex);
        }
    }

    /// <summary>
    ///     Opens the zip of a package, refusing a zip that is a link to a file outside this
    ///     source's folder.
    /// </summary>
    /// <param name="package">The package; its location is already known to be directly in the
    ///     source's folder.</param>
    /// <returns>The reader; the caller disposes it.</returns>
    /// <exception cref="ArgumentException">Thrown when the zip is a link to a file elsewhere.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the source's folder no longer
    ///     exists.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the zip cannot be opened.</exception>
    private ZipPackageContentReader OpenZip(DiscoveredPackage package)
    {
        RejectLinkOutsideFolder(package);
        try
        {
            return ZipPackageContentReader.Open(package.Location);
        }
        catch (InvalidOperationException ex) when (!FolderExists())
        {
            // The reader wraps "folder vanished" like any other open failure; the provider's
            // contract is that a missing folder is reported as such
            throw Normalize(ex);
        }
    }

    /// <summary>
    ///     Refuses a package zip that is a symbolic link, or other link, whose final target is not a
    ///     file in the source's own folder - the lexical folder check alone would accept it.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <exception cref="ArgumentException">Thrown when the zip links to a file outside the
    ///     folder.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the link cannot be resolved (for
    ///     example a cycle).</exception>
    private void RejectLinkOutsideFolder(DiscoveredPackage package)
    {
        var file = new FileInfo(package.Location);

        // Not a link (or not there at all): nothing to resolve; a missing file fails when opened
        if (file.LinkTarget is null)
        {
            return;
        }

        FileSystemInfo? target;
        try
        {
            target = file.ResolveLinkTarget(returnFinalTarget: true);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"The package file for '{package.PackageName}' in source '{Alias}' is a link that cannot be followed: {ex.Message}",
                ex);
        }

        if (target is null || !IsInOwnFolder(target.FullName))
        {
            throw new ArgumentException(
                $"Package '{package.PackageName}' in source '{Alias}' is a link to a file outside the folder of the source.",
                nameof(package));
        }
    }

    /// <summary>
    ///     Tells whether a file lies directly in this source's folder, comparing normalized paths.
    /// </summary>
    /// <param name="location">The file path.</param>
    /// <returns><see langword="true"/> when the file's folder is the source's folder.</returns>
    private bool IsInOwnFolder(string location)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(location));
        return string.Equals(
            Path.TrimEndingDirectorySeparator(folder ?? string.Empty),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(_folder)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>
    ///     Turns a failure to read the folder into the provider's contract exception.
    /// </summary>
    /// <param name="cause">The failure.</param>
    /// <returns>A <see cref="DirectoryNotFoundException"/> when the folder is not there (or not
    ///     reachable), otherwise an <see cref="InvalidOperationException"/>; both keep the cause.</returns>
    private Exception Normalize(Exception cause) =>
        FolderExists()
            ? new InvalidOperationException(
                $"The folder of source '{Alias}' could not be read: {cause.Message}", cause)
            : new DirectoryNotFoundException(
                $"The package folder for source '{Alias}' was not found or is not reachable.", cause);

    /// <summary>
    ///     Tells whether the source's folder exists, without ever throwing.
    /// </summary>
    /// <returns><see langword="true"/> when the folder exists and is reachable.</returns>
    private bool FolderExists()
    {
        try
        {
            return Directory.Exists(_folder);
        }
#pragma warning disable CA1031 // Whatever goes wrong, the folder cannot be relied on
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }
}
