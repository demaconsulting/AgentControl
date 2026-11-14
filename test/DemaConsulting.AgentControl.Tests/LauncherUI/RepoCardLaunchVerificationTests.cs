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

using System.IO.Compression;
using System.Text;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.RepoConfig;
using DemaConsulting.AgentControl.RepoSync;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Tests.AgentPackageManagement;
using DemaConsulting.AgentControl.Tests.AgentToolLauncher;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Tests of the verification <see cref="RepoCardViewModel"/> performs before a launch: the agent files
///     physically on disk are compared, by the files identity the pin recorded, with the pinned package; no
///     files at all are restored silently; a difference asks the user to keep or update the files; a restore
///     that is needed but fails asks whether to launch anyway; and an in-sync repo never touches a package
///     source.
/// </summary>
/// <remarks>
///     Uses real repo folders and real zip, NuGet and Git packages, and records every request for a package
///     source registry (<see cref="_registryRequests"/>) so a test can prove that no package source was
///     touched. The user's answers are scripted by answering each raised
///     <see cref="AgentFilesChoiceRequest"/> from a queue; no window is opened. The "launch" tests inject a fake
///     process starter and a deterministic shell detector, so no process is ever started.
/// </remarks>
public sealed class RepoCardLaunchVerificationTests : IDisposable
{
    /// <summary>
    ///     The package every test uses.
    /// </summary>
    private const string PackageId = "contoso-agents";

    /// <summary>
    ///     The folders to delete after a test.
    /// </summary>
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     How many times a card asked for a package source registry (the only way to reach a package
    ///     source) since the counter was last reset.
    /// </summary>
    private int _registryRequests;

    /// <summary>
    ///     Records every process the cards of this test ask to start; no process is ever started.
    /// </summary>
    private readonly FakeAgentToolProcessStarter _starter = new();

    /// <summary>
    ///     Test that a pinned repo without any managed folder gets the pinned package silently: no question,
    ///     no message, and the pin is left as it was.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_NoManagedFolders_RestoresSilentlyWithoutPrompt()
    {
        // Arrange: a repo that was applied from the package and then lost its agent folders
        var (repo, settings, pinBefore) = CreatePinnedRepo("v1");
        Directory.Delete(Path.Combine(repo, ".github"), recursive: true);
        var card = CreateCard(repo, settings);
        var (requests, errors) = Watch(card);

        // Act
        var outcome = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert: restored without asking or warning, exactly the package's files, pin untouched
        Assert.Equal(AgentFilesSyncOutcome.Restored, outcome);
        Assert.Empty(requests);
        Assert.Empty(errors);
        AssertInstalledFilesAre(repo, "v1");
        Assert.Equal(pinBefore, await File.ReadAllTextAsync(PinPath(repo), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that managed folders that exist but hold no file are treated like missing ones: the package
    ///     is installed silently.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_EmptyManagedFolders_RestoresSilentlyWithoutPrompt()
    {
        // Arrange: all four folders present and empty (what the old folder-existence check called synced)
        var (repo, settings, _) = CreatePinnedRepo("v1");
        Directory.Delete(Path.Combine(repo, ".github"), recursive: true);
        foreach (var folder in new[] { "agents", "standards", "templates", "skills" })
        {
            Directory.CreateDirectory(Path.Combine(repo, ".github", folder));
        }

        Assert.True(PackageZipExtractor.AllManagedFoldersExist(repo));
        var card = CreateCard(repo, settings);
        var (requests, errors) = Watch(card);

        // Act
        var outcome = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentFilesSyncOutcome.Restored, outcome);
        Assert.Empty(requests);
        Assert.Empty(errors);
        AssertInstalledFilesAre(repo, "v1");
    }

    /// <summary>
    ///     Test that files that match the pin's recorded identity launch with no question, no message and no
    ///     access to a package source - not even a source that is not configured.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_FilesMatchPin_ReturnsInSyncWithoutPromptOrPackageAccess()
    {
        // Arrange: an untouched applied repo, and a machine with no package source configured at all
        var (repo, _, pinBefore) = CreatePinnedRepo("v1");
        var card = CreateCard(repo, new AppSettings());
        var (requests, errors) = Watch(card);
        _registryRequests = 0;

        // Act
        var outcome = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert: in sync, nothing asked or reported, no registry (so no source) was reached
        Assert.Equal(AgentFilesSyncOutcome.InSync, outcome);
        Assert.Empty(requests);
        Assert.Empty(errors);
        Assert.Equal(0, _registryRequests);
        Assert.Equal(pinBefore, await File.ReadAllTextAsync(PinPath(repo), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a repo applied from a NuGet package is recognized as in sync by its recorded identity,
    ///     without reaching any package source.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_AppliedFromNuGetFolderSource_IsInSyncWithoutPackageAccess()
    {
        // Arrange: a repo applied from a NuGet folder feed (the pin records the files identity)
        var feed = CreateTempDirectory();
        NuGetFeedBuilder.CreateAgentPackage(feed, PackageId, "1.0.0", marker: "v1");
        var repo = CreateTempDirectory();
        var nugetRoot = CreateTempDirectory();
        var settings = new AppSettings
        {
            Sources = [new PackageSourceDefinition { Alias = "Feed", Type = PackageSourceType.NuGet, Location = feed }]
        };
        var applyCard = CreateCard(
            repo, settings, current => PackageSourceRegistry.Create(current.GetEffectiveSources(), nugetRoot));
        await applyCard.ApplySelectedPackageAsync(
            new PackageReference("Feed", PackageId, "1.0.0"), TestContext.Current.CancellationToken);
        Assert.True(PackageContentIdentity.IsFilesIdentity(RepoPinStore.Load(repo)?.ContentIdentity));
        var card = CreateCard(repo, settings, ThrowingFactory);
        var (requests, errors) = Watch(card);
        _registryRequests = 0;

        // Act
        var outcome = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentFilesSyncOutcome.InSync, outcome);
        Assert.Empty(requests);
        Assert.Empty(errors);
        Assert.Equal(0, _registryRequests);
    }

    /// <summary>
    ///     Test that a repo applied from a zip package is recognized as in sync by its recorded identity,
    ///     even when reaching any package source would throw.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_AppliedFromZipSource_IsInSyncWithoutPackageAccess()
    {
        // Arrange: an untouched applied repo, on a machine whose package source access throws
        var (repo, settings, _) = CreatePinnedRepo("v1");
        var card = CreateCard(repo, settings, ThrowingFactory);
        var (requests, errors) = Watch(card);
        _registryRequests = 0;

        // Act
        var outcome = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentFilesSyncOutcome.InSync, outcome);
        Assert.Empty(requests);
        Assert.Empty(errors);
        Assert.Equal(0, _registryRequests);
    }

    /// <summary>
    ///     Test that a repo applied from a Git package is recognized as in sync by its recorded identity,
    ///     without reaching any package source.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_AppliedFromGitSource_IsInSyncWithoutPackageAccess()
    {
        // Arrange: the files committed and tagged in a real repository, and a repo applied from it
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var files = AgentFiles("v1").ToDictionary(file => file.Path, file => file.Text);
        files["release-notes.md"] = "notes of the tag";
        fixture.Commit(files, "one");
        fixture.Tag("v1.0.0", annotated: true);
        fixture.Push();
        var repo = CreateTempDirectory();
        var settings = new AppSettings { Sources = [fixture.CreateDefinition()] };
        var applyCard = CreateCard(
            repo, settings, current => PackageSourceRegistry.Create(current.GetEffectiveSources(), git: fixture.CreateOptions()));
        await applyCard.ApplySelectedPackageAsync(
            new PackageReference("Repo", PackageId, "1.0.0"), TestContext.Current.CancellationToken);
        Assert.True(PackageContentIdentity.IsFilesIdentity(RepoPinStore.Load(repo)?.ContentIdentity));
        var card = CreateCard(repo, settings, ThrowingFactory);
        var (requests, errors) = Watch(card);
        _registryRequests = 0;

        // Act
        var outcome = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentFilesSyncOutcome.InSync, outcome);
        Assert.Empty(requests);
        Assert.Empty(errors);
        Assert.Equal(0, _registryRequests);
    }

    /// <summary>
    ///     Test that a missing file is noticed and the user is asked; keeping leaves every file exactly as it
    ///     is and touches no package source. The question names the package and offers both choices.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_MissingFile_AsksAndKeepLeavesFilesUntouched()
    {
        // Arrange: an applied repo with one file deleted, and a user who will keep the current files
        var (repo, settings, _) = CreatePinnedRepo("v1");
        File.Delete(Path.Combine(repo, ".github", "standards", "style.md"));
        var before = Snapshot(repo);
        var card = CreateCard(repo, settings);
        var (requests, errors) = Watch(card, answers: [false]);
        _registryRequests = 0;

        // Act
        var outcome = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert: asked once with the right wording, files untouched, no package source reached
        Assert.Equal(AgentFilesSyncOutcome.KeptCurrentFiles, outcome);
        var request = Assert.Single(requests);
        Assert.Contains($"{PackageId} 1.0.0", request.Message, StringComparison.Ordinal);
        Assert.Contains("Update from package", request.Message, StringComparison.Ordinal);
        Assert.Contains("Keep current files", request.Message, StringComparison.Ordinal);
        Assert.Equal("Update from package", request.ConfirmText);
        Assert.Equal("Keep current files", request.DeclineText);
        Assert.Empty(errors);
        Assert.Equal(before, Snapshot(repo));
        Assert.Equal(0, _registryRequests);
    }

    /// <summary>
    ///     Regression test of the reported defect: a repo whose four folders exist but about half of whose
    ///     files are missing used to launch forever without a restore. It is now detected, and updating
    ///     restores every file of the package.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_HalfOfTheFilesMissing_AsksAndUpdateRestoresAllFiles()
    {
        // Arrange: two of the four files are gone but every managed folder still exists
        var (repo, settings, pinBefore) = CreatePinnedRepo("v1");
        File.Delete(Path.Combine(repo, ".github", "standards", "style.md"));
        File.Delete(Path.Combine(repo, ".github", "skills", "skill.md"));
        Assert.True(PackageZipExtractor.AllManagedFoldersExist(repo));
        var card = CreateCard(repo, settings);
        var (requests, errors) = Watch(card, answers: [true]);

        // Act
        var outcome = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert: asked, then every file of the package is back and the pin is unchanged
        Assert.Equal(AgentFilesSyncOutcome.Updated, outcome);
        Assert.Single(requests);
        Assert.Empty(errors);
        AssertInstalledFilesAre(repo, "v1");
        Assert.Equal(pinBefore, await File.ReadAllTextAsync(PinPath(repo), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a changed file is noticed, and updating replaces it with the package's file without
    ///     writing the pin.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_ChangedFile_UpdateReplacesFileAndKeepsPinUnchanged()
    {
        // Arrange: an applied repo with one file edited
        var (repo, settings, pinBefore) = CreatePinnedRepo("v1");
        await File.WriteAllTextAsync(
            Path.Combine(repo, ".github", "agents", "copilot.md"), "my experiment", TestContext.Current.CancellationToken);
        var card = CreateCard(repo, settings);
        var (requests, _) = Watch(card, answers: [true]);

        // Act
        var outcome = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentFilesSyncOutcome.Updated, outcome);
        Assert.Single(requests);
        AssertInstalledFilesAre(repo, "v1");
        Assert.Equal(pinBefore, await File.ReadAllTextAsync(PinPath(repo), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that an added file is noticed, and updating deletes it (the managed folders are replaced
    ///     wholesale, as the update is documented to do).
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_ExtraFile_UpdateDeletesExtraFile()
    {
        // Arrange: an applied repo plus a file of the user's own in a managed folder
        var (repo, settings, _) = CreatePinnedRepo("v1");
        var extra = Path.Combine(repo, ".github", "skills", "extra.md");
        await File.WriteAllTextAsync(extra, "mine", TestContext.Current.CancellationToken);
        var card = CreateCard(repo, settings);
        var (requests, _) = Watch(card, answers: [true]);

        // Act
        var outcome = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentFilesSyncOutcome.Updated, outcome);
        Assert.Single(requests);
        Assert.False(File.Exists(extra));
        AssertInstalledFilesAre(repo, "v1");
    }

    /// <summary>
    ///     Test that with nobody to ask (no window is listening) a mismatch keeps the files and never
    ///     touches a package source: files are only ever replaced on an explicit yes.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_MismatchWithoutSubscriber_KeepsFilesAndNeverTouchesPackageSource()
    {
        // Arrange: an applied repo with a file deleted, no subscriber and a throwing package source
        var (repo, settings, _) = CreatePinnedRepo("v1");
        File.Delete(Path.Combine(repo, ".github", "agents", "copilot.md"));
        var before = Snapshot(repo);
        var card = CreateCard(repo, settings, ThrowingFactory);
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);
        _registryRequests = 0;

        // Act
        var outcome = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentFilesSyncOutcome.KeptCurrentFiles, outcome);
        Assert.Empty(errors);
        Assert.Equal(before, Snapshot(repo));
        Assert.Equal(0, _registryRequests);
    }

    /// <summary>
    ///     Test that the question is asked on every launch while the difference persists: nothing is
    ///     remembered.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_MismatchPersists_AsksOnEveryLaunch()
    {
        // Arrange: a difference the user keeps
        var (repo, settings, _) = CreatePinnedRepo("v1");
        File.Delete(Path.Combine(repo, ".github", "templates", "template.md"));
        var card = CreateCard(repo, settings);
        var (requests, _) = Watch(card, answers: [false, false]);

        // Act
        var first = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);
        var second = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentFilesSyncOutcome.KeptCurrentFiles, first);
        Assert.Equal(AgentFilesSyncOutcome.KeptCurrentFiles, second);
        Assert.Equal(2, requests.Count);
    }

    /// <summary>
    ///     Test that choosing to update when the only source delivers other files under the pinned version
    ///     (a republished package) is refused with nothing extracted, and that the user is then offered to
    ///     launch anyway.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_MismatchAndUpdate_RepublishedPackageOnlySource_RefusesFilesUntouchedAndOffersLaunchAnyway()
    {
        // Arrange: a repo with an edited file, and a machine whose only source delivers other files
        var (repo, _, _) = CreatePinnedRepo("v1");
        await File.WriteAllTextAsync(
            Path.Combine(repo, ".github", "agents", "copilot.md"), "my experiment", TestContext.Current.CancellationToken);
        var before = Snapshot(repo);
        var settings = new AppSettings { Sources = [ZipSource("Artifactory", CreateSourceFolder("tampered"))] };
        var card = CreateCard(repo, settings);
        var (requests, _) = Watch(card, answers: [true, true]);

        // Act
        var outcome = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert: first the keep/update question, then the launch-anyway question explaining the refusal
        Assert.Equal(AgentFilesSyncOutcome.FailedLaunchAnyway, outcome);
        Assert.Equal(2, requests.Count);
        Assert.Contains("The agent files were not synced", requests[1].Message, StringComparison.Ordinal);
        Assert.Equal("Launch anyway", requests[1].ConfirmText);
        Assert.Equal("Cancel", requests[1].DeclineText);
        Assert.Equal(before, Snapshot(repo));
    }

    /// <summary>
    ///     Test that a managed folder that is a directory junction cannot be verified, so it is treated as a
    ///     difference: the user is asked (and keeping launches), never silently overwritten (Windows).
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_ManagedFolderIsJunction_TreatedAsMismatchAndAsks()
    {
        // Arrange: .github/skills is a junction to a folder that holds the right file
        var (repo, settings, _) = CreatePinnedRepo("v1");
        var outside = CreateTempDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(outside, "skill.md"), "skills content", TestContext.Current.CancellationToken);
        Directory.Delete(Path.Combine(repo, ".github", "skills"), recursive: true);
        CreateJunctionOrSkip(Path.Combine(repo, ".github", "skills"), outside);
        var card = CreateCard(repo, settings);
        var (requests, errors) = Watch(card, answers: [false]);

        // Act
        var outcome = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert: asked because the files could not be read, nothing was replaced
        Assert.Equal(AgentFilesSyncOutcome.KeptCurrentFiles, outcome);
        var request = Assert.Single(requests);
        Assert.Contains("could not be read", request.Message, StringComparison.Ordinal);
        Assert.Empty(errors);
        Assert.Equal("skills content", await File.ReadAllTextAsync(
            Path.Combine(outside, "skill.md"), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a managed folder that is a symbolic link cannot be verified, so it is treated as a
    ///     difference: the user is asked (and keeping launches), wherever the operating system lets a test
    ///     create a symbolic link.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_ManagedFolderIsSymlink_TreatedAsMismatchAndAsks()
    {
        // Arrange: .github/skills is a symbolic link to a folder that holds the right file
        var (repo, settings, _) = CreatePinnedRepo("v1");
        var outside = CreateTempDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(outside, "skill.md"), "skills content", TestContext.Current.CancellationToken);
        Directory.Delete(Path.Combine(repo, ".github", "skills"), recursive: true);
        CreateDirectorySymlinkOrSkip(Path.Combine(repo, ".github", "skills"), outside);
        var card = CreateCard(repo, settings);
        var (requests, errors) = Watch(card, answers: [false]);

        // Act
        var outcome = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentFilesSyncOutcome.KeptCurrentFiles, outcome);
        var request = Assert.Single(requests);
        Assert.Contains("could not be read", request.Message, StringComparison.Ordinal);
        Assert.Empty(errors);
    }

    /// <summary>
    ///     Test that a pin that recorded no files identity (an older pin) keeps the folder-presence rule:
    ///     all four folders holding files launches with no question and no package access. Documents the
    ///     decision that without a recorded identity there is nothing to compare the files with.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_PinWithoutIdentity_AllFoldersWithFiles_SkipsWithoutPackageAccess()
    {
        // Arrange: an older pin, and all four folders holding (any) files; no package source configured
        var repo = CreateRepoWithLegacyPin();
        foreach (var (path, text) in AgentFiles("whatever"))
        {
            WriteFile(repo, path, text);
        }

        var card = CreateCard(repo, new AppSettings(), ThrowingFactory);
        var (requests, errors) = Watch(card);
        _registryRequests = 0;

        // Act
        var outcome = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentFilesSyncOutcome.Skipped, outcome);
        Assert.Empty(requests);
        Assert.Empty(errors);
        Assert.Equal(0, _registryRequests);
    }

    /// <summary>
    ///     Test that a pin that recorded no files identity still restores silently when a managed folder is
    ///     missing, as before.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_PinWithoutIdentity_SomeFolderMissing_RestoresSilently()
    {
        // Arrange: an older pin and only one of the four folders
        var repo = CreateRepoWithLegacyPin();
        WriteFile(repo, ".github/agents/copilot.md", "old");
        var settings = new AppSettings { Sources = [ZipSource("Contoso", CreateSourceFolder("v1"))] };
        var card = CreateCard(repo, settings);
        var (requests, errors) = Watch(card);

        // Act
        var outcome = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentFilesSyncOutcome.Restored, outcome);
        Assert.Empty(requests);
        Assert.Empty(errors);
        AssertInstalledFilesAre(repo, "v1");
    }

    /// <summary>
    ///     Test that when files are needed and the source cannot be read, the user is told why and asked
    ///     whether to launch anyway or cancel; declining (the default) cancels the launch.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_NoFilesAndSourceUnreachable_AsksLaunchAnywayWithReason_DeclineCancelsLaunch()
    {
        // Arrange: a repo without agent files and a source folder that does not exist
        var (repo, _, _) = CreatePinnedRepo("v1");
        Directory.Delete(Path.Combine(repo, ".github"), recursive: true);
        var settings = new AppSettings { Sources = [ZipSource("Contoso", Path.Combine(CreateTempDirectory(), "missing"))] };
        var card = CreateCard(repo, settings);
        var (requests, errors) = Watch(card, answers: [false]);

        // Act
        var outcome = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert: asked with a reason and the two labels, canceled on the default answer, nothing extracted
        Assert.Equal(AgentFilesSyncOutcome.LaunchCanceled, outcome);
        var request = Assert.Single(requests);
        Assert.Contains("could not be restored", request.Message, StringComparison.Ordinal);
        Assert.Contains("Launch anyway?", request.Message, StringComparison.Ordinal);
        Assert.Equal("Launch anyway", request.ConfirmText);
        Assert.Equal("Cancel", request.DeclineText);
        Assert.Empty(errors);
        Assert.False(Directory.Exists(Path.Combine(repo, ".github")));
    }

    /// <summary>
    ///     Test that choosing "Launch anyway" after a failed restore lets the launch proceed.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_NoFilesAndSourceUnreachable_LaunchAnywayChosen_OutcomeFailedLaunchAnyway()
    {
        // Arrange: a repo without agent files and a source folder that does not exist
        var (repo, _, _) = CreatePinnedRepo("v1");
        Directory.Delete(Path.Combine(repo, ".github"), recursive: true);
        var settings = new AppSettings { Sources = [ZipSource("Contoso", Path.Combine(CreateTempDirectory(), "missing"))] };
        var card = CreateCard(repo, settings);
        var (requests, _) = Watch(card, answers: [true]);

        // Act
        var outcome = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentFilesSyncOutcome.FailedLaunchAnyway, outcome);
        Assert.Single(requests);
    }

    /// <summary>
    ///     Test that the refusal of a republished package is kept when files are needed: nothing is
    ///     extracted, and the user is asked whether to launch anyway.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_NoFilesAndOnlyRepublishedPackage_AsksLaunchAnywayAndNothingExtracted()
    {
        // Arrange: a repo without agent files and a source that delivers other files under the version
        var (repo, _, _) = CreatePinnedRepo("v1");
        Directory.Delete(Path.Combine(repo, ".github"), recursive: true);
        var settings = new AppSettings { Sources = [ZipSource("Artifactory", CreateSourceFolder("tampered"))] };
        var card = CreateCard(repo, settings);
        var (requests, _) = Watch(card, answers: [false]);

        // Act
        var outcome = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentFilesSyncOutcome.LaunchCanceled, outcome);
        var request = Assert.Single(requests);
        Assert.Contains("The agent files were not synced", request.Message, StringComparison.Ordinal);
        Assert.Contains("republished", request.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(repo, ".github")));
    }

    /// <summary>
    ///     Test that an input/output failure while reading every configured source is reported as an
    ///     ordinary restore failure (a question) and does not escape as an unhandled exception.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_EveryConfiguredSourceThrowsIOException_IsReportedNotUnhandled()
    {
        // Arrange: a repo without agent files and a source whose listing fails with an I/O error
        var (repo, _, _) = CreatePinnedRepo("v1");
        Directory.Delete(Path.Combine(repo, ".github"), recursive: true);
        var settings = new AppSettings { Sources = [ZipSource("Flaky", CreateTempDirectory())] };
        var card = CreateCard(
            repo,
            settings,
            current => new PackageSourceRegistry(
                current.Sources,
                definition => new FakePackageSourceProvider(definition.Alias, (PackageId, "1.0.0"))
                {
                    ListVersionsException = new IOException("The network drive vanished.")
                }));
        var (requests, errors) = Watch(card, answers: [false]);

        // Act
        var outcome = await card.EnsureAgentFilesSyncedBeforeLaunchAsync(TestContext.Current.CancellationToken);

        // Assert: reported through the launch-anyway question, not thrown
        Assert.Equal(AgentFilesSyncOutcome.LaunchCanceled, outcome);
        var request = Assert.Single(requests);
        Assert.Contains("The network drive vanished.", request.Message, StringComparison.Ordinal);
        Assert.Empty(errors);
    }

    /// <summary>
    ///     Test that verifying stops with an <see cref="OperationCanceledException"/> when the operation is
    ///     canceled, so the launch can carry on without the check.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_CanceledWhileVerifying_ThrowsOperationCanceledException()
    {
        // Arrange: an applied repo and a token that is already canceled
        var (repo, settings, _) = CreatePinnedRepo("v1");
        var card = CreateCard(repo, settings);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        // Act / Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => card.EnsureAgentFilesSyncedBeforeLaunchAsync(canceled.Token));
    }

    /// <summary>
    ///     Test that Launch with files that differ from the pin, when the user keeps them, starts the agent
    ///     tool and records the launch, with the files untouched.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_LaunchCommand_MismatchKeep_Launches()
    {
        // Arrange: an applied repo with an edited file and a user who keeps it
        var (repo, settings, _) = CreatePinnedRepo("v1");
        var edited = Path.Combine(repo, ".github", "agents", "copilot.md");
        await File.WriteAllTextAsync(edited, "my experiment", TestContext.Current.CancellationToken);
        var card = CreateCard(repo, LaunchSettings(settings.Sources));
        var (requests, _) = Watch(card, answers: [false]);
        var launched = false;
        card.LaunchRecorded += (_, _) => launched = true;

        // Act
        await card.LaunchCommand.ExecuteAsync();

        // Assert
        Assert.Single(requests);
        Assert.True(launched);
        Assert.Single(_starter.Started);
        Assert.Equal("my experiment", await File.ReadAllTextAsync(edited, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that Launch with files that differ from the pin, when the user chooses to update, restores
    ///     the package's files and then starts the agent tool.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_LaunchCommand_MismatchUpdate_RestoresThenLaunches()
    {
        // Arrange: an applied repo with a deleted file and a user who updates
        var (repo, settings, _) = CreatePinnedRepo("v1");
        File.Delete(Path.Combine(repo, ".github", "standards", "style.md"));
        var card = CreateCard(repo, LaunchSettings(settings.Sources));
        var (requests, _) = Watch(card, answers: [true]);
        var launched = false;
        card.LaunchRecorded += (_, _) => launched = true;

        // Act
        await card.LaunchCommand.ExecuteAsync();

        // Assert
        Assert.Single(requests);
        Assert.True(launched);
        Assert.Single(_starter.Started);
        AssertInstalledFilesAre(repo, "v1");
    }

    /// <summary>
    ///     Test that when needed agent files cannot be restored and the user cancels, no agent tool is
    ///     started and no launch is recorded.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_LaunchCommand_RestoreFailsAndUserCancels_DoesNotLaunchOrRecordLaunch()
    {
        // Arrange: a repo without agent files, a source that does not offer the package, a user who cancels
        var (repo, _, _) = CreatePinnedRepo("v1");
        Directory.Delete(Path.Combine(repo, ".github"), recursive: true);
        var card = CreateCard(repo, LaunchSettings([ZipSource("Empty", CreateTempDirectory())]));
        var (requests, _) = Watch(card, answers: [false]);
        var launched = false;
        card.LaunchRecorded += (_, _) => launched = true;

        // Act
        await card.LaunchCommand.ExecuteAsync();

        // Assert
        Assert.Single(requests);
        Assert.False(launched);
        Assert.Null(card.LastLaunchedUtc);
        Assert.Empty(_starter.Started);
        Assert.Contains("Launch canceled", card.StatusMessage, StringComparison.Ordinal);
        Assert.False(card.IsBusy);
    }

    /// <summary>
    ///     Test that when needed agent files cannot be restored and the user chooses "Launch anyway", the
    ///     agent tool is started and the launch recorded.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_LaunchCommand_RestoreFailsAndUserChoosesLaunchAnyway_Launches()
    {
        // Arrange: a repo without agent files, a source that does not offer the package, a user who launches
        var (repo, _, _) = CreatePinnedRepo("v1");
        Directory.Delete(Path.Combine(repo, ".github"), recursive: true);
        var card = CreateCard(repo, LaunchSettings([ZipSource("Empty", CreateTempDirectory())]));
        var (requests, _) = Watch(card, answers: [true]);
        var launched = false;
        card.LaunchRecorded += (_, _) => launched = true;

        // Act
        await card.LaunchCommand.ExecuteAsync();

        // Assert
        Assert.Single(requests);
        Assert.True(launched);
        Assert.NotNull(card.LastLaunchedUtc);
        Assert.Single(_starter.Started);
    }

    /// <summary>
    ///     Test that only the first answer to a request counts.
    /// </summary>
    [Fact]
    public async Task AgentFilesChoiceRequest_Respond_FirstAnswerWins()
    {
        // Arrange
        var request = new AgentFilesChoiceRequest("question", "Yes please", "No thanks");

        // Act
        request.Respond(true);
        request.Respond(false);

        // Assert
        Assert.True(await request.Answer);
        Assert.Equal("question", request.Message);
        Assert.Equal("Yes please", request.ConfirmText);
        Assert.Equal("No thanks", request.DeclineText);
    }

    /// <summary>
    ///     Test that a request rejects a missing message or label.
    /// </summary>
    [Fact]
    public void AgentFilesChoiceRequest_Constructor_NullArguments_Throw()
    {
        // Arrange: each argument missing in turn

        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new AgentFilesChoiceRequest(null!, "a", "b"));
        Assert.Throws<ArgumentNullException>(() => new AgentFilesChoiceRequest("m", null!, "b"));
        Assert.Throws<ArgumentNullException>(() => new AgentFilesChoiceRequest("m", "a", null!));
    }

    /// <summary>
    ///     Test that answering a request never runs the waiting operation inside the answering call: the
    ///     continuation runs on another thread, so the dialog's close handling is never re-entered.
    /// </summary>
    [Fact]
    public async Task AgentFilesChoiceRequest_Answer_CompletesAsynchronously()
    {
        // Arrange: a continuation attached before the answer is given from a dedicated thread
        var request = new AgentFilesChoiceRequest("m", "a", "b");
        var continuationThread = request.Answer.ContinueWith(
            _ => Environment.CurrentManagedThreadId, TaskContinuationOptions.ExecuteSynchronously);
        var respondingThread = 0;
        var thread = new Thread(() =>
        {
            respondingThread = Environment.CurrentManagedThreadId;
            request.Respond(true);
        });

        // Act
        thread.Start();
        thread.Join();

        // Assert
        Assert.NotEqual(respondingThread, await continuationThread);
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup; leftover temp folders do not fail the test.
            }
        }
    }

    /// <summary>
    ///     Creates a zip source definition.
    /// </summary>
    /// <param name="alias">The source alias.</param>
    /// <param name="folder">The folder that holds the zips.</param>
    /// <returns>The definition.</returns>
    private static PackageSourceDefinition ZipSource(string alias, string folder) =>
        new() { Alias = alias, Type = PackageSourceType.Zip, Location = folder };

    /// <summary>
    ///     The four agent files every package of these tests delivers, whose agents file holds the given
    ///     marker.
    /// </summary>
    /// <param name="marker">The text of the agents file.</param>
    /// <returns>The package-relative path and text of each file.</returns>
    private static (string Path, string Text)[] AgentFiles(string marker) =>
    [
        (".github/agents/copilot.md", marker),
        (".github/standards/style.md", "standards content"),
        (".github/templates/template.md", "templates content"),
        (".github/skills/skill.md", "skills content")
    ];

    /// <summary>
    ///     Creates a zip source folder holding version 1.0.0 of the package.
    /// </summary>
    /// <param name="marker">The text of the agents file.</param>
    /// <returns>The folder.</returns>
    private string CreateSourceFolder(string marker)
    {
        var folder = CreateTempDirectory();
        using var archive = ZipFile.Open(Path.Combine(folder, $"{PackageId}-1.0.0.zip"), ZipArchiveMode.Create);
        foreach (var (name, text) in AgentFiles(marker).Append(("release-notes.md", "notes")))
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(text);
        }

        return folder;
    }

    /// <summary>
    ///     Creates a repo that was really applied from version 1.0.0 of a zip source, so its pin is exactly
    ///     what the application writes (it records the files identity).
    /// </summary>
    /// <param name="marker">The text of the agents file.</param>
    /// <returns>The repo folder, the settings of the machine that applied it (one source, "Contoso", that
    ///     still delivers the package) and the text of the pin file.</returns>
    private (string Repo, AppSettings Settings, string PinJson) CreatePinnedRepo(string marker)
    {
        var repo = CreateTempDirectory();
        var settings = new AppSettings { Sources = [ZipSource("Contoso", CreateSourceFolder(marker))] };
        var card = CreateCard(repo, settings);
        card.ApplySelectedPackage(PackageId, "1.0.0", "Contoso");
        Assert.True(PackageContentIdentity.IsFilesIdentity(RepoPinStore.Load(repo)?.ContentIdentity));
        return (repo, settings, File.ReadAllText(PinPath(repo)));
    }

    /// <summary>
    ///     Creates a repo with an older pin (schema 2) that recorded no files identity.
    /// </summary>
    /// <returns>The repo folder.</returns>
    private string CreateRepoWithLegacyPin()
    {
        var repo = CreateTempDirectory();
        File.WriteAllText(
            PinPath(repo),
            $$"""{ "SchemaVersion": 2, "Source": "Contoso", "PackageName": "{{PackageId}}", "Version": "1.0.0" }""");
        return repo;
    }

    /// <summary>
    ///     Creates a card for a repo whose pin has been read, with the registry requests counted.
    /// </summary>
    /// <param name="repo">The repo folder.</param>
    /// <param name="settings">The settings of the machine.</param>
    /// <param name="registryFactory">Builds the registry; defaults to the real one for the configured
    ///     sources. Every call is counted in <see cref="_registryRequests"/>.</param>
    /// <returns>The card.</returns>
    private RepoCardViewModel CreateCard(
        string repo, AppSettings settings, Func<AppSettings, PackageSourceRegistry>? registryFactory = null)
    {
        var card = new RepoCardViewModel(
            new RecentRepo { Path = repo },
            () => settings,
            new PackageVersionCache(),
            registryFactory: current =>
            {
                Interlocked.Increment(ref _registryRequests);
                return registryFactory is null
                    ? PackageSourceRegistry.Create(current.GetEffectiveSources())
                    : registryFactory(current);
            },
            // Launch-safe by construction: a card built here never starts a real process
            processStarter: _starter,
            shellDetector: FakeAgentToolProcessStarter.CreateShellDetector());
        card.RefreshCheap();
        return card;
    }

    /// <summary>
    ///     A registry factory that fails the test (by throwing) when a package source is reached.
    /// </summary>
    /// <param name="settings">The settings (unused).</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="InvalidOperationException">Always thrown: no package source may be reached.</exception>
    private static PackageSourceRegistry ThrowingFactory(AppSettings settings) =>
        throw new InvalidOperationException("A package source must not be reached.");

    /// <summary>
    ///     Builds settings that launch a fake agent tool command (the process is never started: the
    ///     cards of these tests use <see cref="_starter"/>).
    /// </summary>
    /// <param name="sources">The configured package sources.</param>
    /// <returns>The settings.</returns>
    private static AppSettings LaunchSettings(IReadOnlyList<PackageSourceDefinition> sources) =>
        new()
        {
            AgentTool = AgentToolKind.Custom,
            CustomAgentCommand = "my-agent",
            Sources = [.. sources]
        };

    /// <summary>
    ///     Subscribes to what a card tells the user: the questions it raises (answered from a script) and
    ///     the messages it reports.
    /// </summary>
    /// <param name="card">The card to watch.</param>
    /// <param name="answers">The answers to give, in order, to the questions raised: <see langword="true"/>
    ///     for the confirming button; when the script runs out (or none is given) the safe answer is
    ///     given and the question is still recorded.</param>
    /// <returns>The questions raised and the messages reported, appended to as they happen.</returns>
    private static (List<AgentFilesChoiceRequest> Requests, List<string> Errors) Watch(
        RepoCardViewModel card, IEnumerable<bool>? answers = null)
    {
        var requests = new List<AgentFilesChoiceRequest>();
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Always subscribe (with an empty script when none was given) so that an unexpected question is
        // recorded and answered with the safe choice instead of silently skipped
        var script = new Queue<bool>(answers ?? []);
        card.AgentFilesChoiceRequested += (_, request) =>
        {
            requests.Add(request);
            request.Respond(script.Count > 0 && script.Dequeue());
        };
        return (requests, errors);
    }

    /// <summary>
    ///     Asserts the repo holds exactly the four package files and nothing else in the managed folders.
    /// </summary>
    /// <param name="repo">The repo folder.</param>
    /// <param name="marker">The expected text of the agents file.</param>
    private static void AssertInstalledFilesAre(string repo, string marker)
    {
        var expected = AgentFiles(marker)
            .Select(file => file.Path + "=" + file.Text)
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(expected, Snapshot(repo));
    }

    /// <summary>
    ///     Captures the files below a repo's <c>.github</c> folder (relative path and text) for comparison.
    /// </summary>
    /// <param name="repo">The repo folder.</param>
    /// <returns>A sorted list of <c>path=text</c> lines.</returns>
    private static List<string> Snapshot(string repo)
    {
        var github = Path.Combine(repo, ".github");
        return Directory.Exists(github)
            ? Directory.EnumerateFiles(github, "*", SearchOption.AllDirectories)
                .Select(file => ".github/" + Path.GetRelativePath(github, file).Replace('\\', '/') + "=" + File.ReadAllText(file))
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];
    }

    /// <summary>
    ///     Writes a text file below a repo, creating the folders on the way.
    /// </summary>
    /// <param name="repo">The repo folder.</param>
    /// <param name="relativePath">The <c>/</c>-separated path below the repo.</param>
    /// <param name="content">The file's text.</param>
    private static void WriteFile(string repo, string relativePath, string content)
    {
        var path = Path.Combine(repo, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>
    ///     Gets the path of a repo's pin file.
    /// </summary>
    /// <param name="repo">The repo folder.</param>
    /// <returns>The path.</returns>
    private static string PinPath(string repo) => Path.Combine(repo, ".agentcontrol.json");

    /// <summary>
    ///     Creates a directory junction (needs no privilege on Windows), or skips the test on platforms
    ///     that do not have junctions.
    /// </summary>
    /// <param name="linkPath">The junction to create.</param>
    /// <param name="target">The existing directory it points at.</param>
    private static void CreateJunctionOrSkip(string linkPath, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Directory junctions exist only on Windows.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("/c");
        // cspell:disable-next-line
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(target);
        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start cmd.exe to create a junction.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Creating the junction failed ({process.ExitCode}): {output}");
        }
    }

    /// <summary>
    ///     Creates a directory symbolic link, or skips the test when the platform or privileges do not
    ///     permit creating one.
    /// </summary>
    /// <param name="linkPath">The link to create.</param>
    /// <param name="target">The existing directory it points at.</param>
    private static void CreateDirectorySymlinkOrSkip(string linkPath, string target)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
            Directory.CreateSymbolicLink(linkPath, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip($"Cannot create symbolic links in this environment: {ex.Message}");
        }
    }

    /// <summary>
    ///     Creates a unique temporary directory removed by <see cref="Dispose"/>.
    /// </summary>
    /// <returns>The folder.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_launch_verification_test_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }
}
