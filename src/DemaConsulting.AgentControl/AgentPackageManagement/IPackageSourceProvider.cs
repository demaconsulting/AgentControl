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
///     The contract every kind of agent package source implements: report what packages and
///     versions the source offers, and hand over the content of one chosen version.
/// </summary>
/// <remarks>
///     <para>
///     One provider instance serves exactly one configured source (<see cref="Alias"/>) and never
///     consults any other. All members are asynchronous and cancellable because real sources can
///     be slow or unreachable (a network share today, a remote server later); callers must be able
///     to abandon a call. Providers never write to the repo, and never persist credentials.
///     </para>
///     <para>
///     The interface is deliberately minimal so new source kinds (for example a git-tag source)
///     only need these three operations. Implementations are safe to call from any thread unless
///     they document otherwise.
///     </para>
/// </remarks>
internal interface IPackageSourceProvider
{
    /// <summary>
    ///     Gets the alias of the configured source this provider serves.
    /// </summary>
    string Alias { get; }

    /// <summary>
    ///     Lists the package names or IDs this source offers.
    /// </summary>
    /// <param name="cancellationToken">Token to abandon the call.</param>
    /// <returns>The distinct package names, ordered case-insensitively for stable display; empty
    ///     when the source offers none.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when a folder source does not exist or
    ///     is unreachable.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     is canceled.</exception>
    Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Lists the versions of one package this source offers.
    /// </summary>
    /// <param name="packageId">The package name or ID.</param>
    /// <param name="cancellationToken">Token to abandon the call.</param>
    /// <returns>The discovered versions in no guaranteed order, each stamped with this provider's
    ///     <see cref="Alias"/>; empty when the source has no such package.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="packageId"/> is null, empty
    ///     or whitespace.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when a folder source does not exist or
    ///     is unreachable.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the source fails for another
    ///     reason; the message is safe to show to the user.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     is canceled.</exception>
    Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(string packageId, CancellationToken cancellationToken);

    /// <summary>
    ///     Makes one discovered package's content available for reading.
    /// </summary>
    /// <param name="package">A package previously returned by <see cref="ListVersionsAsync"/> of
    ///     this same provider.</param>
    /// <param name="cancellationToken">Token to abandon the call.</param>
    /// <returns>A reader over the package content that the caller must dispose. Its
    ///     <see cref="IPackageContentReader.ContentIdentity"/> names the bytes the source delivered
    ///     (the SHA-256 of the zip or <c>.nupkg</c>), so the caller can pin and later compare
    ///     them; a provider that cannot name one leaves it <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="package"/> was discovered
    ///     at a different source (a package is never fetched from a source it did not come
    ///     from).</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when a folder source does not exist or
    ///     is unreachable.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the package cannot be fetched or
    ///     opened; the message is safe to show to the user.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     is canceled.</exception>
    Task<IPackageContentReader> AcquireAsync(DiscoveredPackage package, CancellationToken cancellationToken);
}
