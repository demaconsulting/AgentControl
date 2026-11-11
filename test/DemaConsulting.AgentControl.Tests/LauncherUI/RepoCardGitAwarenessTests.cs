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

using System.Text;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.RepoConfig;
using DemaConsulting.AgentControl.RepoSync;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Tests.AgentPackageManagement;
using Material.Icons;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Tests of how <see cref="RepoCardViewModel"/> shows and treats a repo whose package a Git source
///     supplies: the Git icon and the tag in the chip tooltip, that a user-started refresh reads the Git
///     sources again (so a newly pushed tag is offered), a Git that is too old, "Change package..." on a
///     pinned repo and the confirmation when the same version now delivers different files.
/// </summary>
/// <remarks>
///     Most tests put a fake provider behind the card's registry, so no git or network is involved; the
///     refresh test uses a real local git repository. The repo folders are real temporary folders.
/// </remarks>
public sealed class RepoCardGitAwarenessTests : IDisposable
{
    /// <summary>
    ///     The commit the repo was set up from.
    /// </summary>
    private const string PinnedCommit = "abcdef0123456789abcdef0123456789abcdef01";

    /// <summary>
    ///     Another commit, for a tag that was moved to other files.
    /// </summary>
    private const string OtherCommit = "1234567890abcdef1234567890abcdef12345678";

    /// <summary>
    ///     The folders to delete after a test.
    /// </summary>
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that a repo supplied by a Git source shows the Git icon and the source's alias on its chip.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_SuppliedByGitSource_ShowsGitIconAndAlias()
    {
        // Arrange / Act
        var (_, card, _) = CreatePinnedCard();

        // Assert
        Assert.Equal(MaterialIconKind.Git, card.PinnedSourceIcon);
        Assert.Equal("Repo", card.PinnedSourceLabel);
        Assert.Equal("contoso-agents 1.2.3", card.PackageChipText);
    }

    /// <summary>
    ///     Test that the chip tooltip of a package a Git source supplies names the tag the version has,
    ///     and nothing about a commit.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_PackageChipTooltip_GitSource_NamesTagOnly()
    {
        // Arrange / Act
        var (_, card, _) = CreatePinnedCard();

        // Assert
        Assert.Equal("release/1.2.3", card.PinnedTagName);
        Assert.Equal(
            "Agent files: contoso-agents 1.2.3 from source 'Repo' (git repository), tag release/1.2.3.",
            card.PackageChipTooltip);
    }

    /// <summary>
    ///     Test that the chip tooltip of a package a zip source supplies has no tag.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_PackageChipTooltip_ZipSource_HasNoTag()
    {
        // Arrange / Act
        var (_, card, _) = CreatePinnedCard(source: ZipSource());

        // Assert
        Assert.Null(card.PinnedTagName);
        Assert.Equal(
            "Agent files: contoso-agents 1.2.3 from source 'Repo' (zip folder).",
            card.PackageChipTooltip);
    }

    /// <summary>
    ///     Test that the pin does not record the tag: editing the source's tag pattern maps the pinned
    ///     version to another tag, which the chip tooltip then names.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_PinnedTagName_TagPatternEdited_FollowsTheSourcesPattern()
    {
        // Arrange: a card supplied by a Git source with the pattern release/{version}
        var (_, card, _) = CreatePinnedCard(out var settings);
        Assert.Equal("release/1.2.3", card.PinnedTagName);

        // Act: the user edits the tag pattern
        var edited = GitSource();
        edited.TagPattern = "v{version}";
        settings.Sources = [edited];

        // Assert
        Assert.Equal("v1.2.3", card.PinnedTagName);
        Assert.Contains("tag v1.2.3.", card.PackageChipTooltip, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a user-started refresh reads a Git source again instead of reusing the session's
    ///     cached listing, so a tag pushed after the first listing (a new version) is offered as the
    ///     upgrade, while the background check keeps using the cache. A real local git repository is used.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_RefreshAsync_GitSourceNewTagPushedAfterCachedListing_ListsAgainAndOffersNewVersion()
    {
        // Arrange: a repository tagged v1.0.0, and a card supplied by it that has listed it once
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var files = new Dictionary<string, string>
        {
            [".github/agents/copilot.md"] = "one",
            ["release-notes.md"] = "notes"
        };
        fixture.Commit(files, "one");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var repo = CreateTempDirectory();
        RepoPinStore.Save(repo, new RepoPin { PackageName = "contoso-agents", Version = "1.0.0" });
        var settings = new AppSettings { Sources = [fixture.CreateDefinition()] };
        var card = new RepoCardViewModel(
            new RecentRepo { Path = repo, PinnedPackageName = "contoso-agents", PinnedPackageVersion = "1.0.0", PinnedSource = "Repo" },
            () => settings,
            new PackageVersionCache(),
            registryFactory: current => PackageSourceRegistry.Create(current.GetEffectiveSources(), git: fixture.CreateOptions()));
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);
        Assert.False(card.IsUpgradeAvailable);
        files[".github/agents/copilot.md"] = "two";
        fixture.Commit(files, "two");
        fixture.Tag("v1.1.0");
        fixture.Push();

        // Act: a background check answers from the cache; then the user refreshes the card
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);
        var upgradeAfterBackground = card.IsUpgradeAvailable;
        await card.RefreshAsync(TestContext.Current.CancellationToken);

        // Assert: the cache was used by the background check only, and the refresh found the new tag
        Assert.False(upgradeAfterBackground);
        Assert.True(card.IsUpgradeAvailable);
        Assert.Equal("1.1.0", card.LatestAvailableVersion);
        Assert.False(card.HasSourceProblem);
    }

    /// <summary>
    ///     Test that a user-started refresh reads every configured Git source again, not only the one that
    ///     supplies the package: a newly pushed version may be in any of them.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_RefreshAsync_SeveralGitSources_ListsEachOfThemAgain()
    {
        // Arrange: two Git sources, each with its own fake provider, listed once by a background check
        var repo = CreateTempDirectory();
        RepoPinStore.Save(repo, new RepoPin { PackageName = "contoso-agents", Version = "1.2.3" });
        var repoProvider = new FakePackageSourceProvider("Repo", ("contoso-agents", "1.2.3"));
        var mirror = GitSource();
        mirror.Alias = "Mirror";
        var mirrorProvider = new FakePackageSourceProvider("Mirror", ("contoso-agents", "1.2.3"));
        var settings = new AppSettings { Sources = [GitSource(), mirror] };
        var card = new RepoCardViewModel(
            new RecentRepo { Path = repo },
            () => settings,
            new PackageVersionCache(),
            registryFactory: _ => new PackageSourceRegistry(
                settings.Sources, definition => definition.Alias == "Repo" ? repoProvider : mirrorProvider));
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, repoProvider.ListVersionsCalls);
        Assert.Equal(1, mirrorProvider.ListVersionsCalls);

        // Act: the user refreshes the card
        await card.RefreshAsync(TestContext.Current.CancellationToken);

        // Assert: both Git sources were read again
        Assert.Equal(2, repoProvider.ListVersionsCalls);
        Assert.Equal(2, mirrorProvider.ListVersionsCalls);
    }

    /// <summary>
    ///     Test that a user-started refresh of a repo supplied by a source that is not a Git source still
    ///     uses the cached listing: only mutable Git tags need to be read again.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_RefreshAsync_ZipSource_KeepsUsingCachedListing()
    {
        // Arrange
        var (_, card, provider) = CreatePinnedCard(source: ZipSource());
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Act
        await card.RefreshAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, provider.ListVersionsCalls);
    }

    /// <summary>
    ///     Test that a Git that is too old, found by the background check, becomes a package source
    ///     problem with the reason (not a sign-in prompt), and that the card is a problem.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_RefreshUpgradeStatus_GitTooOld_ShowsSourceProblemWithoutSignIn()
    {
        // Arrange
        var (_, card, provider) = CreatePinnedCard();
        provider.ListVersionsException =
            new GitRemoteException(GitFailureKind.GitTooOld, "Git 2.20 is too old; install Git 2.30 or newer.");

        // Act
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(card.HasSourceProblem);
        Assert.False(card.SourceNeedsSignIn);
        Assert.Equal("Git 2.20 is too old; install Git 2.30 or newer.", card.SourceProblem);
        Assert.Equal(RepoCardStatus.Problem, card.Status);
        Assert.False(card.IsUpgradeAvailable);
    }

    /// <summary>
    ///     Test that "Change package..." is offered for a pinned repo: the command can execute, raises the
    ///     Select Package request, and the agent-file menu group is shown.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_SelectPackageCommand_PinnedRepo_RaisesSelectPackageRequested()
    {
        // Arrange
        var (_, card, _) = CreatePinnedCard();
        var requests = 0;
        card.SelectPackageRequested += (_, _) => requests++;

        // Act
        var canExecute = card.SelectPackageCommand.CanExecute(null);
        card.SelectPackageCommand.Execute(null);

        // Assert
        Assert.True(canExecute);
        Assert.True(card.HasPinnedPackage);
        Assert.True(card.HasAgentFileActions);
        Assert.Equal(1, requests);
    }

    /// <summary>
    ///     Test that choosing the pinned version again when its tag now delivers different files (it was
    ///     moved to another commit) asks for confirmation and changes nothing until the user answers.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_ApplySelectedPackage_GitVersionWithOtherFiles_AsksAndChangesNothing()
    {
        // Arrange: the repo was set up from the first commit; the tag now points to a commit with other files
        var (repo, card, provider) = CreateApplyingCard();
        var requests = new List<ContentChangeConfirmationRequest>();
        var errors = new List<string>();
        card.ContentChangeConfirmationRequested += (_, request) => requests.Add(request);
        card.ErrorOccurred += (_, message) => errors.Add(message);
        var pinBefore = await File.ReadAllTextAsync(Path.Combine(repo, ".agentcontrol.json"), TestContext.Current.CancellationToken);
        provider.Commit = OtherCommit;

        // Act: Change package, same version
        await card.ApplySelectedPackageAsync(
            new PackageReference("Repo", "contoso-agents", "1.2.3"), TestContext.Current.CancellationToken);

        // Assert: one question, nothing extracted, no error, pin as it was
        var asked = Assert.Single(requests);
        Assert.Equal(new PackageReference("Repo", "contoso-agents", "1.2.3"), asked.Selection);
        Assert.Empty(errors);
        Assert.False(File.Exists(Path.Combine(repo, ".github", "agents", "copilot.md")));
        Assert.Equal(pinBefore, await File.ReadAllTextAsync(Path.Combine(repo, ".agentcontrol.json"), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that, once the user confirmed, the other files are applied and their identity is recorded
    ///     in the pin, which still names no source.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_ApplySelectedPackage_GitVersionWithOtherFilesAccepted_AppliesAndRecordsIdentity()
    {
        // Arrange
        var (repo, card, provider) = CreateApplyingCard();
        provider.Commit = OtherCommit;
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        await card.ApplySelectedPackageAsync(
            new PackageReference("Repo", "contoso-agents", "1.2.3"),
            TestContext.Current.CancellationToken,
            acceptContentChange: true);

        // Assert
        Assert.Empty(errors);
        Assert.Equal("content of 1234567", await File.ReadAllTextAsync(Path.Combine(repo, ".github", "agents", "copilot.md"), TestContext.Current.CancellationToken));
        Assert.Equal(CommitReader.IdentityOf(OtherCommit), RepoPinStore.Load(repo)?.ContentIdentity);
        Assert.DoesNotContain("source", await File.ReadAllTextAsync(Path.Combine(repo, ".agentcontrol.json"), TestContext.Current.CancellationToken), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Repo", card.SupplyingSource);
    }

    /// <summary>
    ///     Test that when nobody can be asked, the refusal says how to accept the change - through the
    ///     card menu's "Change package..." - and no longer tells the user to edit the pin file.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_ApplySelectedPackage_GitVersionWithOtherFilesWithoutSubscriber_AdvisesChangePackage()
    {
        // Arrange
        var (_, card, provider) = CreateApplyingCard();
        provider.Commit = OtherCommit;
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        await card.ApplySelectedPackageAsync(
            new PackageReference("Repo", "contoso-agents", "1.2.3"), TestContext.Current.CancellationToken);

        // Assert
        var message = Assert.Single(errors);
        Assert.Contains("Change package...", message, StringComparison.Ordinal);
        Assert.DoesNotContain(".agentcontrol.json", message, StringComparison.Ordinal);
        Assert.DoesNotContain("delete", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var path in _tempPaths.Where(Directory.Exists))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; leftover temp folders do not fail the test.
            }
        }
    }

    /// <summary>
    ///     Creates the Git source of a test.
    /// </summary>
    /// <returns>The definition.</returns>
    private static PackageSourceDefinition GitSource() => new()
    {
        Alias = "Repo",
        Type = PackageSourceType.Git,
        Location = "https://git.example.com/org/agents.git",
        TagPattern = "release/{version}",
        PackageIds = ["contoso-agents"]
    };

    /// <summary>
    ///     Creates a zip source with the same alias.
    /// </summary>
    /// <returns>The definition.</returns>
    private static PackageSourceDefinition ZipSource() => new()
    {
        Alias = "Repo",
        Type = PackageSourceType.Zip,
        Location = Path.Combine(Path.GetTempPath(), "agentcontrol_git_card_zips")
    };

    /// <summary>
    ///     Creates a card for a repo pinned to <c>contoso-agents 1.2.3</c> whose package a source supplies,
    ///     with a fake provider serving that version.
    /// </summary>
    /// <param name="source">The source that supplies it; the Git source when <see langword="null"/>.</param>
    /// <returns>The repo folder, the card (its pin read) and the fake provider.</returns>
    private (string Repo, RepoCardViewModel Card, FakePackageSourceProvider Provider) CreatePinnedCard(
        PackageSourceDefinition? source = null) =>
        CreatePinnedCard(out _, source);

    /// <summary>
    ///     Creates a card for a repo pinned to <c>contoso-agents 1.2.3</c> whose package a source supplies,
    ///     with a fake provider serving that version.
    /// </summary>
    /// <param name="settings">Receives the settings the card reads, so a test can edit the sources.</param>
    /// <param name="source">The source that supplies it; the Git source when <see langword="null"/>.</param>
    /// <returns>The repo folder, the card (its pin read) and the fake provider.</returns>
    private (string Repo, RepoCardViewModel Card, FakePackageSourceProvider Provider) CreatePinnedCard(
        out AppSettings settings, PackageSourceDefinition? source = null)
    {
        var definition = source ?? GitSource();
        var repo = CreateTempDirectory();
        RepoPinStore.Save(repo, new RepoPin { PackageName = "contoso-agents", Version = "1.2.3" });
        var provider = new FakePackageSourceProvider("Repo", ("contoso-agents", "1.2.3"));
        var current = new AppSettings { Sources = [definition] };
        settings = current;
        var card = new RepoCardViewModel(
            new RecentRepo
            {
                Path = repo,
                PinnedPackageName = "contoso-agents",
                PinnedPackageVersion = "1.2.3",
                PinnedSource = "Repo"
            },
            () => current,
            new PackageVersionCache(),
            registryFactory: _ => new PackageSourceRegistry(current.Sources, definition => provider));
        card.RefreshCheap();
        return (repo, card, provider);
    }

    /// <summary>
    ///     Creates a card for a repo pinned to <c>contoso-agents 1.2.3</c> as delivered by the first commit
    ///     of a Git source whose provider can deliver the package content of its current commit.
    /// </summary>
    /// <returns>The repo folder, the card and the provider, whose <c>Commit</c> can be changed.</returns>
    private (string Repo, RepoCardViewModel Card, DeliveringGitProvider Provider) CreateApplyingCard()
    {
        var repo = CreateTempDirectory();
        RepoPinStore.Save(repo, new RepoPin
        {
            PackageName = "contoso-agents",
            Version = "1.2.3",
            ContentIdentity = CommitReader.IdentityOf(PinnedCommit)
        });
        var provider = new DeliveringGitProvider { Commit = PinnedCommit };
        var settings = new AppSettings { Sources = [GitSource()] };
        var registry = new PackageSourceRegistry(settings.Sources, _ => provider);
        var card = new RepoCardViewModel(
            new RecentRepo { Path = repo }, () => settings, new PackageVersionCache(), registryFactory: _ => registry);
        card.RefreshCheap();
        return (repo, card, provider);
    }

    /// <summary>
    ///     Creates a unique temporary directory removed by <see cref="Dispose"/>.
    /// </summary>
    /// <returns>The folder.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_git_card_test_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }

    /// <summary>
    ///     A Git-like source that serves version 1.2.3 at a changeable commit and delivers a package whose
    ///     content and identity name that commit.
    /// </summary>
    private sealed class DeliveringGitProvider : IPackageSourceProvider
    {
        /// <summary>
        ///     Gets or sets the commit the tag currently points to.
        /// </summary>
        public string Commit { get; set; } = string.Empty;

        /// <inheritdoc />
        public string Alias => "Repo";

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(["contoso-agents"]);

        /// <inheritdoc />
        public Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(string packageId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DiscoveredPackage>>(
                [new DiscoveredPackage("contoso-agents", new PackageVersion(1, 2, 3), "release/1.2.3", "Repo", Commit)]);

        /// <inheritdoc />
        public Task<IPackageContentReader> AcquireAsync(DiscoveredPackage package, CancellationToken cancellationToken) =>
            Task.FromResult<IPackageContentReader>(new CommitReader(Commit));
    }

    /// <summary>
    ///     A reader over a fixed set of files whose content names the commit it stands for; its content
    ///     identity is a (synthetic) files identity that differs from commit to commit.
    /// </summary>
    /// <param name="commit">The commit.</param>
    private sealed class CommitReader(string commit) : IPackageContentReader
    {
        /// <summary>
        ///     Gets the files identity this reader reports for a commit.
        /// </summary>
        /// <param name="commit">The commit.</param>
        /// <returns>A well-formed files identity that is different for every commit.</returns>
        public static string IdentityOf(string commit) =>
            "files-sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(commit)));

        /// <inheritdoc />
        public string Description => "commit " + commit;

        /// <inheritdoc />
        public string? ContentIdentity => IdentityOf(commit);

        /// <inheritdoc />
        public IReadOnlyList<string> ListFiles() =>
            [".github/agents/copilot.md", ".github/standards/style.md", ".github/templates/template.md", ".github/skills/skill.md"];

        /// <inheritdoc />
        public Stream OpenRead(string relativePath) =>
            new MemoryStream(Encoding.UTF8.GetBytes(
                relativePath.EndsWith("copilot.md", StringComparison.Ordinal) ? "content of " + commit[..7] : "other"));

        /// <inheritdoc />
        public string? ReadText(string relativePath) => null;

        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing to release.
        }
    }
}
