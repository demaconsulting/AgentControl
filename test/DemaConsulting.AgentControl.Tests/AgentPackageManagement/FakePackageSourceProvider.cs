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

using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.RepoSync;

namespace DemaConsulting.AgentControl.Tests.AgentPackageManagement;

/// <summary>
///     In-memory <see cref="IPackageSourceProvider"/> that serves a fixed list of packages and
///     counts how often each operation is called, so tests can prove which source was (and was
///     not) contacted without touching the filesystem or network.
/// </summary>
/// <remarks>
///     <see cref="AcquireAsync"/> deliberately throws <see cref="NotSupportedException"/> after
///     recording the call: tests that need real package content use the real providers instead.
///     An optional gate lets a test hold <see cref="ListVersionsAsync"/> open to create an
///     in-flight call. Thread-safe.
/// </remarks>
internal sealed class FakePackageSourceProvider : IPackageSourceProvider
{
    /// <summary>
    ///     The packages this fake serves, as (package ID, version text) pairs.
    /// </summary>
    private readonly List<(string PackageId, string Version)> _packages;

    /// <summary>
    ///     Number of times <see cref="ListPackageIdsAsync"/> was called.
    /// </summary>
    private int _listPackageIdsCalls;

    /// <summary>
    ///     Number of times <see cref="ListVersionsAsync"/> was called.
    /// </summary>
    private int _listVersionsCalls;

    /// <summary>
    ///     Number of times <see cref="AcquireAsync"/> was called.
    /// </summary>
    private int _acquireCalls;

    /// <summary>
    ///     Initializes a fake serving the given packages.
    /// </summary>
    /// <param name="alias">The source alias this fake reports.</param>
    /// <param name="packages">The packages served as (package ID, version text) pairs.</param>
    public FakePackageSourceProvider(string alias, params (string PackageId, string Version)[] packages)
    {
        Alias = alias;
        _packages = [.. packages];
    }

    /// <summary>
    ///     Adds a package version to what this fake serves from now on, like a release being
    ///     published while the application runs.
    /// </summary>
    /// <param name="packageId">The package ID.</param>
    /// <param name="version">The version text.</param>
    public void Publish(string packageId, string version)
    {
        lock (_packages)
        {
            _packages.Add((packageId, version));
        }
    }

    /// <inheritdoc />
    public string Alias { get; }

    /// <summary>
    ///     Gets the number of <see cref="ListPackageIdsAsync"/> calls so far.
    /// </summary>
    public int ListPackageIdsCalls => Volatile.Read(ref _listPackageIdsCalls);

    /// <summary>
    ///     Gets the number of <see cref="ListVersionsAsync"/> calls so far.
    /// </summary>
    public int ListVersionsCalls => Volatile.Read(ref _listVersionsCalls);

    /// <summary>
    ///     Gets the number of <see cref="AcquireAsync"/> calls so far.
    /// </summary>
    public int AcquireCalls => Volatile.Read(ref _acquireCalls);

    /// <summary>
    ///     Gets the total number of provider calls of any kind, i.e. how often this source was
    ///     contacted.
    /// </summary>
    public int TotalCalls => ListPackageIdsCalls + ListVersionsCalls + AcquireCalls;

    /// <summary>
    ///     Gets or sets a task that <see cref="ListVersionsAsync"/> awaits before returning, or
    ///     <see langword="null"/> to return immediately.
    /// </summary>
    public Task? ListVersionsGate { get; set; }

    /// <summary>
    ///     Gets or sets whether a call waits for <see cref="ListVersionsGate"/> without honoring its
    ///     cancellation token, like a provider call that is already in flight and only notices a
    ///     cancellation request later (so a test can hold a canceled check open).
    /// </summary>
    public bool ListVersionsGateIgnoresCancellation { get; set; }

    /// <summary>
    ///     Gets the cancellation token of the most recent <see cref="ListVersionsAsync"/> call, or
    ///     <see langword="null"/> before the first call.
    /// </summary>
    public CancellationToken? LastListVersionsToken { get; private set; }

    /// <summary>
    ///     Gets or sets an exception that <see cref="ListVersionsAsync"/> throws (after the gate),
    ///     or <see langword="null"/> to succeed.
    /// </summary>
    public Exception? ListVersionsException { get; set; }

    /// <summary>
    ///     Gets the revision (for example a Git commit) the fake reports per version text, for
    ///     versions that should carry one; other versions are listed without a revision.
    /// </summary>
    public Dictionary<string, string> Revisions { get; } = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _listPackageIdsCalls);
        return Task.FromResult<IReadOnlyList<string>>(
            Snapshot().Select(p => p.PackageId).Distinct().OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>
    ///     Takes a copy of the packages served now.
    /// </summary>
    /// <returns>The packages.</returns>
    private List<(string PackageId, string Version)> Snapshot()
    {
        lock (_packages)
        {
            return [.. _packages];
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(
        string packageId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _listVersionsCalls);
        LastListVersionsToken = cancellationToken;

        // Snapshot the gate so a later change does not affect this call; waiting honors
        // cancellation, like a real source does
        var gate = ListVersionsGate;
        if (gate is not null)
        {
            if (ListVersionsGateIgnoresCancellation)
            {
                await gate;
            }
            else
            {
                await gate.WaitAsync(cancellationToken);
            }
        }

        if (ListVersionsException is not null)
        {
            throw ListVersionsException;
        }

        return Snapshot()
            .Where(p => string.Equals(p.PackageId, packageId, StringComparison.Ordinal))
            .Select(p => new DiscoveredPackage(
                p.PackageId, ParseVersion(p.Version), p.Version, Alias,
                Revisions.TryGetValue(p.Version, out var revision) ? revision : null))
            .ToList();
    }

    /// <inheritdoc />
    public Task<IPackageContentReader> AcquireAsync(DiscoveredPackage package, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _acquireCalls);
        throw new NotSupportedException("FakePackageSourceProvider does not supply package content.");
    }

    /// <summary>
    ///     Parses a version text, failing the test setup loudly if it is not valid.
    /// </summary>
    /// <param name="version">The version text.</param>
    /// <returns>The parsed version.</returns>
    private static PackageVersion ParseVersion(string version) =>
        PackageVersion.TryParse(version, out var parsed) && parsed is not null
            ? parsed
            : throw new ArgumentException($"Invalid test version '{version}'.", nameof(version));
}
