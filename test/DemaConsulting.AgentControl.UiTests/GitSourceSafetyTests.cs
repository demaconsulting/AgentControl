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

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     End-to-end tests of what a hostile Git repository can do to a machine that uses it as a package
///     source: nothing. Each test builds a real local bare Git repository (see
///     <see cref="GitFixtureRepository"/>; skipped when git is not installed, failed when the environment
///     variable <c>CI</c> is set) and drives the published application, with the real <c>git</c>, through the
///     same windows a user uses.
/// </summary>
/// <remarks>
///     The user's own git configuration is the other half of the threat, so one test replaces it with a
///     configuration that would run a hook and a filter program for any repository, and shows with plain git
///     that it really would, before it shows that the application runs neither. Nothing touches the real
///     user profile, a credential store or a network.
/// </remarks>
public sealed class GitSourceSafetyTests
{
    /// <summary>
    ///     A tag whose package layout holds a symbolic link in a managed folder is refused when the user chooses
    ///     it in Select Package: the user is told why in plain language, the repo gets no files and no pin, and
    ///     nothing of the tag stays in the application's cache.
    /// </summary>
    [Fact]
    public void SelectPackage_FromGitRepositoryWithLinkInLayout_IsRefusedAndWritesNothing()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        RefuseHostileLayout("symbolic-link", "a symbolic link");
    }

    /// <summary>
    ///     A tag whose package layout holds a submodule is refused when the user chooses it in Select Package:
    ///     the user is told why, the repo gets no files and no pin, and nothing of the tag stays in the cache.
    /// </summary>
    [Fact]
    public void SelectPackage_FromGitRepositoryWithSubmoduleInLayout_IsRefusedAndWritesNothing()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        RefuseHostileLayout("submodule", "a submodule");
    }

    /// <summary>
    ///     A tag whose package layout holds two files whose names differ only in letter case - which a
    ///     case-insensitive file system would merge into one - is refused when the user chooses it in Select
    ///     Package: the user is told why, the repo gets no files and no pin, and nothing of the tag stays in the
    ///     cache.
    /// </summary>
    [Fact]
    public void SelectPackage_FromGitRepositoryWithCaseCollidingNames_IsRefusedAndWritesNothing()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        RefuseHostileLayout("case-collision", "differ only in letter case");
    }

    /// <summary>
    ///     With a user configuration that would run a post-checkout hook and a filter program for every
    ///     repository (shown, with plain git, to really do so), choosing a good package whose attributes select
    ///     that filter, expand identifiers and convert line endings applies it normally and runs neither the
    ///     hook nor the filter: the files hold exactly the bytes of the commit.
    /// </summary>
    [Fact]
    public void SelectPackage_FromGitRepositoryUnderHostileUserConfiguration_RunsNoCodeAndAppliesRawBytes()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: a good package whose attributes select a filter, and a user environment that runs code for any repository
        using var fixture = GitFixtureRepository.CreateOrSkip();
        fixture.PublishVersionWithConversionAttributes("1.3.0");
        var hookMarker = Path.Combine(fixture.Root, "hook-ran.txt");
        var filterMarker = Path.Combine(fixture.Root, "filter-ran.txt");
        var hostile = fixture.CreateHostileEnvironment(hookMarker, filterMarker);

        // Control: plain git under the same environment does run both, so finding no marker later shows something
        if (!fixture.HostileEnvironmentRunsCodeForPlainGit(hostile, hookMarker, filterMarker, "release/1.3.0"))
        {
            Assert.Skip("Hooks and filters do not run in this environment, so their neutralization cannot be demonstrated.");
        }

        using var context = new AgentControlTestContext(gitSource: fixture.CreateSource())
        {
            LaunchEnvironment = hostile
        };
        var mainWindow = context.Launch();

        // Act: choose the version in Select Package
        ChooseVersion(context, mainWindow, "1.3.0");

        // Assert: applied (the release notes of the version open)
        Assert.NotNull(context.WaitForWindow(
            title => title.StartsWith("Release Notes", StringComparison.Ordinal), TimeSpan.FromSeconds(30)));

        // Assert: the files hold the bytes of the commit - no conversion, no expansion
        Assert.Equal(
            System.Text.Encoding.UTF8.GetBytes(GitFixtureRepository.CrlfContent),
            File.ReadAllBytes(Path.Combine(context.RepoPath, ".github", "standards", "crlf.md")));
        Assert.Equal(
            System.Text.Encoding.UTF8.GetBytes(GitFixtureRepository.IdentContent),
            File.ReadAllBytes(Path.Combine(context.RepoPath, ".github", "agents", "ident.md")));

        // Assert: neither the hook nor the filter ran, and nothing but the package layout reached the repo - no
        // repository metadata, and none of the repository's other content
        Assert.False(File.Exists(hookMarker), "A hook of the user's configuration ran.");
        Assert.False(File.Exists(filterMarker), "A filter program of the user's configuration ran.");
        Assert.False(Directory.Exists(Path.Combine(context.RepoPath, ".git")));
        Assert.False(File.Exists(Path.Combine(context.RepoPath, "README.md")));
        Assert.False(Directory.Exists(Path.Combine(context.RepoPath, "docs")));
    }

    /// <summary>
    ///     An address that asks git to run a program (a remote-helper address such as <c>ext::</c>) is refused
    ///     by the Git source editor - the error is shown, the source cannot be saved - and the program is never
    ///     run, so no marker it would write exists.
    /// </summary>
    [Fact]
    public void SettingsWindow_AddGitRepositoryWithRemoteHelperAddress_IsRefusedAndRunsNothing()
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
        var marker = Path.Combine(fixture.Root, "program-ran.txt").Replace('\\', '/');
        var mainWindow = context.Launch();
        var settingsWindow = GitSourceTests.OpenSettings(context, mainWindow);
        GitSourceTests.Find(settingsWindow, "AddSourceButton").AsButton().Invoke();
        GitSourceTests.Find(settingsWindow, "AddGitSourceButton").AsMenuItem().Invoke();
        var editor = context.WaitForWindowWithDescendant("SourceGitPackageIdTextBox", TimeSpan.FromSeconds(15));
        Assert.NotNull(editor);

        // Act: type an address that names a program to run, and a valid alias and pattern
        GitSourceTests.Find(editor, "SourceAliasTextBox").AsTextBox().Text = "Hostile";
        GitSourceTests.Find(editor, "SourceTagPatternTextBox").AsTextBox().Text = "release/{version}";
        GitSourceTests.Find(editor, "SourceLocationTextBox").AsTextBox().Text = $"ext::sh -c \"echo ran > {marker}\"";

        // Assert: the address is refused with a message, the source cannot be saved, and nothing ran
        Assert.True(Retry.WhileFalse(
            () => !string.IsNullOrWhiteSpace(
                editor.FindFirstDescendant(cf => cf.ByAutomationId("SourceLocationErrorText"))?.Name),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.False(GitSourceTests.Find(editor, "SourceEditorSaveButton").AsButton().IsEnabled);
        Assert.False(File.Exists(Path.Combine(fixture.Root, "program-ran.txt")));
    }

    /// <summary>
    ///     Publishes a hostile version beside the fixture's good ones, chooses it in Select Package and checks
    ///     that it is refused and that nothing was written or kept.
    /// </summary>
    /// <param name="kind">The kind of hostile layout (see <see cref="GitFixtureRepository.PublishHostileVersion"/>).</param>
    /// <param name="expectedPhrase">A phrase the refusal's explanation must contain.</param>
    private static void RefuseHostileLayout(string kind, string expectedPhrase)
    {
        // Arrange: a repository whose tag release/1.2.0 holds a hostile layout, and a never-pinned repo
        using var fixture = GitFixtureRepository.CreateOrSkip();
        var hostileCommit = fixture.PublishHostileVersion("1.2.0", kind);
        using var context = new AgentControlTestContext(gitSource: fixture.CreateSource())
        {
            LaunchEnvironment = fixture.IsolationEnvironment
        };
        var mainWindow = context.Launch();

        // Act: choose the hostile version in Select Package
        ChooseVersion(context, mainWindow, "1.2.0");

        // Assert: the refusal is explained, and the message names the problem
        var messageWindow = context.WaitForWindowWithDescendant("MessageText", TimeSpan.FromSeconds(60));
        Assert.NotNull(messageWindow);
        var message = GitSourceTests.Find(messageWindow, "MessageText").Name;
        Assert.Contains(expectedPhrase, message, StringComparison.Ordinal);

        // Assert: the repo got nothing - no files, no pin, no release notes
        Assert.Empty(Directory.GetFileSystemEntries(context.RepoPath));
        Assert.Null(context.WaitForWindow(
            title => title.StartsWith("Release Notes", StringComparison.Ordinal), TimeSpan.FromSeconds(1)));

        // Assert: nothing of the hostile tag stays in the application's cache, and nothing was written outside
        // the repo and the configuration folder
        Assert.Empty(Directory.EnumerateDirectories(context.ConfigDirectory, hostileCommit, SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateDirectories(context.ConfigDirectory, ".tmp-*", SearchOption.AllDirectories));
        Assert.False(File.Exists(Path.Combine(context.RootDirectory, "outside.txt")));
    }

    /// <summary>
    ///     Opens Select Package from the never-pinned repo's card, chooses the package and the version and
    ///     confirms.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <param name="mainWindow">The main window.</param>
    /// <param name="version">The version to choose, for example <c>1.2.0</c>.</param>
    private static void ChooseVersion(AgentControlTestContext context, Window mainWindow, string version)
    {
        GitSourceTests.OpenCardMenuItem(mainWindow, "SelectPackageMenuItem").AsMenuItem().Invoke();
        var dialog = context.WaitForWindowWithDescendant("SelectPackageNameList", TimeSpan.FromSeconds(15));
        Assert.NotNull(dialog);
        GitSourceTests.SelectListItem(dialog, "SelectPackageNameList", GitFixtureRepository.PackageName);
        GitSourceTests.SelectListItem(dialog, "SelectPackageVersionList", version);
        var confirm = GitSourceTests.Find(dialog, "SelectPackageConfirmButton").AsButton();
        Assert.True(Retry.WhileFalse(
            () => confirm.IsEnabled, TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(200)).Result);
        confirm.Invoke();
    }
}
