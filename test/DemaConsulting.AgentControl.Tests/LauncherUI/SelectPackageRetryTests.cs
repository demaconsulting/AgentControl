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
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Tests.AgentPackageManagement;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Tests of the "Retry" action of <see cref="SelectPackageWindowViewModel"/>: a server source
///     whose versions could not be read (it needs a sign-in, or is unreachable) can be read again
///     without closing the dialog and without disturbing the other sources.
/// </summary>
public class SelectPackageRetryTests
{
    /// <summary>
    ///     Test that when the versions of a package could not be read, the error is shown, Retry is
    ///     offered, and retrying reads them again and shows the versions.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_Retry_VersionsFailedThenServerAnswers_ShowsVersions()
    {
        // Arrange: a server that first needs a sign-in, then works
        var provider = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"), ("contoso-agents", "1.1.0"))
        {
            ListVersionsException = new PackageSourceAccessException(
                PackageSourceFailureKind.AuthenticationFailed, "Source 'Feed' needs a sign-in.")
        };
        var viewModel = await CreateLoadedViewModelAsync(provider);
        Assert.False(viewModel.CanRetry);

        // Act: select the package, whose versions fail
        viewModel.SelectedPackageName = "contoso-agents";
        await viewModel.PendingVersionsLoad;

        // Assert: the error is shown inline with Retry
        Assert.True(viewModel.HasVersionsError);
        Assert.Contains("needs a sign-in", viewModel.VersionsError, StringComparison.Ordinal);
        Assert.True(viewModel.CanRetry);
        Assert.True(viewModel.RetryCommand.CanExecute(null));
        Assert.Empty(viewModel.AvailableVersions);

        // Act: the server recovers, and the user retries
        provider.ListVersionsException = null;
        viewModel.RetryCommand.Execute(null);
        await viewModel.PendingVersionsLoad;

        // Assert: the error is gone and the versions are shown, the latest selected
        Assert.False(viewModel.HasVersionsError);
        Assert.False(viewModel.CanRetry);
        Assert.Equal(["1.1.0", "1.0.0"], viewModel.AvailableVersions);
        Assert.Equal("1.1.0", viewModel.SelectedVersion);
        Assert.Equal(2, provider.ListVersionsCalls);
    }

    /// <summary>
    ///     Test that a retry that fails again keeps the error and Retry available.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_Retry_StillFailing_KeepsErrorAndRetryAvailable()
    {
        // Arrange
        var provider = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"))
        {
            ListVersionsException = new PackageSourceAccessException(PackageSourceFailureKind.Unreachable, "The server is down.")
        };
        var viewModel = await CreateLoadedViewModelAsync(provider);
        viewModel.SelectedPackageName = "contoso-agents";
        await viewModel.PendingVersionsLoad;

        // Act
        viewModel.RetryCommand.Execute(null);
        await viewModel.PendingVersionsLoad;

        // Assert
        Assert.True(viewModel.HasVersionsError);
        Assert.Contains("The server is down.", viewModel.VersionsError, StringComparison.Ordinal);
        Assert.True(viewModel.CanRetry);
        Assert.Equal(2, provider.ListVersionsCalls);
    }

    /// <summary>
    ///     Test that a source that could not be read is read again by Retry without touching the
    ///     other source.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_Retry_SourceFailedThenAnswers_LoadsOnlyThatSource()
    {
        // Arrange: source "Flaky" fails to list its packages the first time; "Steady" never fails
        var flaky = new FlakyIdsProvider("Flaky");
        var steady = new FakePackageSourceProvider("Steady", ("other-agents", "1.0.0"));
        var registry = new PackageSourceRegistry(
            [Source("Flaky"), Source("Steady")],
            definition => definition.Alias == "Flaky" ? flaky : steady);
        var viewModel = new SelectPackageWindowViewModel(registry, new PackageVersionCache());
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectedSource = viewModel.Sources[0];
        Assert.True(viewModel.HasSourceError);
        Assert.True(viewModel.CanRetry);
        var steadyCalls = steady.ListPackageIdsCalls;

        // Act
        flaky.Fail = false;
        viewModel.RetryCommand.Execute(null);
        await viewModel.PendingSourceRetry;

        // Assert: the flaky source now offers its package; the steady one was not read again
        Assert.False(viewModel.HasSourceError);
        Assert.False(viewModel.CanRetry);
        Assert.Equal(["flaky-agents"], viewModel.PackageNames);
        Assert.Equal(steadyCalls, steady.ListPackageIdsCalls);
    }

    /// <summary>
    ///     Test that Retry does nothing when there is nothing to retry.
    /// </summary>
    [Fact]
    public async Task SelectPackageWindowViewModel_Retry_NothingFailed_DoesNothing()
    {
        // Arrange
        var provider = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"));
        var viewModel = await CreateLoadedViewModelAsync(provider);

        // Act
        viewModel.RetryCommand.Execute(null);

        // Assert
        Assert.False(viewModel.CanRetry);
        Assert.Equal(0, provider.ListVersionsCalls);
    }

    /// <summary>
    ///     Creates a view model over one source and loads it.
    /// </summary>
    /// <param name="provider">The provider that serves the source.</param>
    /// <returns>The loaded view model, with the source selected.</returns>
    private static async Task<SelectPackageWindowViewModel> CreateLoadedViewModelAsync(IPackageSourceProvider provider)
    {
        var registry = new PackageSourceRegistry([Source(provider.Alias)], _ => provider);
        var viewModel = new SelectPackageWindowViewModel(registry, new PackageVersionCache());
        await viewModel.LoadAsync(CancellationToken.None);
        return viewModel;
    }

    /// <summary>
    ///     Creates a zip source definition for a fake provider to serve.
    /// </summary>
    /// <param name="alias">The alias.</param>
    /// <returns>The definition.</returns>
    private static PackageSourceDefinition Source(string alias) =>
        new() { Alias = alias, Type = PackageSourceType.Zip, Location = "unused" };

    /// <summary>
    ///     A source whose package listing fails until told to work.
    /// </summary>
    private sealed class FlakyIdsProvider : IPackageSourceProvider
    {
        /// <summary>
        ///     Initializes the provider.
        /// </summary>
        /// <param name="alias">The alias.</param>
        public FlakyIdsProvider(string alias) => Alias = alias;

        /// <inheritdoc />
        public string Alias { get; }

        /// <summary>
        ///     Gets or sets whether the package listing fails.
        /// </summary>
        public bool Fail { get; set; } = true;

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken) =>
            Fail
                ? throw new PackageSourceAccessException(PackageSourceFailureKind.Unreachable, "The server is down.")
                : Task.FromResult<IReadOnlyList<string>>(["flaky-agents"]);

        /// <inheritdoc />
        public Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(string packageId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DiscoveredPackage>>([]);

        /// <inheritdoc />
        public Task<DemaConsulting.AgentControl.RepoSync.IPackageContentReader> AcquireAsync(
            DiscoveredPackage package, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
