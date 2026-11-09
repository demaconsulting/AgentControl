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

using Avalonia.Controls;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Settings;
using Material.Icons;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Tests of the Git-repository parts of the settings dialog: the "Git repository" item of the
///     Add menu, how a Git source is listed (icon, kind name, status), what removing one warns,
///     and that a Git source with unusable settings is refused on save.
/// </summary>
public class SettingsWindowGitSourceTests
{
    /// <summary>
    ///     A valid https repository address.
    /// </summary>
    private const string RepositoryAddress = "https://git.example.com/org/agents.git";

    /// <summary>
    ///     Test that the "Git repository" Add action asks for an editor in Git mode for a new source.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_AddGitSourceCommand_RequestsGitEditor()
    {
        // Arrange
        var viewModel = new SettingsWindowViewModel(new AppSettings(), _ => { }, (_, _) => Task.FromResult(true));
        SourceEditorViewModel? requested = null;
        viewModel.EditorRequested += (_, editor) => requested = editor;

        // Act
        viewModel.AddGitSourceCommand.Execute(null);

        // Assert
        Assert.NotNull(requested);
        Assert.Equal(PackageSourceType.Git, requested.Type);
        Assert.True(requested.IsGit);
        Assert.True(requested.IsNew);
        Assert.Equal("Add Git Repository", requested.Title);
    }

    /// <summary>
    ///     Test that the Add menu of the settings window holds a "Git repository" item with the Git
    ///     icon after the three existing ones, and that choosing it opens a Git editor.
    /// </summary>
    [Fact]
    public void SettingsWindow_AddGitSourceButton_IsInTheAddMenuAndOpensGitEditor() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var viewModel = new SettingsWindowViewModel(new AppSettings(), _ => { }, (_, _) => Task.FromResult(true));
        var settingsWindow = new SettingsWindow(viewModel);
        settingsWindow.Show();
        try
        {
            var addButton = settingsWindow.FindControl<Button>("AddSourceButton");
            Assert.NotNull(addButton);
            var menu = Assert.IsType<MenuFlyout>(addButton.Flyout);
            var items = menu.Items.OfType<MenuItem>().ToList();

            // Assert: the Git item is in the menu, with an icon and its header
            Assert.Contains("AddGitSourceButton", items.Select(item => item.Name));
            var git = settingsWindow.FindControl<MenuItem>("AddGitSourceButton")!;
            Assert.NotNull(git.Icon);
            var header = Assert.IsType<StackPanel>(git.Header);
            Assert.Equal("Git repository", header.Children.OfType<TextBlock>().First().Text);

            // Act
            SourceEditorViewModel? requested = null;
            viewModel.EditorRequested += (_, editor) => requested = editor;
            DialogWindowTests.ClickMenuItem(settingsWindow, "AddSourceButton", "AddGitSourceButton");

            // Assert
            Assert.NotNull(requested);
            Assert.Equal(PackageSourceType.Git, requested.Type);
        }
        finally
        {
            settingsWindow.Close();
        }
    });

    /// <summary>
    ///     Test that a Git source reached over the network is listed with the Git icon and kind name,
    ///     is "Not checked" (neither a folder check nor a connection is made for it), and that a
    ///     saved Git source is written to the settings with its pattern, folder and package name.
    /// </summary>
    [Fact]
    public async Task SettingsWindowViewModel_SaveGitSource_ListsItAsNotCheckedAndPersistsIt()
    {
        // Arrange: a folder check that must never be asked about a remote repository
        var asked = new List<string>();
        AppSettings? saved = null;
        var viewModel = new SettingsWindowViewModel(
            new AppSettings(),
            settings => saved = settings,
            (path, _) =>
            {
                asked.Add(path);
                return Task.FromResult(true);
            });
        var editor = viewModel.CreateEditor(PackageSourceType.Git, null);
        editor.Alias = "Agents";
        editor.Location = RepositoryAddress;
        editor.TagPattern = "release/{version}";
        editor.Subfolder = "packaging";
        editor.GitPackageId = "contoso-agents";

        // Act
        editor.SaveCommand.Execute(null);
        await viewModel.WaitForStatusChecksAsync();
        viewModel.SaveCommand.Execute(null);

        // Assert: listed as a Git repository that is not checked
        var row = Assert.Single(viewModel.Sources);
        Assert.Equal(MaterialIconKind.Git, row.TypeIcon);
        Assert.Equal("Git repository", row.TypeName);
        Assert.Equal("Not checked", row.StatusText);
        Assert.Contains("repository", row.StatusTooltip, StringComparison.Ordinal);
        Assert.False(row.IsChecking);
        Assert.False(row.IsAvailable);
        Assert.False(row.IsMissing);
        Assert.Empty(asked);

        // Assert: persisted with its Git settings
        Assert.NotNull(saved);
        var source = Assert.Single(saved.Sources);
        Assert.Equal(PackageSourceType.Git, source.Type);
        Assert.Equal(RepositoryAddress, source.Location);
        Assert.Equal("release/{version}", source.TagPattern);
        Assert.Equal("packaging", source.Subfolder);
        Assert.Equal(["contoso-agents"], source.PackageIds);
    }

    /// <summary>
    ///     Test that a Git source whose address is a local path gets the folder status like a folder
    ///     source: found when the folder exists, not found when it does not.
    /// </summary>
    /// <param name="exists">Whether the folder check finds the folder.</param>
    /// <param name="expectedText">The status text expected.</param>
    [Theory]
    [InlineData(true, "Folder found")]
    [InlineData(false, "Folder not found")]
    public async Task SettingsWindowViewModel_GitSourceWithLocalPath_ShowsFolderStatus(bool exists, string expectedText)
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_git_row", "agents.git");
        var asked = new List<string>();
        var viewModel = new SettingsWindowViewModel(
            new AppSettings { Sources = [GitSource("Local", path)] },
            _ => { },
            (folder, _) =>
            {
                asked.Add(folder);
                return Task.FromResult(exists);
            });

        // Act
        await viewModel.WaitForStatusChecksAsync();

        // Assert
        var row = Assert.Single(viewModel.Sources);
        Assert.False(row.IsRemote);
        Assert.Equal(expectedText, row.StatusText);
        Assert.Equal(exists, row.IsAvailable);
        Assert.Equal(!exists, row.IsMissing);
        Assert.Equal([path], asked);
    }

    /// <summary>
    ///     Test that removing a Git source warns the same way as for any source: it names the source
    ///     and counts the repos pinned to it.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_RemoveGitSource_WarnsAboutPinnedRepos()
    {
        // Arrange
        var viewModel = new SettingsWindowViewModel(
            new AppSettings
            {
                Sources = [GitSource("Agents", RepositoryAddress)],
                RecentRepos = [new RecentRepo { Path = Path.GetTempPath(), PinnedPackageName = "p", PinnedSource = "Agents" }]
            },
            _ => { },
            (_, _) => Task.FromResult(true));
        var row = viewModel.Sources[0];

        // Act
        var warning = SettingsWindowViewModel.GetRemovalWarning(row);
        viewModel.RemoveSource(row);

        // Assert
        Assert.Contains("'Agents'", warning, StringComparison.Ordinal);
        Assert.Contains("1 repo is pinned", warning, StringComparison.Ordinal);
        Assert.Empty(viewModel.Sources);
    }

    /// <summary>
    ///     Test that a Git source with unusable settings (here from a hand-edited settings file: a
    ///     tag pattern without the version placeholder) is refused on save: the callback is not
    ///     invoked, the window does not report saved, and the reason is shown.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_Save_GitSourceWithBadTagPattern_IsRefusedWithErrorMessage()
    {
        // Arrange
        var saveCalled = false;
        var savedRaised = false;
        var source = GitSource("Agents", RepositoryAddress);
        source.TagPattern = "release";
        var viewModel = new SettingsWindowViewModel(
            new AppSettings { Sources = [source] },
            _ => saveCalled = true,
            (_, _) => Task.FromResult(true));
        viewModel.Saved += (_, _) => savedRaised = true;

        // Act
        viewModel.SaveCommand.Execute(null);

        // Assert
        Assert.False(saveCalled);
        Assert.False(savedRaised);
        Assert.True(viewModel.HasError);
        Assert.Contains("{version}", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that saving a Git source writes it to the settings with its address, tag pattern, folder
    ///     and package name.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_SaveGitSource_PersistsAddressPatternFolderAndPackageName()
    {
        // Arrange
        AppSettings? saved = null;
        var viewModel = new SettingsWindowViewModel(
            new AppSettings(), settings => saved = settings, (_, _) => Task.FromResult(true));
        var editor = viewModel.CreateEditor(PackageSourceType.Git, null);
        editor.Alias = "Agents";
        editor.Location = RepositoryAddress;
        editor.TagPattern = "release/{version}";
        editor.Subfolder = "packaging";
        editor.GitPackageId = "contoso-agents";

        // Act
        editor.SaveCommand.Execute(null);
        viewModel.SaveCommand.Execute(null);

        // Assert
        Assert.NotNull(saved);
        var source = Assert.Single(saved.Sources);
        Assert.Equal(PackageSourceType.Git, source.Type);
        Assert.Equal("Agents", source.Alias);
        Assert.Equal(RepositoryAddress, source.Location);
        Assert.Equal("release/{version}", source.TagPattern);
        Assert.Equal("packaging", source.Subfolder);
        Assert.Equal(["contoso-agents"], source.PackageIds);
    }

    /// <summary>
    ///     Test that a Git source whose address the application would not accept is refused on save,
    ///     with a message that does not repeat the address.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_Save_GitSourceWithBadAddress_IsRefusedWithErrorMessage()
    {
        // Arrange: a hand-edited address that embeds a credential
        var source = GitSource("Agents", "https://user:sentinel-secret@git.example.com/org/agents.git");

        // Act / Assert
        AssertSaveRefused(source, unwanted: "sentinel-secret");
    }

    /// <summary>
    ///     Test that a Git source whose folder inside the repository the application would not accept is
    ///     refused on save.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_Save_GitSourceWithBadSubfolder_IsRefusedWithErrorMessage()
    {
        // Arrange: a folder that leaves the repository
        var source = GitSource("Agents", RepositoryAddress);
        source.Subfolder = "../outside";

        // Act / Assert
        AssertSaveRefused(source, unwanted: "../outside");
    }

    /// <summary>
    ///     Test that a Git source whose package name the application would not accept is refused on save.
    /// </summary>
    [Fact]
    public void SettingsWindowViewModel_Save_GitSourceWithBadPackageName_IsRefusedWithErrorMessage()
    {
        // Arrange: a name with characters a package name may not have
        var source = GitSource("Agents", RepositoryAddress);
        source.PackageIds = ["bad name!"];

        // Act / Assert
        AssertSaveRefused(source, unwanted: "bad name!");
    }

    /// <summary>
    ///     Saves the settings with one source and checks that the save is refused: the callback is not
    ///     invoked, the window does not report saved, an explanation is shown and it does not repeat the
    ///     given text.
    /// </summary>
    /// <param name="source">The source with unusable settings.</param>
    /// <param name="unwanted">Text the explanation must not contain.</param>
    private static void AssertSaveRefused(PackageSourceDefinition source, string unwanted)
    {
        // Arrange
        var saveCalled = false;
        var savedRaised = false;
        var viewModel = new SettingsWindowViewModel(
            new AppSettings { Sources = [source] },
            _ => saveCalled = true,
            (_, _) => Task.FromResult(true));
        viewModel.Saved += (_, _) => savedRaised = true;

        // Act
        viewModel.SaveCommand.Execute(null);

        // Assert
        Assert.False(saveCalled);
        Assert.False(savedRaised);
        Assert.True(viewModel.HasError);
        Assert.NotEmpty(viewModel.ErrorMessage!);
        Assert.DoesNotContain(unwanted, viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Creates a valid Git source.
    /// </summary>
    /// <param name="alias">The alias.</param>
    /// <param name="location">The repository address.</param>
    /// <returns>The definition.</returns>
    private static PackageSourceDefinition GitSource(string alias, string location) => new()
    {
        Alias = alias,
        Type = PackageSourceType.Git,
        Location = location,
        TagPattern = "v{version}",
        PackageIds = ["contoso-agents"]
    };
}
