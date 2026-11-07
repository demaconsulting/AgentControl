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
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.AgentControl.RepoConfig;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Startup;
using DemaConsulting.AgentControl.Tests.AgentPackageManagement;
using DemaConsulting.AgentControl.Tests.PackageCredentials;
using DemaConsulting.NuGet.Caching;
using Material.Icons;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Tests of <see cref="RepoCardViewModel"/> for a repo pinned to a NuGet server source,
///     against an in-process mock feed: the card's chip identifies the source, and the background
///     upgrade check never opens the sign-in dialog while a check the user asked for may.
/// </summary>
public sealed class RepoCardServerSourceTests : IAsyncLifetime
{
    /// <summary>
    ///     The feed account's user name.
    /// </summary>
    private const string FeedUser = "svc-agents";

    /// <summary>
    ///     The feed account's password.
    /// </summary>
    private const string FeedPassword = "card-test-pw-5521";

    /// <summary>
    ///     The package every test pins.
    /// </summary>
    private const string PackageId = "contoso-agents";

    /// <summary>
    ///     The feeds started by the running test.
    /// </summary>
    private readonly List<MockNuGetFeed> _feeds = [];

    /// <summary>
    ///     The temporary folders created by the running test.
    /// </summary>
    private readonly List<string> _tempPaths = [];

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var feed in _feeds)
        {
            await feed.DisposeAsync();
        }

        foreach (var path in _tempPaths)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup of a temporary folder
            }
        }
    }

    /// <summary>
    ///     Test that the package chip names the pinned source by alias and shows the server icon,
    ///     which is different from the zip and NuGet-folder icons.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_PinnedToServerSource_ChipShowsAliasAndServerIcon()
    {
        // Arrange
        var feed = await StartFeedAsync("1.0.0", "1.1.0");
        var card = CreateCard(feed, credentials: null);

        // Assert
        Assert.Equal("Feed", card.PinnedSourceLabel);
        Assert.Equal(MaterialIconKind.ServerNetwork, card.PinnedSourceIcon);
        Assert.NotEqual(MaterialIconKind.FolderZip, card.PinnedSourceIcon);
        Assert.NotEqual(MaterialIconKind.PackageVariant, card.PinnedSourceIcon);
        Assert.Contains("NuGet server", card.PackageChipTooltip, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an anonymous server answers the upgrade check with the newer version.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_RefreshUpgradeStatusAsync_AnonymousServer_OffersNewerVersion()
    {
        // Arrange
        var feed = await StartFeedAsync("1.0.0", "1.1.0");
        var card = CreateCard(feed, credentials: null);

        // Act
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(card.IsUpgradeAvailable);
        Assert.Equal("1.1.0", card.LatestAvailableVersion);
        Assert.False(card.HasSourceProblem);
    }

    /// <summary>
    ///     Test that the background upgrade check against a server that needs a sign-in never
    ///     opens the dialog, and instead tells the user (on the card) how to sign in.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_RefreshUpgradeStatusAsync_BackgroundCheckNeedsSignIn_DoesNotPromptAndExplains()
    {
        // Arrange
        var feed = await StartFeedAsync("1.0.0", "1.1.0");
        RequireSignIn(feed);
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential(FeedUser, FeedPassword));
        var card = CreateCard(feed, new PackageCredentialContext(new SessionCredentialStore(), prompt));

        // Act: the background check (the default)
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert: no dialog, no upgrade, and the card says what to do
        Assert.Equal(0, prompt.AskCount);
        Assert.False(card.IsUpgradeAvailable);
        Assert.True(card.HasSourceProblem);
        Assert.Contains("needs a sign-in", card.SourceProblem, StringComparison.Ordinal);
        Assert.Contains("Refresh", card.SourceProblem, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a check the user asked for may open the dialog, then finds the upgrade, and
    ///     that a later background check (even by another card) reuses the session credential
    ///     without asking again.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_RefreshUpgradeStatusAsync_UserRequestedCheck_PromptsOnceThenBackgroundChecksReuseCredential()
    {
        // Arrange
        var feed = await StartFeedAsync("1.0.0", "1.1.0");
        RequireSignIn(feed);
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential(FeedUser, FeedPassword));
        var context = new PackageCredentialContext(new SessionCredentialStore(), prompt);
        var card = CreateCard(feed, context);

        // Act: a user-requested check
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken, allowPrompt: true);

        // Assert
        Assert.Equal(1, prompt.AskCount);
        Assert.True(card.IsUpgradeAvailable);
        Assert.False(card.HasSourceProblem);

        // Act: another card's background check, using a fresh version cache
        var other = CreateCard(feed, context);
        await other.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert: still one question in total
        Assert.Equal(1, prompt.AskCount);
        Assert.True(other.IsUpgradeAvailable);
    }

    /// <summary>
    ///     Starts a mock feed serving the given versions of the test package.
    /// </summary>
    /// <param name="versions">The versions to serve.</param>
    /// <returns>The running feed.</returns>
    private async Task<MockNuGetFeed> StartFeedAsync(params string[] versions)
    {
        var feed = await MockNuGetFeed.StartAsync();
        foreach (var version in versions)
        {
            feed.AddPackage(PackageId, version);
        }

        _feeds.Add(feed);
        return feed;
    }

    /// <summary>
    ///     Makes a feed require the test account's credentials.
    /// </summary>
    /// <param name="feed">The feed.</param>
    private static void RequireSignIn(MockNuGetFeed feed)
    {
        feed.RequiredUser = FeedUser;
        feed.RequiredPassword = FeedPassword;
    }

    /// <summary>
    ///     Creates a card for a repo pinned to <c>contoso-agents 1.0.0</c> from the feed's source.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="credentials">The credential facilities, or <see langword="null"/>.</param>
    /// <returns>The card.</returns>
    private RepoCardViewModel CreateCard(MockNuGetFeed feed, PackageCredentialContext? credentials)
    {
        var repoRoot = CreateTempDirectory();
        RepoPinStore.Save(repoRoot, new RepoPin { Source = "Feed", PackageName = PackageId, Version = "1.0.0" });
        var settings = new AppSettings
        {
            Sources =
            [
                new PackageSourceDefinition
                {
                    Alias = "Feed",
                    Type = PackageSourceType.NuGet,
                    Location = feed.IndexUrl,
                    PackageIds = [PackageId],
                    AllowInsecureHttp = true
                }
            ]
        };
        var recentRepo = new RecentRepo
        {
            Path = repoRoot,
            PinnedPackageName = PackageId,
            PinnedPackageVersion = "1.0.0",
            PinnedSource = "Feed"
        };
        var startupOptions = StartupOptions.Parse(["--config-dir", CreateTempDirectory()]);
        return new RepoCardViewModel(
            recentRepo, () => settings, new PackageVersionCache(), startupOptions, credentials: credentials);
    }

    /// <summary>
    ///     Creates a new empty temporary directory removed when the test ends.
    /// </summary>
    /// <returns>The directory's absolute path.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_card_server_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }
}
