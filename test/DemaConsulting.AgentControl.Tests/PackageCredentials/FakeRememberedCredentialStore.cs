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

using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.NuGet.Caching;

namespace DemaConsulting.AgentControl.Tests.PackageCredentials;

/// <summary>
///     An in-memory <see cref="IRememberedCredentialStore"/> for tests: holds credentials by
///     origin, records every call and can be told to be unavailable, to fail, or to be slow.
/// </summary>
/// <remarks>
///     Thread-safe, because the code under test calls it from worker threads.
/// </remarks>
internal sealed class FakeRememberedCredentialStore : IRememberedCredentialStore
{
    /// <summary>
    ///     The saved credentials by origin text.
    /// </summary>
    private readonly Dictionary<string, NuGetCredential> _saved = [];

    /// <summary>
    ///     The calls made, as <c>operation origin</c> text.
    /// </summary>
    private readonly List<string> _calls = [];

    /// <summary>
    ///     Gets or sets a value indicating whether a credential helper is "configured".
    /// </summary>
    public bool Available { get; set; } = true;

    /// <summary>
    ///     Gets or sets a value indicating whether <see cref="ApproveAsync"/> and
    ///     <see cref="RejectAsync"/> report that the store refused.
    /// </summary>
    public bool RefuseChanges { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether every call throws, to prove callers tolerate a
    ///     misbehaving store.
    /// </summary>
    public bool ThrowOnEveryCall { get; set; }

    /// <summary>
    ///     Gets or sets how long a lookup takes, so concurrent callers can be shown to share one
    ///     lookup.
    /// </summary>
    public TimeSpan FillDelay { get; set; }

    /// <summary>
    ///     Gets or sets a task <see cref="IsAvailableAsync"/> waits for before answering, so a
    ///     check that is still running can be observed; <see langword="null"/> answers at once.
    /// </summary>
    public Task? AvailableGate { get; set; }

    /// <summary>
    ///     Gets the calls made so far.
    /// </summary>
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>
    ///     Counts the calls of one kind.
    /// </summary>
    /// <param name="operation">The operation name: <c>available</c>, <c>fill</c>, <c>approve</c>
    ///     or <c>reject</c>.</param>
    /// <returns>How many such calls were made.</returns>
    public int CountOf(string operation) => Calls.Count(call => call.StartsWith(operation + " ", StringComparison.Ordinal));

    /// <summary>
    ///     Puts a credential into the store, as if the user had saved it earlier.
    /// </summary>
    /// <param name="origin">The feed origin.</param>
    /// <param name="credential">The credential.</param>
    public void Save(FeedOrigin origin, NuGetCredential credential)
    {
        lock (_saved)
        {
            _saved[origin.ToString()] = credential;
        }
    }

    /// <summary>
    ///     Looks at what the store holds for an origin, without recording a call.
    /// </summary>
    /// <param name="origin">The feed origin.</param>
    /// <returns>The saved credential, or <see langword="null"/>.</returns>
    public NuGetCredential? Peek(FeedOrigin origin)
    {
        lock (_saved)
        {
            return _saved.GetValueOrDefault(origin.ToString());
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(FeedOrigin origin, CancellationToken cancellationToken)
    {
        Record("available", origin);
        if (AvailableGate is not null)
        {
            await AvailableGate.WaitAsync(cancellationToken);
        }

        return Available;
    }

    /// <inheritdoc />
    public async Task<NuGetCredential?> FillAsync(FeedOrigin origin, CancellationToken cancellationToken)
    {
        Record("fill", origin);
        if (FillDelay > TimeSpan.Zero)
        {
            await Task.Delay(FillDelay, cancellationToken);
        }

        return Peek(origin);
    }

    /// <inheritdoc />
    public Task<bool> ApproveAsync(FeedOrigin origin, NuGetCredential credential, CancellationToken cancellationToken)
    {
        Record("approve", origin);
        if (RefuseChanges)
        {
            return Task.FromResult(false);
        }

        Save(origin, credential);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> RejectAsync(FeedOrigin origin, NuGetCredential? credential, CancellationToken cancellationToken)
    {
        Record("reject", origin);
        if (RefuseChanges)
        {
            return Task.FromResult(false);
        }

        lock (_saved)
        {
            _saved.Remove(origin.ToString());
        }

        return Task.FromResult(true);
    }

    /// <summary>
    ///     Records a call and throws when configured to.
    /// </summary>
    /// <param name="operation">The operation name.</param>
    /// <param name="origin">The feed origin.</param>
    private void Record(string operation, FeedOrigin origin)
    {
        lock (_calls)
        {
            _calls.Add($"{operation} {origin}");
        }

        if (ThrowOnEveryCall)
        {
            throw new InvalidOperationException("The credential store is broken.");
        }
    }
}
