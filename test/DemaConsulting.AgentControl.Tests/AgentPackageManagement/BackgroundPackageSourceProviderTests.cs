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
using DemaConsulting.AgentControl.Utilities;

namespace DemaConsulting.AgentControl.Tests.AgentPackageManagement;

/// <summary>
///     Unit tests for <see cref="BackgroundPackageSourceProvider"/>: every call of the decorated
///     provider runs on a worker thread, an acquired package's content identity is computed there
///     before it is returned, and answers, failures and cancellation pass through unchanged.
/// </summary>
/// <remarks>
///     The decorated provider is a scripted fake whose calls do what each test needs (block until
///     released, or ask <see cref="BlockingCallGuard"/> as a real provider's disk or process work
///     would). The calling thread is guarded only around the synchronous call, and the record of
///     refused calls is read before the first <c>await</c>, because the record is kept per thread.
/// </remarks>
public class BackgroundPackageSourceProviderTests
{
    /// <summary>
    ///     A package the scripted provider can be asked to acquire.
    /// </summary>
    private static readonly DiscoveredPackage Package =
        new("contoso-agents", PackageVersion.TryParse("1.0.0", out var version) ? version! : throw new InvalidOperationException(),
            "contoso-agents-1.0.0.zip", "Feed");

    /// <summary>
    ///     Test that acquiring through the decorator returns at once even when the decorated provider
    ///     blocks before its first wait (as a provider that starts git or reads its cache does), and
    ///     completes with the provider's package once that work ends.
    /// </summary>
    [Fact]
    public async Task BackgroundPackageSourceProvider_AcquireAsync_InnerBlocksSynchronously_ReturnsPendingTask()
    {
        // Arrange: a provider whose acquire blocks its calling thread until released
        using var release = new ManualResetEventSlim();
        var reader = new CountingReader();
        var inner = new ScriptedProvider
        {
            Acquire = (_, _) =>
            {
                release.Wait();
                return Task.FromResult<IPackageContentReader>(reader);
            }
        };
        var provider = new BackgroundPackageSourceProvider(inner);

        // Act
        var acquiring = provider.AcquireAsync(Package, CancellationToken.None);
        var completedWhileBlocked = acquiring.IsCompleted;
        release.Set();
        var acquired = await acquiring;

        // Assert: the caller was not held, and the provider's own reader came back
        Assert.False(completedWhileBlocked);
        Assert.Same(reader, acquired);
    }

    /// <summary>
    ///     Test that the decorator reads the acquired package's content identity on its worker before
    ///     returning the reader, so a caller on a guarded thread that reads it afterwards hashes
    ///     nothing.
    /// </summary>
    [Fact]
    public async Task BackgroundPackageSourceProvider_AcquireAsync_ComputesContentIdentityBeforeReturning()
    {
        // Arrange: a reader whose identity is computed (once, then remembered) with a guarded call
        var reader = new CountingReader();
        var provider = new BackgroundPackageSourceProvider(
            new ScriptedProvider { Acquire = (_, _) => Task.FromResult<IPackageContentReader>(reader) });

        // Act: acquire from a guarded thread, then read the identity there as a card would
        Task<IPackageContentReader> acquiring;
        using (BlockingCallGuard.GuardCurrentThread())
        {
            acquiring = provider.AcquireAsync(Package, CancellationToken.None);
        }

        var acquireViolations = BlockingCallGuard.TakeViolations();
        var acquired = await acquiring;
        var computedBeforeReturn = reader.IdentityComputations;
        string? identity;
        using (BlockingCallGuard.GuardCurrentThread())
        {
            identity = acquired.ContentIdentity;
        }

        var readViolations = BlockingCallGuard.TakeViolations();

        // Assert: computed once, before the reader was returned, and never on the guarded thread
        Assert.Empty(acquireViolations);
        Assert.Empty(readViolations);
        Assert.Equal(1, computedBeforeReturn);
        Assert.Equal(1, reader.IdentityComputations);
        Assert.Equal(CountingReader.Identity, identity);
    }

    /// <summary>
    ///     Test that a listing made from a guarded thread runs the decorated provider's blocking work
    ///     on another thread, and returns its answer unchanged.
    /// </summary>
    [Fact]
    public async Task BackgroundPackageSourceProvider_ListVersionsAsync_OnGuardedThread_RunsInnerOffThread()
    {
        // Arrange: a provider whose listing makes a guarded call before it answers
        IReadOnlyList<DiscoveredPackage> answer = [Package];
        IReadOnlyList<string> ids = ["contoso-agents"];
        var inner = new ScriptedProvider
        {
            ListVersions = (_, _) =>
            {
                BlockingCallGuard.ThrowIfGuarded("scripted listing");
                return Task.FromResult(answer);
            },
            ListPackageIds = _ =>
            {
                BlockingCallGuard.ThrowIfGuarded("scripted listing");
                return Task.FromResult(ids);
            }
        };
        var provider = new BackgroundPackageSourceProvider(inner);

        // Act
        Task<IReadOnlyList<DiscoveredPackage>> listing;
        Task<IReadOnlyList<string>> listingIds;
        using (BlockingCallGuard.GuardCurrentThread())
        {
            listing = provider.ListVersionsAsync("contoso-agents", CancellationToken.None);
            listingIds = provider.ListPackageIdsAsync(CancellationToken.None);
        }

        var violations = BlockingCallGuard.TakeViolations();

        // Assert: nothing was refused on the guarded thread, and the answers pass through
        Assert.Empty(violations);
        Assert.Same(answer, await listing);
        Assert.Same(ids, await listingIds);
        Assert.Equal("Feed", provider.Alias);
    }

    /// <summary>
    ///     Test that a call whose token is already canceled is canceled without reaching the decorated
    ///     provider, and that a failure of the decorated provider is passed through unchanged.
    /// </summary>
    [Fact]
    public async Task BackgroundPackageSourceProvider_Canceled_PropagatesCancellation()
    {
        // Arrange
        var calls = 0;
        var failure = new InvalidOperationException("The source failed.");
        var inner = new ScriptedProvider
        {
            ListVersions = (_, _) =>
            {
                calls++;
                return Task.FromException<IReadOnlyList<DiscoveredPackage>>(failure);
            }
        };
        var provider = new BackgroundPackageSourceProvider(inner);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var canceled = await Record.ExceptionAsync(() => provider.ListVersionsAsync("contoso-agents", cancellation.Token));
        var callsAfterCancel = calls;
        var failed = await Record.ExceptionAsync(() => provider.ListVersionsAsync("contoso-agents", CancellationToken.None));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(canceled);
        Assert.Equal(0, callsAfterCancel);
        Assert.Same(failure, failed);
    }

    /// <summary>
    ///     Test that a reader whose content identity cannot be read is disposed and the failure is
    ///     thrown to the caller, so no reader is left open.
    /// </summary>
    [Fact]
    public async Task BackgroundPackageSourceProvider_AcquireAsync_IdentityFails_DisposesReaderAndThrows()
    {
        // Arrange
        var reader = new CountingReader { IdentityFailure = new IOException("The package could not be read.") };
        var provider = new BackgroundPackageSourceProvider(
            new ScriptedProvider { Acquire = (_, _) => Task.FromResult<IPackageContentReader>(reader) });

        // Act
        var thrown = await Record.ExceptionAsync(() => provider.AcquireAsync(Package, CancellationToken.None));

        // Assert
        Assert.Same(reader.IdentityFailure, thrown);
        Assert.True(reader.Disposed);
    }

    /// <summary>
    ///     A provider whose calls run the delegates a test sets; a call without one fails.
    /// </summary>
    private sealed class ScriptedProvider : IPackageSourceProvider
    {
        /// <summary>
        ///     Gets the listing of package IDs.
        /// </summary>
        public Func<CancellationToken, Task<IReadOnlyList<string>>>? ListPackageIds { get; init; }

        /// <summary>
        ///     Gets the listing of versions.
        /// </summary>
        public Func<string, CancellationToken, Task<IReadOnlyList<DiscoveredPackage>>>? ListVersions { get; init; }

        /// <summary>
        ///     Gets the acquire.
        /// </summary>
        public Func<DiscoveredPackage, CancellationToken, Task<IPackageContentReader>>? Acquire { get; init; }

        /// <inheritdoc />
        public string Alias => "Feed";

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken) =>
            (ListPackageIds ?? throw new NotSupportedException())(cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(string packageId, CancellationToken cancellationToken) =>
            (ListVersions ?? throw new NotSupportedException())(packageId, cancellationToken);

        /// <inheritdoc />
        public Task<IPackageContentReader> AcquireAsync(DiscoveredPackage package, CancellationToken cancellationToken) =>
            (Acquire ?? throw new NotSupportedException())(package, cancellationToken);
    }

    /// <summary>
    ///     A reader with no files whose content identity is computed on first read with a guarded
    ///     call (as hashing a real package is) and then remembered, counting the computations.
    /// </summary>
    private sealed class CountingReader : IPackageContentReader
    {
        /// <summary>
        ///     The identity the reader reports.
        /// </summary>
        public const string Identity = "files-sha256:0000000000000000000000000000000000000000000000000000000000000000";

        /// <summary>
        ///     How many times the identity was computed.
        /// </summary>
        private int _identityComputations;

        /// <summary>
        ///     Whether the identity was computed.
        /// </summary>
        private bool _computed;

        /// <summary>
        ///     Gets the failure the identity computation throws, or <see langword="null"/> for none.
        /// </summary>
        public Exception? IdentityFailure { get; init; }

        /// <summary>
        ///     Gets how many times the identity was computed.
        /// </summary>
        public int IdentityComputations => Volatile.Read(ref _identityComputations);

        /// <summary>
        ///     Gets a value indicating whether the reader was disposed.
        /// </summary>
        public bool Disposed { get; private set; }

        /// <inheritdoc />
        public string Description => "scripted package";

        /// <inheritdoc />
        public string? ContentIdentity
        {
            get
            {
                if (!_computed)
                {
                    BlockingCallGuard.ThrowIfGuarded("scripted package hash");
                    Interlocked.Increment(ref _identityComputations);
                    if (IdentityFailure is not null)
                    {
                        throw IdentityFailure;
                    }

                    _computed = true;
                }

                return Identity;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ListFiles() => [];

        /// <inheritdoc />
        public Stream OpenRead(string relativePath) => throw new InvalidOperationException("No such file.");

        /// <inheritdoc />
        public string? ReadText(string relativePath) => null;

        /// <inheritdoc />
        public void Dispose() => Disposed = true;
    }
}
