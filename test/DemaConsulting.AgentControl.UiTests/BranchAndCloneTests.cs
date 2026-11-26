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

using System.Text.Json;
using DemaConsulting.AgentControl.Tests.GitIntegration;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     End-to-end tests of switching a repo to another branch from its card and of cloning a repository from
///     the app bar, in the published application with the real <c>git</c> against local repositories (see
///     <see cref="GitFixtureRepository"/>; skipped when git is not installed, failed when the environment
///     variable <c>CI</c> is set). Nothing touches a network, the user's git configuration or a credential
///     store.
/// </summary>
public sealed class BranchAndCloneTests
{
    /// <summary>
    ///     The user name of the private test server.
    /// </summary>
    private const string SignInUser = "agent";

    /// <summary>
    ///     The password of the private test server; it must never appear in a file the application writes.
    /// </summary>
    private const string SignInPassword = "clone-sentinel-pw-3b9e52";
    /// <summary>
    ///     Switching to a branch that exists only on the remote creates a local branch for it: the card's
    ///     branch chip and the files follow, the card says so; then, with uncommitted work that the other
    ///     branch would overwrite, the switch is refused with git's own reason in a message, the card stays on
    ///     its branch and the user's work is untouched.
    /// </summary>
    [Fact]
    public void SwitchBranchMenuItem_ChooseOtherBranch_ChangesBranchChipAndBlockedSwitchShowsGitsReason()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: the card's repository is a clone on main, with a local branch and a branch that exists only on the remote
        using var fixture = GitFixtureRepository.CreateOrSkip();
        using var context = new AgentControlTestContext(useRealGit: true)
        {
            LaunchEnvironment = fixture.IsolationEnvironment
        };
        var repo = context.RepoPath;
        var notes = Path.Combine(repo, "release-notes.md");
        fixture.Git(fixture.Root, "clone", "-q", fixture.BarePath, repo);
        fixture.Git(repo, "switch", "-q", "-c", "feature");
        File.WriteAllText(notes, "feature notes\n");
        fixture.Git(repo, "commit", "-q", "-a", "-m", "feature");
        fixture.Git(repo, "switch", "-q", "-c", "scratch");
        File.WriteAllText(notes, "remote notes\n");
        fixture.Git(repo, "commit", "-q", "-a", "-m", "remote only");
        fixture.Git(repo, "push", "-q", "origin", "scratch:from-remote");
        fixture.Git(repo, "switch", "-q", "main");
        fixture.Git(repo, "branch", "-q", "-D", "scratch");
        var mainWindow = context.Launch();
        Assert.True(GitSourceTests.WaitForText(mainWindow, "CurrentBranchText", "main"));

        // Act: choose Switch branch... and the branch that only the remote has
        ChooseBranch(context, mainWindow, "from-remote");

        // Assert: the branch chip, the repository and the files are on the new branch, and the card says so
        Assert.True(GitSourceTests.WaitForText(mainWindow, "CurrentBranchText", "from-remote"));
        Assert.Equal("from-remote", fixture.Git(repo, "branch", "--show-current").Trim());
        Assert.Equal("remote notes\n", File.ReadAllText(notes));
        Assert.True(GitSourceTests.WaitForText(mainWindow, "RepoStatusMessageText", "Switched to branch 'from-remote'."));

        // Arrange: uncommitted work that main's version of the file would overwrite
        File.WriteAllText(notes, "my uncommitted work\n");

        // Act: try to switch to main
        ChooseBranch(context, mainWindow, "main");

        // Assert: git's reason is shown, naming the problem and that nothing was changed
        var messageWindow = context.WaitForWindowWithDescendant("MessageText", TimeSpan.FromSeconds(30));
        Assert.NotNull(messageWindow);
        var message = GitSourceTests.Find(messageWindow, "MessageText").Name;
        Assert.Contains("would be overwritten", message, StringComparison.Ordinal);
        Assert.Contains("Nothing was changed.", message, StringComparison.Ordinal);
        GitSourceTests.Find(messageWindow, "MessageBoxOkButton").AsButton().Invoke();

        // Assert: still on the branch, and the user's work is untouched
        Assert.Equal("from-remote", fixture.Git(repo, "branch", "--show-current").Trim());
        Assert.Equal("my uncommitted work\n", File.ReadAllText(notes));
        Assert.True(GitSourceTests.WaitForText(mainWindow, "CurrentBranchText", "from-remote"));
    }

    /// <summary>
    ///     Cloning a local bare repository from the Clone button - address, parent folder and folder name typed
    ///     into the dialog, the group of the list chosen - makes a working clone, adds its card to the group,
    ///     saves it, and leaves no progress strip or dialog behind.
    /// </summary>
    [Fact]
    public void CloneRepoButton_CloneFromLocalBareRepoIntoGroup_AddsTheCardAndRemovesTheProgressStrip()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: a local bare repository, and a list with one repo in the group "Work"
        using var fixture = GitFixtureRepository.CreateOrSkip();
        using var context = new AgentControlTestContext(
            useRealGit: true,
            customizeSettings: (settings, self) =>
                settings["RecentRepos"] = new object[] { new { Path = self.RepoPath, GroupName = "Work" } })
        {
            LaunchEnvironment = fixture.IsolationEnvironment
        };
        var parent = Path.Combine(context.RootDirectory, "clones");
        Directory.CreateDirectory(parent);
        var destination = Path.Combine(parent, "cloned-agents");
        var mainWindow = context.Launch();
        Assert.True(CardRefreshWait.WaitUntilCardsAreRead(mainWindow, TimeSpan.FromSeconds(30)));

        // Act: open the dialog from the app bar and fill it in
        GitSourceTests.Find(mainWindow, "CloneRepoButton").AsButton().Invoke();
        var dialog = context.WaitForDialog("CloneUrlTextBox", TimeSpan.FromSeconds(15));
        Assert.NotNull(dialog);
        GitSourceTests.Find(dialog, "CloneUrlTextBox").AsTextBox().Text = fixture.BarePath;
        GitSourceTests.Find(dialog, "CloneParentTextBox").AsTextBox().Text = parent;
        GitSourceTests.Find(dialog, "CloneFolderNameTextBox").AsTextBox().Text = "cloned-agents";
        GitSourceTests.Find(dialog, "CloneGroupComboBox").AsComboBox().Select("Work");
        var clone = GitSourceTests.Find(dialog, "CloneAcceptButton").AsButton();
        Assert.True(Retry.WhileFalse(() => clone.IsEnabled, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200)).Result);
        clone.Invoke();

        // Assert: the card appears and the progress strip and the dialog are gone
        Assert.True(Retry.WhileFalse(
            () => RepoNames(mainWindow).Contains("cloned-agents"),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.True(Retry.WhileFalse(
            () => CardRefreshWait.FindShown(mainWindow, "GlobalProgressStrip") is null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Null(context.WaitForDialog("CloneUrlTextBox", TimeSpan.FromMilliseconds(500)));

        // Assert: a working clone with the repository's files
        Assert.True(Directory.Exists(Path.Combine(destination, ".git")));
        Assert.True(File.Exists(Path.Combine(destination, "release-notes.md")));
        Assert.Equal("main", fixture.Git(destination, "branch", "--show-current").Trim());

        // Assert: the card is listed in the group (its header counts both repos) and saved with its group
        Assert.True(Retry.WhileFalse(
            () => mainWindow.FindAllDescendants(cf => cf.ByAutomationId("GroupHeaderToggleButton"))
                .Any(header => header.Name.StartsWith("Work, 2 repos", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.True(Retry.WhileFalse(
            () => StoredGroupOf(context, destination) == "Work",
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(200)).Result);
    }

    /// <summary>
    ///     An address that git would read as an option or a remote helper (<c>--upload-pack=...</c>,
    ///     <c>ext::sh -c ...</c>) is refused by the Clone dialog - the error is shown and Clone cannot be
    ///     pressed - so no git clone is ever started and the program the address names is never run.
    /// </summary>
    [Fact]
    public void CloneRepoButton_UrlThatLooksLikeAnOption_IsRefusedAndRunsNoGit()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: the arg-logger stub stands in for git, so any clone that was started would be recorded
        using var context = new AgentControlTestContext();
        var marker = Path.Combine(context.RootDirectory, "program-ran.txt");
        var mainWindow = context.Launch();
        Assert.True(CardRefreshWait.WaitUntilCardsAreRead(mainWindow, TimeSpan.FromSeconds(30)));
        GitSourceTests.Find(mainWindow, "CloneRepoButton").AsButton().Invoke();
        var dialog = context.WaitForDialog("CloneUrlTextBox", TimeSpan.FromSeconds(15));
        Assert.NotNull(dialog);
        GitSourceTests.Find(dialog, "CloneParentTextBox").AsTextBox().Text = context.RootDirectory;

        foreach (var address in new[]
                 {
                     $"--upload-pack=\"echo ran > {marker.Replace('\\', '/')}\"",
                     $"ext::sh -c \"echo ran > {marker.Replace('\\', '/')}\""
                 })
        {
            // Act: type the address
            GitSourceTests.Find(dialog, "CloneUrlTextBox").AsTextBox().Text = address;

            // Assert: the refusal is shown and Clone cannot be pressed
            Assert.True(Retry.WhileFalse(
                () => !string.IsNullOrWhiteSpace(
                    dialog.FindFirstDescendant(cf => cf.ByAutomationId("CloneErrorText"))?.Name),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(200)).Result);
            Assert.False(GitSourceTests.Find(dialog, "CloneAcceptButton").AsButton().IsEnabled);
        }

        // Act: close the dialog
        GitSourceTests.Find(dialog, "CloneCancelButton").AsButton().Invoke();
        Assert.True(Retry.WhileFalse(
            () => context.WaitForDialog("CloneUrlTextBox", TimeSpan.FromMilliseconds(200)) is null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);

        // Assert: no clone was started and no program ran
        Assert.DoesNotContain(
            ArgLoggerLog.ReadAll(context.ArgLoggerOutputFile),
            record => record.Arguments.Contains("clone"));
        Assert.False(File.Exists(marker));
        Assert.Equal(["repo"], RepoNames(mainWindow));
    }

    /// <summary>
    ///     A repo whose branches pin different versions follows the switch: the package chip shows the new
    ///     branch's version, and the uncommitted changes that do not touch the files that differ travel with the
    ///     switch (an untracked file and an edit of a file both branches share), so the card still shows them.
    /// </summary>
    [Fact]
    public void SwitchBranchMenuItem_BranchWithOtherPin_ChangesPackageChipAndKeepsUnrelatedChanges()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: a clone whose main branch pins 1.0.0 and whose other branch pins 2.0.0, with the same shared file
        using var fixture = GitFixtureRepository.CreateOrSkip();
        using var context = new AgentControlTestContext(useRealGit: true)
        {
            LaunchEnvironment = fixture.IsolationEnvironment
        };
        var repo = context.RepoPath;
        fixture.Git(fixture.Root, "clone", "-q", fixture.BarePath, repo);
        var shared = Path.Combine(repo, "shared.txt");
        File.WriteAllText(shared, "shared\n");
        TestRepoPinWriter.Write(repo, GitFixtureRepository.PackageName, "1.0.0");
        fixture.Git(repo, "add", "-A");
        fixture.Git(repo, "commit", "-q", "-m", "pin 1.0.0");
        fixture.Git(repo, "switch", "-q", "-c", "other");
        TestRepoPinWriter.Write(repo, GitFixtureRepository.PackageName, "2.0.0");
        fixture.Git(repo, "commit", "-q", "-a", "-m", "pin 2.0.0");
        fixture.Git(repo, "switch", "-q", "main");

        // Arrange: work in progress that the switch has no reason to touch
        var untracked = Path.Combine(repo, "my-notes.txt");
        File.WriteAllText(untracked, "notes in progress\n");
        File.WriteAllText(shared, "shared, edited\n");
        var mainWindow = context.Launch();
        Assert.True(GitSourceTests.WaitForText(mainWindow, "CurrentBranchText", "main"));
        Assert.True(GitSourceTests.WaitForText(
            mainWindow, "RepoPinnedVersionText", $"{GitFixtureRepository.PackageName} 1.0.0"));
        Assert.True(WaitForDirtyBadge(mainWindow));

        // Act: switch to the other branch
        ChooseBranch(context, mainWindow, "other");

        // Assert: the branch chip and the package chip follow the branch
        Assert.True(GitSourceTests.WaitForText(mainWindow, "CurrentBranchText", "other"));
        Assert.True(GitSourceTests.WaitForText(
            mainWindow, "RepoPinnedVersionText", $"{GitFixtureRepository.PackageName} 2.0.0"));
        Assert.Equal("other", fixture.Git(repo, "branch", "--show-current").Trim());

        // Assert: the work in progress went along, and the card still shows it
        Assert.Equal("notes in progress\n", File.ReadAllText(untracked));
        Assert.Equal("shared, edited\n", File.ReadAllText(shared));
        Assert.True(WaitForDirtyBadge(mainWindow));
    }

    /// <summary>
    ///     While a clone runs (git held by the arg-logger stub), the progress strip shows it with a Cancel button
    ///     named after the clone and the Clone button is unavailable; Cancel stops the clone, and the parent folder
    ///     holds exactly what the user put there - no hidden working folder, no destination - with no message and no
    ///     new card, and the Clone button is available again.
    /// </summary>
    [Fact]
    public void GlobalProgressCancelButton_WhileCloning_StopsTheCloneAndLeavesNothingBehind()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: the stub stands in for git and keeps every command waiting while the hold file exists; the
        // parent folder holds a file of the user's and a folder that only looks like the clone's working folder
        using var context = new AgentControlTestContext();
        var holdFile = Path.Combine(context.RootDirectory, "git-hold");
        context.GitHoldFile = holdFile;
        var parent = Path.Combine(context.RootDirectory, "clones");
        Directory.CreateDirectory(parent);
        var sentinel = Path.Combine(parent, "keep-me.txt");
        File.WriteAllText(sentinel, "mine");
        var lookAlike = Path.Combine(parent, ".agentcontrol-clone-not-mine");
        Directory.CreateDirectory(lookAlike);
        File.WriteAllText(Path.Combine(lookAlike, "keep-me-too.txt"), "mine");
        var mainWindow = context.Launch();
        Assert.True(CardRefreshWait.WaitUntilCardsAreRead(mainWindow, TimeSpan.FromSeconds(30)));
        File.WriteAllText(holdFile, string.Empty);
        try
        {
            // Act: clone from the dialog; git (the stub) is held
            GitSourceTests.Find(mainWindow, "CloneRepoButton").AsButton().Invoke();
            var dialog = context.WaitForDialog("CloneUrlTextBox", TimeSpan.FromSeconds(15));
            Assert.NotNull(dialog);
            GitSourceTests.Find(dialog, "CloneUrlTextBox").AsTextBox().Text = "https://example.invalid/team/r.git";
            GitSourceTests.Find(dialog, "CloneParentTextBox").AsTextBox().Text = parent;
            GitSourceTests.Find(dialog, "CloneFolderNameTextBox").AsTextBox().Text = "canceled-clone";
            var clone = GitSourceTests.Find(dialog, "CloneAcceptButton").AsButton();
            Assert.True(Retry.WhileFalse(() => clone.IsEnabled, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200)).Result);
            clone.Invoke();

            // Assert: the strip shows the clone with a Cancel button named for it, and Clone is unavailable
            Assert.True(Retry.WhileFalse(
                () => CardRefreshWait.FindShown(mainWindow, "GlobalProgressText")?.Name == "Cloning canceled-clone...",
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMilliseconds(200)).Result);
            Assert.Equal(
                "Cancel cloning repository",
                CardRefreshWait.FindShown(mainWindow, "GlobalProgressCancelButton")?.Name);
            Assert.False(GitSourceTests.Find(mainWindow, "CloneRepoButton").AsButton().IsEnabled);

            // Act: Cancel
            GitSourceTests.Find(mainWindow, "GlobalProgressCancelButton").AsButton().Invoke();

            // Assert: the strip goes and Clone is available again
            Assert.True(Retry.WhileFalse(
                () => CardRefreshWait.FindShown(mainWindow, "GlobalProgressStrip") is null,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMilliseconds(200)).Result);
            Assert.True(Retry.WhileFalse(
                () => GitSourceTests.Find(mainWindow, "CloneRepoButton").AsButton().IsEnabled,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMilliseconds(200)).Result);

            // Assert: only what the user put there is left - no working folder, no destination
            Assert.Equal(
                [".agentcontrol-clone-not-mine", "keep-me.txt"],
                Directory.GetFileSystemEntries(parent).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
            Assert.Equal("mine", File.ReadAllText(sentinel));
            Assert.Equal("mine", File.ReadAllText(Path.Combine(lookAlike, "keep-me-too.txt")));

            // Assert: no message, no new card
            Assert.Null(context.WaitForWindowWithDescendant("MessageText", TimeSpan.FromSeconds(2)));
            Assert.Equal(["repo"], RepoNames(mainWindow));
        }
        finally
        {
            // Release any git still waiting before the application is closed
            File.Delete(holdFile);
        }
    }

    /// <summary>
    ///     A clone whose address carries a password (<c>https://user:password@host/...</c>) works against a private
    ///     server, adds the card, and never writes the password to the application's settings or logs; the
    ///     password stays in git's own <c>.git/config</c> of the new folder, which is git's behavior.
    /// </summary>
    [Fact]
    public async Task CloneRepoButton_CloneWithPasswordInAddress_NeverWritesThePasswordToApplicationFiles()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: a private server (real git, throw-away certificate, sign-in required) and an address with the
        // user and password; the clone goes outside the application's folders, because git itself keeps the address
        // in the new folder's configuration
        using var fixture = GitFixtureRepository.CreateOrSkip();
        await using var server = await GitHttpsServer.StartAsync(fixture.BarePath, SignInUser, SignInPassword);
        var (configuration, _) = GitHttpsServer.CreateUserConfiguration(fixture.Root, SignInUser, SignInPassword);
        using var context = new AgentControlTestContext(useRealGit: true)
        {
            LaunchEnvironment = new Dictionary<string, string>
            {
                ["GIT_CONFIG_GLOBAL"] = configuration,
                ["GIT_CONFIG_NOSYSTEM"] = "1"
            }
        };
        var address = new UriBuilder(server.RepositoryUrl) { UserName = SignInUser, Password = SignInPassword }.Uri.AbsoluteUri;
        var parent = Path.Combine(fixture.Root, "clones");
        Directory.CreateDirectory(parent);
        var destination = Path.Combine(parent, "agents-clone");
        var mainWindow = context.Launch();
        Assert.True(CardRefreshWait.WaitUntilCardsAreRead(mainWindow, TimeSpan.FromSeconds(30)));

        // Act: clone from the dialog with the password in the address
        GitSourceTests.Find(mainWindow, "CloneRepoButton").AsButton().Invoke();
        var dialog = context.WaitForDialog("CloneUrlTextBox", TimeSpan.FromSeconds(15));
        Assert.NotNull(dialog);
        GitSourceTests.Find(dialog, "CloneUrlTextBox").AsTextBox().Text = address;
        GitSourceTests.Find(dialog, "CloneParentTextBox").AsTextBox().Text = parent;
        GitSourceTests.Find(dialog, "CloneFolderNameTextBox").AsTextBox().Text = "agents-clone";
        var accept = GitSourceTests.Find(dialog, "CloneAcceptButton").AsButton();
        Assert.True(Retry.WhileFalse(() => accept.IsEnabled, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200)).Result);
        accept.Invoke();

        // Assert: the clone worked - the card is listed, the folder is a working clone, the server was signed in
        Assert.True(Retry.WhileFalse(
            () => RepoNames(mainWindow).Contains("agents-clone"),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.True(Directory.Exists(Path.Combine(destination, ".git")));
        Assert.True(server.SignedInRequestCount > 0);
        Assert.Null(context.WaitForWindowWithDescendant("MessageText", TimeSpan.FromSeconds(2)));

        // Assert: the application wrote the password nowhere in its own folders (settings, logs, pin files)
        context.CloseApplication();
        foreach (var file in Directory.EnumerateFiles(context.RootDirectory, "*", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain(SignInPassword, SharedFile.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Waits until the first card shows the warning that the working tree has uncommitted changes.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    /// <returns><see langword="true"/> when the warning was shown in time.</returns>
    private static bool WaitForDirtyBadge(Window mainWindow) =>
        Retry.WhileFalse(
            () => CardRefreshWait.FindShown(mainWindow, "DirtyWorkingTreeBadge") is not null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(200)).Result;

    /// <summary>
    ///     Opens the card's Switch branch dialog, filters the list down to one branch and switches to it.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <param name="mainWindow">The main window.</param>
    /// <param name="branch">The branch to switch to; it must be the only other branch whose name contains this
    ///     text.</param>
    private static void ChooseBranch(AgentControlTestContext context, Window mainWindow, string branch)
    {
        GitSourceTests.OpenCardMenuItem(mainWindow, "SwitchBranchMenuItem").AsMenuItem().Invoke();
        var dialog = context.WaitForDialog("SwitchBranchFilterTextBox", TimeSpan.FromSeconds(15));
        Assert.NotNull(dialog);

        // One branch left to choose from selects it, so the Switch button becomes available
        GitSourceTests.Find(dialog, "SwitchBranchFilterTextBox").AsTextBox().Text = branch;
        var switchButton = GitSourceTests.Find(dialog, "SwitchBranchAcceptButton").AsButton();
        Assert.True(Retry.WhileFalse(
            () => switchButton.IsEnabled, TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(200)).Result);
        switchButton.Invoke();
    }

    /// <summary>
    ///     Lists the names of the repo cards shown, top to bottom.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    /// <returns>The card names.</returns>
    private static List<string> RepoNames(Window mainWindow) =>
        [.. mainWindow.FindAllDescendants(cf => cf.ByAutomationId("RepoNameText"))
            .OrderBy(element => element.BoundingRectangle.Top).Select(element => element.Name)];

    /// <summary>
    ///     Reads the group the settings file stores for a repo.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <param name="repoPath">The repo's folder.</param>
    /// <returns>The group's name, or <see langword="null"/> when the repo is not stored or has no group.</returns>
    private static string? StoredGroupOf(AgentControlTestContext context, string repoPath)
    {
        var path = Path.Combine(context.ConfigDirectory, "settings.json");
        if (!File.Exists(path))
        {
            return null;
        }

        using var document = JsonDocument.Parse(SharedFile.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("RecentRepos", out var repos) || repos.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var repo in repos.EnumerateArray())
        {
            if (repo.TryGetProperty("Path", out var stored) &&
                string.Equals(
                    Path.TrimEndingDirectorySeparator(stored.GetString() ?? string.Empty),
                    Path.TrimEndingDirectorySeparator(repoPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                return repo.TryGetProperty("GroupName", out var group) ? group.GetString() : null;
            }
        }

        return null;
    }
}
