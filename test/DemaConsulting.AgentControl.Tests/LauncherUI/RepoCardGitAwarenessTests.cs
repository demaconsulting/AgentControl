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
///     Tests of how <see cref="RepoCardViewModel"/> shows and treats a repo pinned to a Git source:
///     the Git icon and the tag and commit in the chip tooltip, the moved-tag warning (shown, never
///     applied), a Git that is too old, "Change package..." on a pinned repo and the confirmation
///     when the same version now delivers a different commit.
/// </summary>
/// <remarks>
///     The Git source is a fake provider behind the card's registry, so no git or network is involved;
///     the repo folders are real temporary folders.
/// </remarks>
public sealed class RepoCardGitAwarenessTests : IDisposable
{
    /// <summary>
    ///     The commit the repo was set up from.
    /// </summary>
    private const string PinnedCommit = "abcdef0123456789abcdef0123456789abcdef01";

    /// <summary>
    ///     The content identity a Git pin records for <see cref="PinnedCommit"/>.
    /// </summary>
    private const string PinnedIdentity = "git-commit:" + PinnedCommit;

    /// <summary>
    ///     Another commit, for a tag that moved.
    /// </summary>
    private const string OtherCommit = "1234567890abcdef1234567890abcdef12345678";

    /// <summary>
    ///     The folders to delete after a test.
    /// </summary>
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that a repo pinned to a Git source shows the Git icon and the source's alias on its chip.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_PinnedGitSource_ShowsGitIconAndAlias()
    {
        // Arrange / Act
        var (_, card, _) = CreatePinnedCard();

        // Assert
        Assert.Equal(MaterialIconKind.Git, card.PinnedSourceIcon);
        Assert.Equal("Repo", card.PinnedSourceLabel);
        Assert.Equal("contoso-agents 1.2.3", card.PackageChipText);
    }

    /// <summary>
    ///     Test that the chip tooltip of a Git pin names the tag and the first seven characters of the
    ///     commit the files came from.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_PackageChipTooltip_GitPinWithCommit_NamesTagAndShortCommit()
    {
        // Arrange / Act
        var (_, card, _) = CreatePinnedCard();

        // Assert
        Assert.Equal("release/1.2.3", card.PinnedTagName);
        Assert.Equal("abcdef0", card.PinnedCommitShort);
        Assert.Equal(
            "Agent files: contoso-agents 1.2.3 from source 'Repo' (git repository), tag release/1.2.3 @ abcdef0.",
            card.PackageChipTooltip);
    }

    /// <summary>
    ///     Test that the chip tooltip of a Git pin that recorded no commit (made before commits were
    ///     recorded) names the tag only.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_PackageChipTooltip_GitPinWithoutIdentity_NamesTagOnly()
    {
        // Arrange / Act
        var (_, card, _) = CreatePinnedCard(identity: null);

        // Assert
        Assert.Null(card.PinnedCommitShort);
        Assert.Equal(
            "Agent files: contoso-agents 1.2.3 from source 'Repo' (git repository), tag release/1.2.3.",
            card.PackageChipTooltip);
    }

    /// <summary>
    ///     Test that the chip tooltip of a zip pin has neither a tag nor a commit.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_PackageChipTooltip_ZipPin_HasNoTagOrCommit()
    {
        // Arrange / Act
        var (_, card, _) = CreatePinnedCard(source: ZipSource(), identity: "sha256:" + new string('a', 64));

        // Assert
        Assert.Null(card.PinnedTagName);
        Assert.Null(card.PinnedCommitShort);
        Assert.Equal(
            "Agent files: contoso-agents 1.2.3 from source 'Repo' (zip folder).",
            card.PackageChipTooltip);
    }

    /// <summary>
    ///     Test that when the tag of the pinned version now points to another commit the card shows a
    ///     "Tag moved" status that outranks an available upgrade and hides the up-to-date cue, with a
    ///     tooltip naming the tag, both commits and "Change package...".
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_RefreshUpgradeStatus_TagMoved_ShowsTagMovedStatusAndTooltip()
    {
        // Arrange: the listing shows the pinned version at another commit, and a newer version too
        var (_, card, provider) = CreatePinnedCard(newerVersion: "1.3.0");
        provider.Revisions["1.2.3"] = OtherCommit;
        provider.Revisions["1.3.0"] = PinnedCommit;

        // Act
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert: moved wins over "upgrade available"
        Assert.True(card.IsPinnedTagMoved);
        Assert.True(card.IsUpgradeAvailable);
        Assert.Equal(RepoCardStatus.TagMoved, card.Status);
        Assert.Equal("Tag moved", card.StatusText);
        Assert.False(card.ShowUpToDateCue);
        Assert.Equal(card.PinnedTagMovedTooltip, card.StatusTooltip);
        Assert.Contains("release/1.2.3", card.PinnedTagMovedTooltip, StringComparison.Ordinal);
        Assert.Contains("abcdef0 -> 1234567", card.PinnedTagMovedTooltip, StringComparison.Ordinal);
        Assert.Contains("Change package...", card.PinnedTagMovedTooltip, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a moved tag is only shown, never applied: the repo's files and pin are untouched and
    ///     no package is acquired.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_RefreshUpgradeStatus_TagMoved_AppliesNothing()
    {
        // Arrange
        var (repo, card, provider) = CreatePinnedCard();
        provider.Revisions["1.2.3"] = OtherCommit;
        var markerPath = Path.Combine(repo, ".github", "agents", "copilot.md");
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        await File.WriteAllTextAsync(markerPath, "mine", TestContext.Current.CancellationToken);
        var pinBefore = await File.ReadAllTextAsync(Path.Combine(repo, ".agentcontrol.json"), TestContext.Current.CancellationToken);

        // Act
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(card.IsPinnedTagMoved);
        Assert.Equal(0, provider.AcquireCalls);
        Assert.Equal("mine", await File.ReadAllTextAsync(markerPath, TestContext.Current.CancellationToken));
        Assert.Equal(pinBefore, await File.ReadAllTextAsync(Path.Combine(repo, ".agentcontrol.json"), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a tag that still points to the recorded commit is not reported as moved.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_RefreshUpgradeStatus_SameCommit_IsNotMoved()
    {
        // Arrange
        var (_, card, provider) = CreatePinnedCard();
        provider.Revisions["1.2.3"] = PinnedCommit;

        // Act
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(card.IsPinnedTagMoved);
        Assert.Equal(string.Empty, card.PinnedTagMovedTooltip);
        Assert.Equal(RepoCardStatus.UpToDate, card.Status);
        Assert.True(card.ShowUpToDateCue);
    }

    /// <summary>
    ///     Test that a source that is not a Git source never reports a moved tag, even if its listing
    ///     carries a revision.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_RefreshUpgradeStatus_ZipSourceWithRevision_IsNotMoved()
    {
        // Arrange
        var (_, card, provider) = CreatePinnedCard(source: ZipSource(), identity: "sha256:" + new string('a', 64));
        provider.Revisions["1.2.3"] = OtherCommit;

        // Act
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(card.IsPinnedTagMoved);
        Assert.Equal(RepoCardStatus.UpToDate, card.Status);
    }

    /// <summary>
    ///     Test that a Git pin that recorded no commit has nothing to compare with, so it is not reported
    ///     as moved (it adopts a commit the next time a package is applied).
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_RefreshUpgradeStatus_PinWithoutIdentity_IsNotMoved()
    {
        // Arrange
        var (_, card, provider) = CreatePinnedCard(identity: null);
        provider.Revisions["1.2.3"] = OtherCommit;

        // Act
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(card.IsPinnedTagMoved);
        Assert.Equal(RepoCardStatus.UpToDate, card.Status);
    }

    /// <summary>
    ///     Test that the moved-tag state is cleared when the pin changes (here: the user accepted the new
    ///     commit, so the pin now records it).
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_RefreshUpgradeStatus_PinUpdatedAfterTagMoved_ClearsMovedTag()
    {
        // Arrange: a moved tag is shown
        var (repo, card, provider) = CreatePinnedCard();
        provider.Revisions["1.2.3"] = OtherCommit;
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);
        Assert.True(card.IsPinnedTagMoved);

        // Act: the pin now records the new commit and the card re-reads it
        RepoPinStore.Save(repo, new RepoPin
        {
            Source = "Repo",
            PackageName = "contoso-agents",
            Version = "1.2.3",
            ContentIdentity = PackageContentIdentity.FromGitCommit(OtherCommit)
        });
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(card.IsPinnedTagMoved);
        Assert.Equal("1234567", card.PinnedCommitShort);
        Assert.Equal(RepoCardStatus.UpToDate, card.Status);
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
    ///     Test that choosing the pinned version again when it now delivers a different commit asks for
    ///     confirmation and changes nothing until the user answers.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_ApplySelectedPackage_GitVersionAtNewCommit_AsksAndChangesNothing()
    {
        // Arrange: the repo was set up from the first commit; the tag now points to another one
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
    ///     Test that, once the user confirmed, the new commit's content is applied and its commit is
    ///     recorded in the pin (which also clears a moved-tag warning).
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_ApplySelectedPackage_GitVersionAtNewCommitAccepted_AppliesAndRecordsCommit()
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
        Assert.Equal(PackageContentIdentity.FromGitCommit(OtherCommit), RepoPinStore.Load(repo)?.ContentIdentity);
        Assert.Equal("1234567", card.PinnedCommitShort);
        Assert.False(card.IsPinnedTagMoved);
    }

    /// <summary>
    ///     Test that when nobody can be asked, the refusal says how to accept the change - through the
    ///     card menu's "Change package..." - and no longer tells the user to edit the pin file.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_ApplySelectedPackage_GitVersionAtNewCommitWithoutSubscriber_AdvisesChangePackage()
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
    ///     Creates a card for a repo pinned to <c>contoso-agents 1.2.3</c> of a source, with a fake
    ///     provider serving that version.
    /// </summary>
    /// <param name="source">The source the pin names; the Git source when <see langword="null"/>.</param>
    /// <param name="identity">The content identity the pin records; the commit identity by default,
    ///     <see langword="null"/> for a pin that recorded none.</param>
    /// <param name="newerVersion">A second version the provider serves, or <see langword="null"/>.</param>
    /// <returns>The repo folder, the card (its pin read) and the fake provider.</returns>
    private (string Repo, RepoCardViewModel Card, FakePackageSourceProvider Provider) CreatePinnedCard(
        PackageSourceDefinition? source = null, string? identity = PinnedIdentity, string? newerVersion = null)
    {
        var definition = source ?? GitSource();
        var repo = CreateTempDirectory();
        RepoPinStore.Save(repo, new RepoPin
        {
            Source = "Repo",
            PackageName = "contoso-agents",
            Version = "1.2.3",
            ContentIdentity = identity
        });
        var provider = newerVersion is null
            ? new FakePackageSourceProvider("Repo", ("contoso-agents", "1.2.3"))
            : new FakePackageSourceProvider("Repo", ("contoso-agents", "1.2.3"), ("contoso-agents", newerVersion));
        var settings = new AppSettings { Sources = [definition] };
        var registry = new PackageSourceRegistry(settings.Sources, _ => provider);
        var card = new RepoCardViewModel(
            new RecentRepo { Path = repo }, () => settings, new PackageVersionCache(), registryFactory: _ => registry);
        card.RefreshCheap();
        return (repo, card, provider);
    }

    /// <summary>
    ///     Creates a card for a repo pinned to <c>contoso-agents 1.2.3</c> at the first commit of a Git
    ///     source whose provider can deliver the package content of its current commit.
    /// </summary>
    /// <returns>The repo folder, the card and the provider, whose <c>Commit</c> can be changed.</returns>
    private (string Repo, RepoCardViewModel Card, DeliveringGitProvider Provider) CreateApplyingCard()
    {
        var repo = CreateTempDirectory();
        RepoPinStore.Save(repo, new RepoPin
        {
            Source = "Repo",
            PackageName = "contoso-agents",
            Version = "1.2.3",
            ContentIdentity = PackageContentIdentity.FromGitCommit(PinnedCommit)
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
    ///     A reader over a fixed set of files whose content names the commit it stands for.
    /// </summary>
    /// <param name="commit">The commit.</param>
    private sealed class CommitReader(string commit) : IPackageContentReader
    {
        /// <inheritdoc />
        public string Description => "commit " + commit;

        /// <inheritdoc />
        public string? ContentIdentity => PackageContentIdentity.FromGitCommit(commit);

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
