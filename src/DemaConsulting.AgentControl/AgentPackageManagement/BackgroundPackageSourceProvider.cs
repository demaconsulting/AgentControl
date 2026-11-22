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
///     Runs every call of another provider on a worker thread, so a caller on the UI thread never
///     waits for the disk, a process or a package hash, and hands back a package whose content
///     identity is already computed.
/// </summary>
/// <remarks>
///     <para>
///     The built-in providers do real work before their first true wait: a Git source creates its
///     cache folders and starts git, a NuGet source loads its configuration and checks its cache
///     folders, a zip folder opens the zip. A caller on the UI thread would run that work itself,
///     and an acquired package's <see cref="IPackageContentReader.ContentIdentity"/> (a decompress
///     and SHA-256 of every file) would be computed on the first read, again on that thread.
///     </para>
///     <para>
///     This decorator starts each call on the thread pool, where there is no synchronization
///     context, so the provider's own work and its continuations (and any library it drives) stay
///     off the caller's thread. <see cref="AcquireAsync"/> reads the content identity on the worker
///     before it returns the reader; readers remember it, so later reads cost nothing. The answers,
///     the exceptions and the cancellation are those of the decorated provider, unchanged.
///     </para>
///     <para>
///     Only <see cref="PackageSourceRegistry.Create"/> uses it, for the built-in providers. The
///     source editor creates its providers itself (it needs the Git provider's tag preview) and
///     runs their calls on a worker of its own. Safe to call from any thread.
///     </para>
/// </remarks>
internal sealed class BackgroundPackageSourceProvider : IPackageSourceProvider
{
    /// <summary>
    ///     Initializes a decorator over a provider.
    /// </summary>
    /// <param name="inner">The provider whose calls run on a worker thread.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is
    ///     <see langword="null"/>.</exception>
    public BackgroundPackageSourceProvider(IPackageSourceProvider inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        Inner = inner;
    }

    /// <summary>
    ///     Gets the decorated provider.
    /// </summary>
    internal IPackageSourceProvider Inner { get; }

    /// <inheritdoc />
    public string Alias => Inner.Alias;

    /// <inheritdoc />
    /// <remarks>Runs the decorated provider's call on a worker thread.</remarks>
    public Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Inner.ListPackageIdsAsync(cancellationToken), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Runs the decorated provider's call on a worker thread.</remarks>
    public Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(string packageId, CancellationToken cancellationToken) =>
        Task.Run(() => Inner.ListVersionsAsync(packageId, cancellationToken), cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    ///     Runs the decorated provider's call on a worker thread and reads the package's content
    ///     identity there before returning, so the caller gets a reader whose identity is already
    ///     known. A reader whose identity cannot be read is disposed and the failure is thrown.
    /// </remarks>
    public Task<IPackageContentReader> AcquireAsync(DiscoveredPackage package, CancellationToken cancellationToken) =>
        Task.Run(
            async () =>
            {
                var reader = await Inner.AcquireAsync(package, cancellationToken).ConfigureAwait(false);
                try
                {
                    _ = reader.ContentIdentity;
                    return reader;
                }
                catch
                {
                    reader.Dispose();
                    throw;
                }
            },
            cancellationToken);
}
