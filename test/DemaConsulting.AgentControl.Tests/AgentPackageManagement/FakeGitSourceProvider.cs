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
///     In-memory provider that answers like a Git source does for the editor's connection test: it
///     implements <see cref="IGitTagPreview"/> and returns a prepared preview (or fails, or waits
///     for a gate), so tests drive the Git editor without git, a repository or a network.
/// </summary>
/// <remarks>
///     Listing and acquiring are never used by the editor and throw
///     <see cref="NotSupportedException"/>.
/// </remarks>
/// <param name="alias">The alias this fake reports.</param>
internal sealed class FakeGitSourceProvider(string alias = "any") : IPackageSourceProvider, IGitTagPreview
{
    /// <summary>
    ///     Number of times <see cref="PreviewAsync"/> was called.
    /// </summary>
    private int _previewCalls;

    /// <inheritdoc />
    public string Alias { get; } = alias;

    /// <summary>
    ///     Gets the number of <see cref="PreviewAsync"/> calls so far.
    /// </summary>
    public int PreviewCalls => Volatile.Read(ref _previewCalls);

    /// <summary>
    ///     Gets or sets the preview that <see cref="PreviewAsync"/> returns.
    /// </summary>
    public GitTagPreview Preview { get; set; } = new(0, [], 0, []);

    /// <summary>
    ///     Gets or sets an exception that <see cref="PreviewAsync"/> throws (after the gate), or
    ///     <see langword="null"/> to succeed.
    /// </summary>
    public Exception? PreviewException { get; set; }

    /// <summary>
    ///     Gets or sets a task that <see cref="PreviewAsync"/> awaits before returning, or
    ///     <see langword="null"/> to return immediately.
    /// </summary>
    public Task? Gate { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the wait for <see cref="Gate"/> ignores the
    ///     cancellation token, like a source that cannot interrupt a request it already sent.
    /// </summary>
    public bool IgnoreCancellation { get; set; }

    /// <summary>
    ///     Creates a tag that is a package version, for a preview.
    /// </summary>
    /// <param name="version">The version text.</param>
    /// <param name="tag">The tag name.</param>
    /// <returns>The discovered package.</returns>
    public static DiscoveredPackage Version(string version, string tag) =>
        new("agents", PackageVersion.TryParse(version, out var parsed) && parsed is not null
            ? parsed
            : throw new ArgumentException($"Invalid test version '{version}'.", nameof(version)), tag, "any");

    /// <inheritdoc />
    public async Task<GitTagPreview> PreviewAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _previewCalls);
        var gate = Gate;
        if (gate is not null)
        {
            await (IgnoreCancellation ? gate : gate.WaitAsync(cancellationToken));
        }

        return PreviewException is null ? Preview : throw PreviewException;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("The Git editor previews tags; it never lists package IDs.");

    /// <inheritdoc />
    public Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(string packageId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The Git editor previews tags; it never lists versions through this provider.");

    /// <inheritdoc />
    public Task<IPackageContentReader> AcquireAsync(DiscoveredPackage package, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The connection test never acquires a package.");
}
