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
using DemaConsulting.AgentControl.RepoConfig;
using DemaConsulting.AgentControl.RepoSync;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Tests.AgentPackageManagement;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Tests of how <see cref="RepoCardViewModel"/> applies a package (Select Package, Change
///     package and Upgrade) to a repo whose managed agent folders are tracked by git: it asks first,
///     changes nothing when declined, and otherwise stops git tracking exactly the tracked folders
///     before the package replaces them.
/// </summary>
/// <remarks>
///     Uses a real git repository (skipped where git is not installed) so that what git tracks
///     before and after is observed, not assumed, and a stub package source that counts how often a
///     package is acquired, so "before anything is downloaded" is checked directly. No Avalonia
///     window is involved: the question is answered by a handler subscribed to the card's event.
///     Runs in the <c>RealProcess</c> collection because the card spawns real git processes.
/// </remarks>
[Collection("RealProcess")]
public sealed class RepoCardCommittedFilesTests : IDisposable
{
    private readonly List<GitRepositoryFixture> _fixtures = [];

    /// <summary>
    ///     Test that Select Package on a repo with tracked agent folders asks the user before any
    ///     package is acquired, naming the repo, exactly the tracked folders and the package, and
    ///     keeps the card busy while the question is open.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_TrackedAgentFolders_AsksBeforeAcquiringAnything()
    {
        // Arrange: a repo that tracks agents and standards (but not templates or skills)
        var (_, card, provider) = CreateTrackedRepo();
        var acquiredWhenAsked = -1;
        var busyWhenAsked = false;
        var requests = new List<CommittedFilesConversionRequest>();
        card.CommittedFilesConversionRequested += (_, request) =>
        {
            acquiredWhenAsked = provider.AcquireCalls;
            busyWhenAsked = card.IsBusy;
            requests.Add(request);
            request.Respond(false);
        };

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert: one question, asked before any acquire, while the card was busy
        var asked = Assert.Single(requests);
        Assert.Equal(0, acquiredWhenAsked);
        Assert.True(busyWhenAsked);
        Assert.Contains($"'{card.RepoName}'", asked.Message, StringComparison.Ordinal);
        Assert.Contains("in .github/agents, .github/standards.", asked.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(".github/templates", asked.Message, StringComparison.Ordinal);
        Assert.Contains("contoso-agents 1.0.0", asked.Message, StringComparison.Ordinal);
        Assert.Contains("Commit the result afterwards", asked.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that declining the question changes nothing at all: no package is acquired, git still
    ///     tracks the same files, the files on disk and the pin file are as they were, and the user is
    ///     told it was canceled.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_TrackedAgentFoldersDeclined_ChangesNothing()
    {
        // Arrange
        var (fixture, card, provider) = CreateTrackedRepo();
        var indexBefore = IndexedFiles(fixture);
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);
        card.CommittedFilesConversionRequested += (_, request) => request.Respond(false);

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert: nothing acquired, nothing changed in git, on disk or in the pin, and no error
        Assert.Equal(0, provider.AcquireCalls);
        Assert.Equal(indexBefore, IndexedFiles(fixture));
        Assert.Equal("old agent", File.ReadAllText(Path.Combine(fixture.WorkPath, ".github", "agents", "old.md")));
        Assert.False(File.Exists(Path.Combine(fixture.WorkPath, ".agentcontrol.json")));
        Assert.False(File.Exists(Path.Combine(fixture.WorkPath, ".gitignore")));
        Assert.Empty(errors);
        Assert.False(card.IsBusy);
        Assert.Contains("canceled", card.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("nothing was changed", card.StatusMessage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that accepting the question stops git tracking exactly the tracked managed folders
    ///     (not other tracked paths), replaces their files with the package, writes the pin and
    ///     <c>.gitignore</c>, clears the committed-files badge and tells the user to commit.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_TrackedAgentFoldersAccepted_StopsTrackingThenAppliesPackageAndAsksToCommit()
    {
        // Arrange: a card that already shows the committed-files badge
        var (fixture, card, provider) = CreateTrackedRepo();
        card.RefreshCheap();
        Assert.True(card.HasCommittedAgentFiles);
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);
        card.CommittedFilesConversionRequested += (_, request) => request.Respond(true);

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert: git no longer tracks the managed folders, but still tracks everything else
        Assert.Empty(errors);
        Assert.Equal(1, provider.AcquireCalls);
        Assert.Equal([".github/workflows/ci.yml", "README.md"], IndexedFiles(fixture));

        // Assert: the package replaced the old files, the pin and .gitignore were written
        var agents = Path.Combine(fixture.WorkPath, ".github", "agents");
        Assert.False(File.Exists(Path.Combine(agents, "old.md")));
        Assert.Equal("new agent", File.ReadAllText(Path.Combine(agents, "copilot.md")));
        Assert.Equal("contoso-agents", RepoPinStore.Load(fixture.WorkPath)?.PackageName);
        Assert.Equal("1.0.0", RepoPinStore.Load(fixture.WorkPath)?.Version);
        Assert.Contains(".github/agents", File.ReadAllText(Path.Combine(fixture.WorkPath, ".gitignore")), StringComparison.Ordinal);

        // Assert: the badge is cleared (git now reports nothing tracked) and the user is told to commit
        Assert.False(card.HasCommittedAgentFiles);
        Assert.False(card.IsBusy);
        Assert.Contains("Package selected: contoso-agents 1.0.0.", card.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("no longer tracked by git", card.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("commit the change", card.StatusMessage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that Upgrade goes through the same conversion: it asks, and once accepted stops git
    ///     tracking the folders and applies the newer version.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_UpgradeCommand_TrackedAgentFoldersAccepted_StopsTrackingThenUpgrades()
    {
        // Arrange: a repo pinned to 1.0.0 whose source also offers 2.0.0
        var (fixture, card, provider) = CreateTrackedRepo(versions: ["1.0.0", "2.0.0"]);
        RepoPinStore.Save(
            fixture.WorkPath, new RepoPin { PackageName = "contoso-agents", Version = "1.0.0" });
        card.RefreshCheap();
        var questions = new List<string>();
        card.CommittedFilesConversionRequested += (_, request) =>
        {
            questions.Add(request.Message);
            request.Respond(true);
        };

        // Act
        await card.UpgradeCommand.ExecuteAsync();

        // Assert: asked once about the newer version, then untracked and upgraded
        Assert.Contains("contoso-agents 2.0.0", Assert.Single(questions), StringComparison.Ordinal);
        Assert.Equal("2.0.0", RepoPinStore.Load(fixture.WorkPath)?.Version);
        Assert.Equal([".github/workflows/ci.yml", "README.md"], IndexedFiles(fixture));
        Assert.Contains("Upgraded to 2.0.0.", card.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("commit the change", card.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(1, provider.AcquireCalls);
    }

    /// <summary>
    ///     Test that a failure to stop git tracking the folders stops the apply before anything is
    ///     replaced: the old files stay, no pin is written, and the user is told why.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_UntrackFails_StopsBeforeExtractingAndChangesNothingElse()
    {
        // Arrange: an index lock held by "another git process" makes git rm fail
        var (fixture, card, _) = CreateTrackedRepo();
        var indexBefore = IndexedFiles(fixture);
        File.WriteAllText(Path.Combine(fixture.WorkPath, ".git", "index.lock"), string.Empty);
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);
        card.CommittedFilesConversionRequested += (_, request) => request.Respond(true);

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert: the error says nothing was changed and carries git's explanation
        var error = Assert.Single(errors);
        Assert.Contains("could not stop git tracking", error, StringComparison.Ordinal);
        Assert.Contains("nothing was changed", error, StringComparison.Ordinal);
        Assert.Contains("git rm --cached", error, StringComparison.Ordinal);

        // Assert: the files, the pin, .gitignore and the index are untouched
        File.Delete(Path.Combine(fixture.WorkPath, ".git", "index.lock"));
        var agents = Path.Combine(fixture.WorkPath, ".github", "agents");
        Assert.Equal("old agent", File.ReadAllText(Path.Combine(agents, "old.md")));
        Assert.False(File.Exists(Path.Combine(agents, "copilot.md")));
        Assert.False(File.Exists(Path.Combine(fixture.WorkPath, ".agentcontrol.json")));
        Assert.False(File.Exists(Path.Combine(fixture.WorkPath, ".gitignore")));
        Assert.Equal(indexBefore, IndexedFiles(fixture));
        Assert.False(card.IsBusy);
    }

    /// <summary>
    ///     Test that a package the extractor refuses after git has stopped tracking the folders puts
    ///     the folders back in git's index: a failed apply never leaves their removal staged, the old
    ///     files stay, and no pin is written.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_ExtractFailsAfterUntrack_RestoresGitTracking()
    {
        // Arrange: a tracked repo and a package with a path-escaping entry the extractor refuses
        var (fixture, card, provider) = CreateTrackedRepo();
        provider.Files = ["../escape.md"];
        var indexBefore = IndexedFiles(fixture);
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);
        card.CommittedFilesConversionRequested += (_, request) => request.Respond(true);

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert: the apply failed, git tracks exactly what it tracked before, nothing else changed
        Assert.Single(errors);
        Assert.Equal(indexBefore, IndexedFiles(fixture));
        Assert.Equal("old agent", File.ReadAllText(Path.Combine(fixture.WorkPath, ".github", "agents", "old.md")));
        Assert.False(File.Exists(Path.Combine(fixture.WorkPath, ".agentcontrol.json")));
        Assert.False(card.IsBusy);
    }

    /// <summary>
    ///     Test that a repo with no tracked managed files is applied exactly as before: no question,
    ///     no change to git, and the ordinary success message.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_NoTrackedAgentFiles_AppliesWithoutAskingOrTouchingGit()
    {
        // Arrange: a repo that tracks nothing under the managed folders
        var (fixture, card, provider) = CreateRepo(
            new Dictionary<string, string> { ["README.md"] = "readme", [".github/workflows/ci.yml"] = "ci" });
        var indexBefore = IndexedFiles(fixture);
        var requests = 0;
        card.CommittedFilesConversionRequested += (_, request) =>
        {
            requests++;
            request.Respond(true);
        };

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert: applied, nobody asked, the index untouched and the plain success message
        Assert.Equal(0, requests);
        Assert.Equal(1, provider.AcquireCalls);
        Assert.Equal(indexBefore, IndexedFiles(fixture));
        Assert.Equal("1.0.0", RepoPinStore.Load(fixture.WorkPath)?.Version);
        Assert.Equal("Package selected: contoso-agents 1.0.0.", card.StatusMessage);
    }

    /// <summary>
    ///     Test that the check is made with git at the moment of the apply and not taken from the
    ///     card's cached badge: files staged after the badge was computed (same commit) are noticed.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_BadgeStaleBecauseFilesStagedLater_ChecksGitFreshAndAsks()
    {
        // Arrange: a card that computed "no committed agent files" for the current commit, after
        // which an agent file is staged without committing (so the commit - the cache key - is the same)
        var (fixture, card, provider) = CreateRepo(new Dictionary<string, string> { ["README.md"] = "readme" });
        card.RefreshCheap();
        Assert.False(card.HasCommittedAgentFiles);
        Directory.CreateDirectory(Path.Combine(fixture.WorkPath, ".github", "skills"));
        File.WriteAllText(Path.Combine(fixture.WorkPath, ".github", "skills", "k.md"), "skill");
        fixture.Git(fixture.WorkPath, "add", "-f", "--", ".github/skills/k.md");
        var messages = new List<string>();
        card.CommittedFilesConversionRequested += (_, request) =>
        {
            messages.Add(request.Message);
            request.Respond(false);
        };

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert: asked about the skills folder although the badge was clear
        Assert.Contains("in .github/skills.", Assert.Single(messages), StringComparison.Ordinal);
        Assert.Equal(0, provider.AcquireCalls);
    }

    /// <summary>
    ///     Test that with nobody to ask, tracked agent folders are never converted: the apply stops
    ///     with an explanation before acquiring anything or changing git.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_TrackedAgentFoldersWithoutSubscriber_StopsAndChangesNothing()
    {
        // Arrange: nothing subscribes to the question
        var (fixture, card, provider) = CreateTrackedRepo();
        var indexBefore = IndexedFiles(fixture);
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("needs your confirmation", error, StringComparison.Ordinal);
        Assert.Contains("Nothing was changed", error, StringComparison.Ordinal);
        Assert.Equal(0, provider.AcquireCalls);
        Assert.Equal(indexBefore, IndexedFiles(fixture));
        Assert.False(File.Exists(Path.Combine(fixture.WorkPath, ".agentcontrol.json")));
    }

    /// <summary>
    ///     Test that when the apply cannot ask git at all (the folder is not a git repository) the
    ///     package is applied as it always was, without a question.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_GitCannotAnswer_AppliesAsBefore()
    {
        // Arrange: a plain folder that is not a git repository
        GitRepositoryFixture.SkipWithoutGit();
        var folder = Path.Combine(Path.GetTempPath(), "agentcontrol_not_a_repo_" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(folder);
        try
        {
            var provider = new CountingProvider(["1.0.0"]);
            var card = CreateCard(folder, provider);
            var requests = 0;
            card.CommittedFilesConversionRequested += (_, _) => requests++;

            // Act
            card.ApplySelectedPackage("contoso-agents", "1.0.0");

            // Assert
            Assert.Equal(0, requests);
            Assert.Equal(1, provider.AcquireCalls);
            Assert.Equal("1.0.0", RepoPinStore.Load(folder)?.Version);
        }
        finally
        {
            GitSourceProvider.DeleteDirectory(folder);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var fixture in _fixtures)
        {
            fixture.Dispose();
        }
    }

    /// <summary>
    ///     Creates a real repo that tracks the agents and standards folders (and unrelated files) and
    ///     has templates and skills folders only as untracked files on disk.
    /// </summary>
    /// <param name="versions">The versions the stub source offers.</param>
    /// <returns>The fixture, a card over its working repository and the stub source.</returns>
    private (GitRepositoryFixture Fixture, RepoCardViewModel Card, CountingProvider Provider) CreateTrackedRepo(
        string[]? versions = null)
    {
        var result = CreateRepo(
            new Dictionary<string, string>
            {
                [".github/agents/old.md"] = "old agent",
                [".github/standards/old.md"] = "old standard",
                [".github/workflows/ci.yml"] = "ci",
                ["README.md"] = "readme"
            },
            versions);

        // Present on disk but not tracked: only the tracked folders are to be reported and untracked
        Directory.CreateDirectory(Path.Combine(result.Fixture.WorkPath, ".github", "templates"));
        File.WriteAllText(Path.Combine(result.Fixture.WorkPath, ".github", "templates", "old.md"), "old template");
        return result;
    }

    /// <summary>
    ///     Creates a real repo with the given committed files and a card over it.
    /// </summary>
    /// <param name="files">The files to commit.</param>
    /// <param name="versions">The versions the stub source offers.</param>
    /// <returns>The fixture, a card over its working repository and the stub source.</returns>
    private (GitRepositoryFixture Fixture, RepoCardViewModel Card, CountingProvider Provider) CreateRepo(
        Dictionary<string, string> files, string[]? versions = null)
    {
        var fixture = GitRepositoryFixture.CreateOrSkip();
        _fixtures.Add(fixture);
        fixture.Commit(files, "Initial commit");
        var provider = new CountingProvider(versions ?? ["1.0.0"]);
        return (fixture, CreateCard(fixture.WorkPath, provider), provider);
    }

    /// <summary>
    ///     Creates a card over a repo folder whose only package source is the stub source.
    /// </summary>
    /// <param name="repo">The repo folder.</param>
    /// <param name="provider">The stub source.</param>
    /// <returns>The card.</returns>
    private static RepoCardViewModel CreateCard(string repo, CountingProvider provider)
    {
        var settings = new AppSettings
        {
            Sources = [new PackageSourceDefinition { Alias = "Default", Type = PackageSourceType.Zip, Location = "unused" }]
        };
        var registry = new PackageSourceRegistry(settings.Sources, _ => provider);
        return new RepoCardViewModel(
            new RecentRepo { Path = repo }, () => settings, new PackageVersionCache(), null, _ => registry);
    }

    /// <summary>
    ///     Lists the files git currently tracks (its index), in git's order.
    /// </summary>
    /// <param name="fixture">The repo's fixture.</param>
    /// <returns>The tracked paths.</returns>
    private static List<string> IndexedFiles(GitRepositoryFixture fixture) =>
        [.. fixture.Git(fixture.WorkPath, "ls-files").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim())];

    /// <summary>
    ///     A package source that serves <c>contoso-agents</c> in the given versions, counts how often a
    ///     package is acquired and delivers one file in each managed folder.
    /// </summary>
    private sealed class CountingProvider : IPackageSourceProvider
    {
        private readonly string[] _versions;

        /// <summary>
        ///     Initializes the source.
        /// </summary>
        /// <param name="versions">The versions it offers.</param>
        /// <param name="files">The files its packages list, or <see langword="null"/> for one in each
        ///     managed folder.</param>
        public CountingProvider(string[] versions, string[]? files = null)
        {
            _versions = versions;
            Files = files;
        }

        /// <summary>
        ///     Gets or sets the files its packages list, or <see langword="null"/> for one in each
        ///     managed folder.
        /// </summary>
        public string[]? Files { get; set; }

        /// <summary>
        ///     Gets how many times a package was acquired.
        /// </summary>
        public int AcquireCalls { get; private set; }

        /// <inheritdoc />
        public string Alias => "Default";

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(["contoso-agents"]);

        /// <inheritdoc />
        public Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(
            string packageId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DiscoveredPackage>>(
                [.. _versions.Select(version => new DiscoveredPackage(
                    "contoso-agents", ParseVersion(version), version, "Default"))]);

        /// <inheritdoc />
        public Task<IPackageContentReader> AcquireAsync(DiscoveredPackage package, CancellationToken cancellationToken)
        {
            AcquireCalls++;
            return Task.FromResult<IPackageContentReader>(new FixedReader(Files));
        }

        /// <summary>
        ///     Parses a version the tests wrote.
        /// </summary>
        /// <param name="version">The version text.</param>
        /// <returns>The version.</returns>
        private static PackageVersion ParseVersion(string version) =>
            PackageVersion.TryParse(version, out var parsed) && parsed is not null
                ? parsed
                : throw new ArgumentException($"'{version}' is not a version.", nameof(version));
    }

    /// <summary>
    ///     A reader over one file in each managed folder.
    /// </summary>
    private sealed class FixedReader : IPackageContentReader
    {
        private static readonly string[] ManagedFiles =
        [
            ".github/agents/copilot.md",
            ".github/standards/style.md",
            ".github/templates/template.md",
            ".github/skills/skill.md"
        ];

        private readonly string[] _files;

        /// <summary>
        ///     Initializes the reader.
        /// </summary>
        /// <param name="files">The files it lists, or <see langword="null"/> for one in each managed folder.</param>
        public FixedReader(string[]? files = null) => _files = files ?? ManagedFiles;

        /// <inheritdoc />
        public string Description => "stub package";

        /// <inheritdoc />
        public IReadOnlyList<string> ListFiles() => _files;

        /// <inheritdoc />
        public Stream OpenRead(string relativePath) =>
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                relativePath.EndsWith("copilot.md", StringComparison.Ordinal) ? "new agent" : "other"));

        /// <inheritdoc />
        public string? ReadText(string relativePath) => null;

        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing to release.
        }
    }
}
