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
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     End-to-end tests for the Git repository package source: adding one through the Settings window,
///     choosing a package from it, the moved-tag warning on a repo card and "Change package...".
/// </summary>
/// <remarks>
///     Every test builds a real local bare Git repository (see <see cref="GitFixtureRepository"/>; the
///     test is skipped when git is not installed) and runs the application with the real <c>git</c> in
///     place of the arg-logger stub and with git isolated from the user's configuration and credential
///     helpers, so nothing touches the real user profile or a credential store and no network is used.
///     The tests drive the published application through UI Automation patterns, reusing
///     <see cref="AgentControlTestContext"/>; dialogs are found by an element they contain because a
///     modal dialog's title is not a dependable match target.
/// </remarks>
public sealed class GitSourceTests
{
    /// <summary>
    ///     The Settings window's Add menu offers a "Git repository" item; adding a Git source through its
    ///     dialog, testing the connection lists the matching tags, and saving persists the source with
    ///     its tag pattern.
    /// </summary>
    [Fact]
    public void SettingsWindow_AddGitRepositoryViaDialog_TestsConnectionListsTagsAndPersistsIt()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        using var fixture = GitFixtureRepository.CreateOrSkip();
        using var context = new AgentControlTestContext(useRealGit: true)
        {
            LaunchEnvironment = fixture.IsolationEnvironment
        };
        var mainWindow = context.Launch();

        // Act: open the Add menu
        var settingsWindow = OpenSettings(context, mainWindow);
        Find(settingsWindow, "AddSourceButton").AsButton().Invoke();

        // Assert: the menu offers the Git repository type, titled and described
        var menuItem = Find(settingsWindow, "AddGitSourceButton");
        Assert.NotNull(menuItem.FindFirstDescendant(cf => cf.ByName("Git repository")));
        Assert.NotNull(menuItem.FindFirstDescendant(cf => cf.ByName("Packages are tags of a Git repository")));

        // Act: choose it, which opens the source editor in Git mode
        menuItem.AsMenuItem().Invoke();
        var editor = context.WaitForWindowWithDescendant("SourceGitPackageIdTextBox", TimeSpan.FromSeconds(15));
        Assert.NotNull(editor);
        Assert.Equal("v{version}", Find(editor, "SourceTagPatternTextBox").AsTextBox().Text);
        Find(editor, "SourceAliasTextBox").AsTextBox().Text = "Repo";
        Find(editor, "SourceLocationTextBox").AsTextBox().Text = fixture.BarePath;
        Find(editor, "SourceTagPatternTextBox").AsTextBox().Text = "release/{version}";

        // Act: test the connection, which lists the tags without cloning
        var test = Find(editor, "SourceTestConnectionButton").AsButton();
        Assert.True(Retry.WhileFalse(() => test.IsEnabled, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200)).Result);
        test.Invoke();

        // Assert: the connection is fine and the preview lists the two version tags, newest first
        Assert.True(WaitForText(editor, "SourceTestResultTitleText", "Connection OK"));
        Assert.Contains("(latest 1.1.0)", Find(editor, "SourceTestResultDetailText").Name, StringComparison.Ordinal);
        var summary = Find(editor, "SourceTagPreviewSummaryText").Name;
        Assert.StartsWith("2 of 4 tags match the pattern.", summary, StringComparison.Ordinal);
        var previewList = Find(editor, "SourceTagPreviewList");
        Assert.True(ContainsName(previewList, "1.1.0   (tag release/1.1.0)"));
        Assert.True(ContainsName(previewList, "1.0.0   (tag release/1.0.0)"));
        Assert.False(ContainsName(previewList, "latest"));

        // Act: save the source, then the settings
        var save = Find(editor, "SourceEditorSaveButton").AsButton();
        Assert.True(Retry.WhileFalse(() => save.IsEnabled, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200)).Result);
        save.Invoke();
        Assert.True(WaitForText(settingsWindow, "SourceAliasText", "Repo"));

        // Assert: the list names the kind of the new source and, because the fixture repository is a
        // local path, shows the status of its folder (a remote repository would read "Online source")
        var sourcesList = Find(settingsWindow, "SourcesList");
        Assert.NotNull(sourcesList.FindFirstDescendant(cf => cf.ByName("Git repository")));
        Assert.True(WaitForText(settingsWindow, "SourceStatusText", "Folder found"));
        Find(settingsWindow, "SaveSettingsButton").AsButton().Invoke();

        // Assert: the source was persisted as a Git source with its address, pattern and package name
        var settingsPath = Path.Combine(context.ConfigDirectory, "settings.json");
        Assert.True(Retry.WhileFalse(
            () => File.Exists(settingsPath) && File.ReadAllText(settingsPath).Contains("\"Repo\"", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        var json = File.ReadAllText(settingsPath);
        Assert.Contains("\"Type\": \"Git\"", json, StringComparison.Ordinal);
        using var settings = JsonDocument.Parse(json);
        var source = settings.RootElement.GetProperty("Sources").EnumerateArray()
            .Single(element => element.GetProperty("Alias").GetString() == "Repo");
        Assert.Equal(fixture.BarePath, source.GetProperty("Location").GetString());
        Assert.Equal("release/{version}", source.GetProperty("TagPattern").GetString());
        Assert.Equal(["Repo"], source.GetProperty("PackageIds").EnumerateArray().Select(id => id.GetString()));
    }

    /// <summary>
    ///     A tag pattern whose capitalization differs from the repository's tags matches nothing: the
    ///     connection test says "No matching tags" and names a real tag so the mistake is easy to spot, and
    ///     once the pattern is fixed the same test succeeds.
    /// </summary>
    [Fact]
    public void SettingsWindow_AddGitRepositoryWithWrongCasePattern_ShowsNoMatchingTagsThenSucceedsWhenFixed()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        using var fixture = GitFixtureRepository.CreateOrSkip();
        using var context = new AgentControlTestContext(useRealGit: true)
        {
            LaunchEnvironment = fixture.IsolationEnvironment
        };
        var mainWindow = context.Launch();
        var settingsWindow = OpenSettings(context, mainWindow);
        AgentControlTestContext.InvokeAddSourceMenuItem(settingsWindow, "AddGitSourceButton");
        var editor = context.WaitForWindowWithDescendant("SourceGitPackageIdTextBox", TimeSpan.FromSeconds(15));
        Assert.NotNull(editor);
        Find(editor, "SourceAliasTextBox").AsTextBox().Text = "Repo";
        Find(editor, "SourceLocationTextBox").AsTextBox().Text = fixture.BarePath;
        Find(editor, "SourceTagPatternTextBox").AsTextBox().Text = "Release/{version}";

        // Act: test the connection with the wrong-case pattern
        var test = Find(editor, "SourceTestConnectionButton").AsButton();
        Assert.True(Retry.WhileFalse(() => test.IsEnabled, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200)).Result);
        test.Invoke();

        // Assert: nothing matches, and the hint names the tags the repository does have
        Assert.True(WaitForText(editor, "SourceTestResultTitleText", "No matching tags"));
        var detail = Find(editor, "SourceTestResultDetailText").Name;
        Assert.Contains("release/1.0.0", detail, StringComparison.Ordinal);
        Assert.Contains("case-sensitive", detail, StringComparison.Ordinal);
        Assert.Null(editor.FindFirstDescendant(cf => cf.ByAutomationId("SourceTagPreviewPanel")));

        // Act: fix the pattern and test again
        Find(editor, "SourceTagPatternTextBox").AsTextBox().Text = "release/{version}";
        Assert.True(Retry.WhileFalse(() => test.IsEnabled, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200)).Result);
        test.Invoke();

        // Assert: the connection is fine and the matching tags are previewed
        Assert.True(WaitForText(editor, "SourceTestResultTitleText", "Connection OK"));
        Assert.True(ContainsName(Find(editor, "SourceTagPreviewList"), "1.1.0   (tag release/1.1.0)"));
    }

    /// <summary>
    ///     An address that carries a credential (a password in the URL) is refused by the Git source editor with
    ///     a message that does not repeat it: the source cannot be saved, and the settings file never holds the
    ///     secret.
    /// </summary>
    [Fact]
    public void SettingsWindow_AddGitRepositoryWithCredentialInAddress_IsRefusedAndNothingIsSaved()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        using var fixture = GitFixtureRepository.CreateOrSkip();
        using var context = new AgentControlTestContext(useRealGit: true)
        {
            LaunchEnvironment = fixture.IsolationEnvironment
        };
        var mainWindow = context.Launch();
        var settingsWindow = OpenSettings(context, mainWindow);
        Find(settingsWindow, "AddSourceButton").AsButton().Invoke();
        Find(settingsWindow, "AddGitSourceButton").AsMenuItem().Invoke();
        var editor = context.WaitForWindowWithDescendant("SourceGitPackageIdTextBox", TimeSpan.FromSeconds(15));
        Assert.NotNull(editor);

        // Act: type an address with a password in it, and a valid alias and pattern
        Find(editor, "SourceAliasTextBox").AsTextBox().Text = "Repo";
        Find(editor, "SourceTagPatternTextBox").AsTextBox().Text = "release/{version}";
        Find(editor, "SourceLocationTextBox").AsTextBox().Text = "https://user:SentinelSecretValue@example.invalid/org/agents.git";

        // Assert: the address is refused with a message that does not repeat the secret, and cannot be saved
        Assert.True(Retry.WhileFalse(
            () => !string.IsNullOrWhiteSpace(
                editor.FindFirstDescendant(cf => cf.ByAutomationId("SourceLocationErrorText"))?.Name),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.DoesNotContain(
            "SentinelSecretValue", Find(editor, "SourceLocationErrorText").Name, StringComparison.Ordinal);
        Assert.False(Find(editor, "SourceEditorSaveButton").AsButton().IsEnabled);

        // Act: leave the editor and save the settings
        Find(editor, "SourceEditorCancelButton").AsButton().Invoke();
        Find(settingsWindow, "SaveSettingsButton").AsButton().Invoke();

        // Assert: no source was added and the secret is in no file the application wrote
        Assert.True(Retry.WhileFalse(
            () => File.Exists(Path.Combine(context.ConfigDirectory, "settings.json")),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        foreach (var file in Directory.EnumerateFiles(context.RootDirectory, "*", SearchOption.AllDirectories))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            Assert.DoesNotContain("SentinelSecretValue", reader.ReadToEnd(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     A never-pinned repo is offered "Select Package..." (not "Change package..."); choosing a version
    ///     of the pre-configured Git source applies that tag's files, records the source, package, version
    ///     and the tag's commit in the pin file, and the card shows the Git source icon and alias.
    /// </summary>
    [Fact]
    public void SelectPackage_FromGitSource_PinsTagCommitAndCardShowsGitSourceChip()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        using var fixture = GitFixtureRepository.CreateOrSkip();
        var tagCommit = fixture.ResolveTag("release/1.0.0");
        using var context = new AgentControlTestContext(gitSource: fixture.CreateSource())
        {
            LaunchEnvironment = fixture.IsolationEnvironment
        };
        var mainWindow = context.Launch();

        // Assert: an unpinned repo is offered Select Package, not Change package
        Assert.NotNull(Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("NoPackageChip")),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        var selectPackageItem = OpenCardMenuItem(mainWindow, "SelectPackageMenuItem");
        Assert.Null(mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ChangePackageMenuItem")));

        // Act: pick the package and the older of the two versions in the dialog
        selectPackageItem.AsMenuItem().Invoke();
        var dialog = context.WaitForWindowWithDescendant("SelectPackageNameList", TimeSpan.FromSeconds(15));
        Assert.NotNull(dialog);
        SelectListItem(dialog, "SelectPackageNameList", GitFixtureRepository.PackageName);
        SelectListItem(dialog, "SelectPackageVersionList", "1.0.0");
        var confirm = Find(dialog, "SelectPackageConfirmButton").AsButton();
        Assert.True(Retry.WhileFalse(() => confirm.IsEnabled, TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(200)).Result);
        confirm.Invoke();

        // Assert: the release notes of the applied version open
        var releaseNotes = context.WaitForWindow(
            title => title.StartsWith("Release Notes", StringComparison.Ordinal), TimeSpan.FromSeconds(30));
        Assert.NotNull(releaseNotes);

        // Assert: the repo holds the tag's files
        Assert.Equal("git 1.0.0", File.ReadAllText(Path.Combine(context.RepoPath, ".github", "agents", "git.md")));
        Assert.True(File.Exists(Path.Combine(context.RepoPath, ".github", "standards", "git.md")));
        Assert.True(File.Exists(Path.Combine(context.RepoPath, ".github", "templates", "git.md")));
        Assert.True(File.Exists(Path.Combine(context.RepoPath, ".github", "skills", "git.md")));

        // Assert: the pin names the source, package, version and the commit the tag points to
        using var pin = JsonDocument.Parse(File.ReadAllText(Path.Combine(context.RepoPath, ".agentcontrol.json")));
        Assert.Equal("Repo", pin.RootElement.GetProperty("Source").GetString());
        Assert.Equal(GitFixtureRepository.PackageName, pin.RootElement.GetProperty("PackageName").GetString());
        Assert.Equal("1.0.0", pin.RootElement.GetProperty("Version").GetString());
        Assert.Equal("git-commit:" + tagCommit, pin.RootElement.GetProperty("SourceRevision").GetString());
        Assert.Equal(GitFixtureRepository.FilesIdentity("git 1.0.0"), pin.RootElement.GetProperty("ContentIdentity").GetString());

        // Assert: the card's package chip shows the package and version, the Git icon and the alias
        Assert.True(WaitForText(mainWindow, "RepoPinnedVersionText", $"{GitFixtureRepository.PackageName} 1.0.0"));
        Assert.True(WaitForText(mainWindow, "PackageSourceAliasText", "Repo"));
        Assert.Null(mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("NoPackageChip")));

        // The icon is purely decorative, so it is not in the UI Automation tree: it is seen as ink drawn
        // just left of the alias, once the release notes window no longer covers the card
        releaseNotes.Close();
        mainWindow.SetForeground();
        var alias = Find(mainWindow, "PackageSourceAliasText").BoundingRectangle;
        var iconArea = new System.Drawing.Rectangle(alias.Left - 22, alias.Top, 20, alias.Height);
        Assert.True(Retry.WhileFalse(
            () => RenderedTextProbe.CountInkPixels(iconArea) > 20,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(300)).Result, "The Git source icon is not drawn beside the alias.");
    }

    /// <summary>
    ///     A repo pinned to a Git tag that was later moved to another commit shows the "Tag moved" chip
    ///     once the background check has run, and nothing is applied: the repo's files and the pin file stay
    ///     as they were. The pinned repo is offered "Change package..." and no longer "Select Package...".
    /// </summary>
    [Fact]
    public void RepoCard_PinnedGitTagMoved_ShowsTagMovedChipAndAppliesNothing()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: a repo pinned to 1.0.0 with the identity of the original commit, whose tag has since moved
        using var fixture = GitFixtureRepository.CreateOrSkip();
        var originalCommit = fixture.ResolveTag("release/1.0.0");
        var movedCommit = fixture.MoveTag("release/1.0.0", "git 1.0.0 moved");
        Assert.NotEqual(originalCommit, movedCommit);
        using var context = CreatePinnedContext(fixture, originalCommit);
        var pinPath = Path.Combine(context.RepoPath, ".agentcontrol.json");
        var pinBefore = File.ReadAllText(pinPath);

        // Act: start the application, whose background check lists the tags
        var mainWindow = context.Launch();

        // Assert: the chip appears and says so
        var chip = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("TagMovedChip")),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(200)).Result;
        Assert.NotNull(chip);
        Assert.True(WaitForText(mainWindow, "TagMovedBadge", "Tag moved"));

        // Assert: the warning applied nothing (no files, no release notes, the pin file as it was)
        Assert.False(Directory.Exists(Path.Combine(context.RepoPath, ".github")));
        Assert.Equal(pinBefore, File.ReadAllText(pinPath));
        Assert.Null(context.WaitForWindow(
            title => title.StartsWith("Release Notes", StringComparison.Ordinal), TimeSpan.FromSeconds(2)));

        // Assert: a pinned repo is offered Change package, not Select Package
        Assert.NotNull(OpenCardMenuItem(mainWindow, "ChangePackageMenuItem"));
        Assert.Null(mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SelectPackageMenuItem")));
        Assert.Equal(pinBefore, File.ReadAllText(pinPath));
    }

    /// <summary>
    ///     "Change package..." opens the Select Package dialog on the pin's current source, package and
    ///     version; choosing that same version, whose tag has moved, asks for confirmation, and answering No
    ///     leaves the repo's files and the pin file unchanged.
    /// </summary>
    [Fact]
    public void ChangePackageMenuItem_SameVersionWithMovedTag_AsksAndDecliningLeavesRepoUnchanged()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        using var fixture = GitFixtureRepository.CreateOrSkip();
        var originalCommit = fixture.ResolveTag("release/1.0.0");
        fixture.MoveTag("release/1.0.0", "git 1.0.0 moved");
        using var context = CreatePinnedContext(fixture, originalCommit);
        var agentFile = SeedOriginalFiles(context);
        var pinPath = Path.Combine(context.RepoPath, ".agentcontrol.json");
        var pinBefore = File.ReadAllText(pinPath);
        var mainWindow = context.Launch();
        Assert.NotNull(Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("TagMovedChip")),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(200)).Result);

        // Act: open Change package and choose what is already selected
        var dialog = OpenChangePackageDialog(context, mainWindow);
        var confirm = Find(dialog, "SelectPackageConfirmButton").AsButton();
        Assert.True(Retry.WhileFalse(() => confirm.IsEnabled, TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(200)).Result);
        confirm.Invoke();

        // Assert: the changed content is explained and a decision is asked for
        var confirmation = context.WaitForWindowWithDescendant("ConfirmationMessageText", TimeSpan.FromSeconds(30));
        Assert.NotNull(confirmation);
        var question = Find(confirmation, "ConfirmationMessageText").Name;
        Assert.Contains("now contains different files", question, StringComparison.Ordinal);
        Assert.Contains("Choose Yes only if you trust the change", question, StringComparison.Ordinal);

        // Act: decline
        Find(confirmation, "ConfirmationNoButton").AsButton().Invoke();

        // Assert: the dialog is gone and nothing changed - files, pin and the moved-tag warning
        Assert.True(Retry.WhileFalse(
            () => context.WaitForWindowWithDescendant("ConfirmationMessageText", TimeSpan.FromMilliseconds(200)) is null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Null(context.WaitForWindow(
            title => title.StartsWith("Release Notes", StringComparison.Ordinal), TimeSpan.FromSeconds(2)));
        Assert.Equal("git 1.0.0", File.ReadAllText(agentFile));
        Assert.False(Directory.Exists(Path.Combine(context.RepoPath, ".github", "skills")));
        Assert.Equal(pinBefore, File.ReadAllText(pinPath));
        Assert.NotNull(mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("TagMovedChip")));
    }

    /// <summary>
    ///     Answering Yes to the confirmation of "Change package..." on the same version with a moved tag
    ///     applies the tag's new content, records the new commit as the pin's content identity, and the
    ///     "Tag moved" chip goes away.
    /// </summary>
    [Fact]
    public void ChangePackageMenuItem_SameVersionWithMovedTag_ConfirmingAppliesNewContentAndRecordsNewCommit()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        using var fixture = GitFixtureRepository.CreateOrSkip();
        var originalCommit = fixture.ResolveTag("release/1.0.0");
        var movedCommit = fixture.MoveTag("release/1.0.0", "git 1.0.0 moved");
        using var context = CreatePinnedContext(fixture, originalCommit);
        var agentFile = SeedOriginalFiles(context);
        var mainWindow = context.Launch();
        Assert.NotNull(Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("TagMovedChip")),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(200)).Result);

        // Act: open Change package, choose what is already selected and confirm the changed content
        var dialog = OpenChangePackageDialog(context, mainWindow);
        var confirm = Find(dialog, "SelectPackageConfirmButton").AsButton();
        Assert.True(Retry.WhileFalse(() => confirm.IsEnabled, TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(200)).Result);
        confirm.Invoke();
        var confirmation = context.WaitForWindowWithDescendant("ConfirmationMessageText", TimeSpan.FromSeconds(30));
        Assert.NotNull(confirmation);
        Find(confirmation, "ConfirmationYesButton").AsButton().Invoke();

        // Assert: the new content is applied and its release notes open
        Assert.NotNull(context.WaitForWindow(
            title => title.StartsWith("Release Notes", StringComparison.Ordinal), TimeSpan.FromSeconds(30)));
        Assert.Equal("git 1.0.0 moved", File.ReadAllText(agentFile));
        Assert.True(File.Exists(Path.Combine(context.RepoPath, ".github", "skills", "git.md")));

        // Assert: the pin keeps its version and records the commit the tag points to now
        using (var pin = JsonDocument.Parse(File.ReadAllText(Path.Combine(context.RepoPath, ".agentcontrol.json"))))
        {
            Assert.Equal("1.0.0", pin.RootElement.GetProperty("Version").GetString());
            Assert.Equal("Repo", pin.RootElement.GetProperty("Source").GetString());
            Assert.Equal("git-commit:" + movedCommit, pin.RootElement.GetProperty("SourceRevision").GetString());
            Assert.Equal(GitFixtureRepository.FilesIdentity("git 1.0.0 moved"), pin.RootElement.GetProperty("ContentIdentity").GetString());
        }

        // Assert: the warning is gone, because the pin now matches the tag
        Assert.True(Retry.WhileNotNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("TagMovedChip")),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromMilliseconds(200)).Success);
    }

    /// <summary>
    ///     Creates a test context for a repo pinned to version 1.0.0 of the fixture's Git source with the
    ///     identity of the given commit, with the real git isolated from the user's configuration.
    /// </summary>
    /// <param name="fixture">The Git fixture the source points at.</param>
    /// <param name="commit">The commit the pin records as the delivered content.</param>
    /// <returns>The context, not yet launched.</returns>
    private static AgentControlTestContext CreatePinnedContext(GitFixtureRepository fixture, string commit) =>
        new(
            pinnedPackageName: GitFixtureRepository.PackageName,
            pinnedPackageVersion: "1.0.0",
            pinnedSource: "Repo",
            pinnedContentIdentity: GitFixtureRepository.FilesIdentity("git 1.0.0"),
            pinnedSourceRevision: "git-commit:" + commit,
            gitSource: fixture.CreateSource())
        {
            LaunchEnvironment = fixture.IsolationEnvironment
        };

    /// <summary>
    ///     Writes the agent file the repo got from version 1.0.0 as originally delivered, so a test can tell
    ///     whether it was replaced.
    /// </summary>
    /// <param name="context">The context whose repo receives the file.</param>
    /// <returns>The path of the file.</returns>
    private static string SeedOriginalFiles(AgentControlTestContext context)
    {
        var agents = Path.Combine(context.RepoPath, ".github", "agents");
        Directory.CreateDirectory(agents);
        var agentFile = Path.Combine(agents, "git.md");
        File.WriteAllText(agentFile, "git 1.0.0");
        return agentFile;
    }

    /// <summary>
    ///     Opens the card's "Change package..." dialog and checks that it opens on the pin's current
    ///     source, package and version.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <param name="mainWindow">The main window.</param>
    /// <returns>The dialog window.</returns>
    private static Window OpenChangePackageDialog(AgentControlTestContext context, Window mainWindow)
    {
        OpenCardMenuItem(mainWindow, "ChangePackageMenuItem").AsMenuItem().Invoke();
        var dialog = context.WaitForWindowWithDescendant("SelectPackageNameList", TimeSpan.FromSeconds(15));
        Assert.NotNull(dialog);

        Assert.True(WaitForSelected(dialog, "SelectPackageSourceList", "Repo"), "The source is not preselected.");
        Assert.True(
            WaitForSelected(dialog, "SelectPackageNameList", GitFixtureRepository.PackageName),
            "The package is not preselected.");
        Assert.True(WaitForSelected(dialog, "SelectPackageVersionList", "1.0.0"), "The version is not preselected.");
        return dialog;
    }

    /// <summary>
    ///     Opens the card's "..." menu and finds one of its items. Under load the first invoke can land
    ///     before the card's menu is ready, so the menu is opened a few times before giving up.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    /// <param name="menuItemAutomationId">The AutomationId of the wanted item.</param>
    /// <returns>The menu item.</returns>
    internal static AutomationElement OpenCardMenuItem(Window mainWindow, string menuItemAutomationId)
    {
        AutomationElement? item = null;
        for (var attempt = 0; attempt < 3 && item is null; attempt++)
        {
            Find(mainWindow, "MoreActionsButton").AsButton().Invoke();
            item = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId(menuItemAutomationId)),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(200)).Result;
        }

        Assert.NotNull(item);
        return item;
    }

    /// <summary>
    ///     Opens the Settings window from the main window.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <param name="mainWindow">The main window.</param>
    /// <returns>The Settings window.</returns>
    internal static Window OpenSettings(AgentControlTestContext context, Window mainWindow)
    {
        Find(mainWindow, "OpenSettingsButton").AsButton().Invoke();
        var settingsWindow = context.WaitForWindowWithDescendant("AddSourceButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(settingsWindow);
        return settingsWindow;
    }

    /// <summary>
    ///     Finds an element by AutomationId, waiting briefly for it to appear.
    /// </summary>
    /// <param name="parent">The window or element to search under.</param>
    /// <param name="automationId">The AutomationId to find.</param>
    /// <returns>The element.</returns>
    internal static AutomationElement Find(AutomationElement parent, string automationId)
    {
        var element = Retry.WhileNull(
            () => parent.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result;
        Assert.NotNull(element);
        return element;
    }

    /// <summary>
    ///     Waits until an element with the given AutomationId shows the given text.
    /// </summary>
    /// <param name="parent">The window or element to search under.</param>
    /// <param name="automationId">The AutomationId of the text element.</param>
    /// <param name="expected">The text expected.</param>
    /// <returns><see langword="true"/> when the text appeared in time.</returns>
    internal static bool WaitForText(AutomationElement parent, string automationId, string expected) =>
        Retry.WhileFalse(
            () => parent.FindAllDescendants(cf => cf.ByAutomationId(automationId))
                .Any(element => string.Equals(element.Name, expected, StringComparison.Ordinal)),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromMilliseconds(200)).Result;

    /// <summary>
    ///     Tells whether something inside an element (such as a list) is named with the given text.
    /// </summary>
    /// <param name="container">The element to search in.</param>
    /// <param name="text">The text to look for in the names of its descendants.</param>
    /// <returns><see langword="true"/> when a descendant's name contains the text.</returns>
    private static bool ContainsName(AutomationElement container, string text) =>
        container.FindAllDescendants()
            .Any(element => (element.Name ?? string.Empty).Contains(text, StringComparison.Ordinal));

    /// <summary>
    ///     Selects an item of a list in the Select Package dialog by its name.
    /// </summary>
    /// <param name="dialog">The dialog window.</param>
    /// <param name="listAutomationId">The AutomationId of the list.</param>
    /// <param name="itemName">The name of the item to select.</param>
    internal static void SelectListItem(AutomationElement dialog, string listAutomationId, string itemName)
    {
        var item = Retry.WhileNull(
            () => dialog.FindFirstDescendant(cf => cf.ByAutomationId(listAutomationId))
                ?.FindFirstDescendant(cf => cf.ByName(itemName)),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(200)).Result?.AsListBoxItem();
        Assert.NotNull(item);
        item.Select();
    }

    /// <summary>
    ///     Waits until a list of the Select Package dialog has the item with the given name selected. A
    ///     source list item is named by the text it holds, found below the item.
    /// </summary>
    /// <param name="dialog">The dialog window.</param>
    /// <param name="listAutomationId">The AutomationId of the list.</param>
    /// <param name="itemName">The name (or, for the source list, the alias) of the item.</param>
    /// <returns><see langword="true"/> when the item was selected in time.</returns>
    private static bool WaitForSelected(AutomationElement dialog, string listAutomationId, string itemName) =>
        Retry.WhileFalse(
            () => dialog.FindFirstDescendant(cf => cf.ByAutomationId(listAutomationId))
                ?.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem))
                .Any(item => item.AsListBoxItem().IsSelected &&
                             (string.Equals(item.Name, itemName, StringComparison.Ordinal) ||
                              item.FindFirstDescendant(cf => cf.ByName(itemName)) is not null)) ?? false,
            TimeSpan.FromSeconds(20),
            TimeSpan.FromMilliseconds(200)).Result;
}
