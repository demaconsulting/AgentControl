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

using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.RepoConfig;
using DemaConsulting.AgentControl.RepoSync;
using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Headless tests for <see cref="MainWindow"/>, <see cref="SelectPackageWindow"/> and
///     <see cref="ReleaseNotesViewer"/> code-behind.
/// </summary>
/// <remarks>
///     Runs in the <c>RealProcess</c> collection because adding a repo card spawns real git
///     subprocesses as part of the card's eager refresh.
/// </remarks>
[Collection("RealProcess")]
public sealed class MainWindowTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that the About button opens About as a modal dialog owned by the main window, and
    ///     that closing it leaves nothing open.
    /// </summary>
    [Fact]
    public void MainWindow_AboutClicked_OpensModalAboutOwnedByMainWindow() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var window = CreateWindow(out _);
        window.Show();

        // Act: open About
        DialogWindowTests.Click(window, "OpenAboutButton");
        Dispatcher.UIThread.RunJobs();

        // Assert: owned by the main window, gone when closed
        var about = Assert.Single(window.OwnedWindows.OfType<AboutWindow>());
        about.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(window.OwnedWindows.OfType<AboutWindow>());
    });

    /// <summary>
    ///     Test that a card added after the data context is set is shown, refreshed when attached
    ///     to the visual tree, and can be removed via a (declined) confirmation flow.
    /// </summary>
    [Fact]
    public void MainWindow_RepoAdded_AttachesCardAndRemoveRequestShowsConfirmation() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var window = CreateWindow(out var viewModel);
        window.Show();

        // Act: add a repo after the data context was set (exercises collection-changed wiring)
        Assert.True(viewModel.AddRepo(CreateTempDirectory()));
        var card = Assert.Single(viewModel.RepoCards);

        // Act: request removal and decline the confirmation dialog
        card.RemoveCommand.Execute(null);
        var confirmation = Assert.Single(window.OwnedWindows.OfType<ConfirmationWindow>());
        DialogWindowTests.Click(confirmation, "ConfirmationNoButton");
        Dispatcher.UIThread.RunJobs();

        // Assert: declining keeps the card listed
        Assert.Single(viewModel.RepoCards);

        // Act: request removal again and accept
        card.RemoveCommand.Execute(null);
        confirmation = Assert.Single(window.OwnedWindows.OfType<ConfirmationWindow>());
        DialogWindowTests.Click(confirmation, "ConfirmationYesButton");
        Dispatcher.UIThread.RunJobs();

        // Assert: accepting removes the card
        Assert.Empty(viewModel.RepoCards);
        window.Close();
    });

    /// <summary>
    ///     Test that a card's select-package request (here failing because no package source is
    ///     configured) surfaces a modal message box that can be dismissed.
    /// </summary>
    [Fact]
    public void MainWindow_SelectPackageWithoutSource_ShowsMessageBox() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var window = CreateWindow(out var viewModel);
        window.Show();
        viewModel.AddRepo(CreateTempDirectory());

        // Act
        viewModel.RepoCards[0].SelectPackageCommand.Execute(null);

        // Assert: a message box or select-package dialog is owned by the main window; dismiss it
        var dialog = Assert.Single(window.OwnedWindows);
        dialog.Close();
        Dispatcher.UIThread.RunJobs();
        window.Close();
    });

    /// <summary>
    ///     Test that cards already present when the data context is assigned are wired up.
    /// </summary>
    [Fact]
    public void MainWindow_ExistingRepos_CardsWiredOnDataContextAssignment() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var settings = new AppSettings { RecentRepos = [new RecentRepo { Path = CreateTempDirectory() }] };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());
        var window = new MainWindow { DataContext = viewModel };

        // Act
        window.Show();
        window.Close();

        // Assert
        Assert.Single(viewModel.RepoCards);
    });

    /// <summary>
    ///     Test that a card that is removed is released by the window: its handlers are detached and
    ///     what the window remembered about it is dropped, so a removed card is not kept alive by the
    ///     window and can no longer show anything.
    /// </summary>
    [Fact]
    public void MainWindow_RepoRemoved_ReleasesTheCardHandlers() => HeadlessTestApp.Run(() =>
    {
        // Arrange: a window with two cards
        var window = CreateWindow(out var viewModel);
        window.Show();
        Assert.True(viewModel.AddRepo(CreateTempDirectory()));
        Assert.True(viewModel.AddRepo(CreateTempDirectory()));
        Assert.Equal(2, window.TrackedCardCount);
        var removed = viewModel.RepoCards[0];

        // Act
        viewModel.RemoveRepo(removed);

        // Assert: only the card that is still listed is tracked
        Assert.Equal(1, window.TrackedCardCount);
        window.Close();
    });

    /// <summary>
    ///     Test that a change that cannot be saved is reported in a message box and leaves the card as
    ///     it was (here a favorite that could not be saved), and that a removal that cannot be saved
    ///     keeps the repo listed and says so.
    /// </summary>
    [Fact]
    public void MainWindow_SettingsCannotBeSaved_ShowsMessageAndKeepsStateAsItWas() => HeadlessTestApp.Run(() =>
    {
        // Arrange: a configuration "directory" that is really a file, so every save fails
        var blockingFile = Path.Combine(CreateTempDirectory(), "not-a-directory");
        File.WriteAllText(blockingFile, "blocker");
        var settings = new AppSettings { RecentRepos = [new RecentRepo { Path = CreateTempDirectory() }] };
        var viewModel = new MainWindowViewModel(settings, configDirectory: blockingFile);
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        var card = Assert.Single(viewModel.RepoCards);

        // Act: mark a favorite
        card.IsFavorite = true;
        Dispatcher.UIThread.RunJobs();

        // Assert: a message was shown and nothing changed
        var message = Assert.Single(window.OwnedWindows.OfType<MessageBoxWindow>());
        Assert.False(card.IsFavorite);
        Assert.False(settings.RecentRepos[0].IsFavorite);
        message.Close();
        Dispatcher.UIThread.RunJobs();

        // Act: confirm removing the repo
        card.RemoveCommand.Execute(null);
        var confirmation = Assert.Single(window.OwnedWindows.OfType<ConfirmationWindow>());
        DialogWindowTests.Click(confirmation, "ConfirmationYesButton");
        Dispatcher.UIThread.RunJobs();

        // Assert: the repo is still listed and the user was told it was not removed
        Assert.Single(viewModel.RepoCards);
        Assert.Single(settings.RecentRepos);
        var removalMessage = Assert.Single(window.OwnedWindows.OfType<MessageBoxWindow>());
        removalMessage.Close();
        Dispatcher.UIThread.RunJobs();
        window.Close();
    });

    /// <summary>
    ///     Test that the card menu-item handlers route through the card's commands.
    /// </summary>
    [Fact]
    public void MainWindow_MenuItemHandlers_RouteThroughCardCommands() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var window = CreateWindow(out var viewModel);
        window.Show();
        viewModel.AddRepo(CreateTempDirectory());
        var card = viewModel.RepoCards[0];
        var removeRequested = false;
        card.RemoveRequested += (_, _) => removeRequested = true;
        var menuItem = new MenuItem { DataContext = card };

        // Act
        InvokeStatic("RemoveMenuItem_Click", menuItem);
        InvokeStatic("SelectPackageMenuItem_Click", menuItem);
        InvokeStatic("RemoveMenuItem_Click", new MenuItem());
        InvokeStatic("SelectPackageMenuItem_Click", new MenuItem());

        // Assert
        Assert.True(removeRequested);
        foreach (var owned in window.OwnedWindows.ToArray())
        {
            owned.Close();
        }

        window.Close();
    });

    /// <summary>
    ///     Test that the button handlers do nothing when no view model is bound.
    /// </summary>
    [Fact]
    public void MainWindow_NoViewModel_ButtonHandlersAreNoOps() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var window = new MainWindow();
        window.Show();

        // Act
        DialogWindowTests.Click(window, "AddRepoButton");
        DialogWindowTests.Click(window, "OpenSettingsButton");

        // Assert
        Assert.Null(window.DataContext);
        window.Close();
    });

    /// <summary>
    ///     Test that the select-package window returns null on Cancel.
    /// </summary>
    [Fact]
    public void SelectPackageWindow_CancelClicked_ReturnsNull() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var owner = new Window();
        owner.Show();
        try
        {
            var registry = PackageSourceRegistry.Create(
                [PackageSourceDefinition.CreateDefaultZip(CreateTempDirectory())]);
            var viewModel = new SelectPackageWindowViewModel(registry, new PackageVersionCache());
            var dialog = new SelectPackageWindow(viewModel);
            var result = dialog.ShowDialog<PackageReference?>(owner);

            // Act
            DialogWindowTests.Click(dialog, "SelectPackageCancelButton");

            // Assert
            Assert.Null(await result);
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that the select-package window rejects a null view model and supports the
    ///     parameterless previewer constructor.
    /// </summary>
    [Fact]
    public void SelectPackageWindow_Constructors_NullViewModelThrows_ParameterlessWorks() => HeadlessTestApp.Run(() =>
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new SelectPackageWindow(null!));
        new SelectPackageWindow().Close();
    });

    /// <summary>
    ///     Test that the release-notes viewer renders one text block per parsed block.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewer_Constructed_RendersBlocks() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var viewer = new ReleaseNotesViewer(
            new ReleaseNotesViewerViewModel("Repo", "# Title\n\n- item **bold**\n\nplain *italic*"));

        // Act
        viewer.Show();

        // Assert
        Assert.Equal(3, viewer.FindControl<StackPanel>("ReleaseNotesPanel")!.Children.Count);
        Assert.Throws<ArgumentNullException>(() => new ReleaseNotesViewer(null!));
        new ReleaseNotesViewer().Close();
        viewer.Close();
    });

    /// <summary>
    ///     Test that the application bootstrap initializes under the headless platform.
    /// </summary>
    [Fact]
    public void App_Initialized_ApplicationIsAgentControlApp() => HeadlessTestApp.Run(() =>
        Assert.IsType<App>(Avalonia.Application.Current));

    /// <summary>
    ///     Test that "Change package..." on a pinned repo opens the Select Package dialog on the source
    ///     that currently supplies the pin's package (the card's remembered supplier - the pin names no
    ///     source) and on the pin's own package and version, so the current choice is selected without a click.
    /// </summary>
    [Fact]
    public void MainWindow_ChangePackageOnPinnedRepo_OpensSelectPackageOnThePinnedChoice() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a second source with two versions and a repo pinned to the older one, supplied by it
        var sourceDir = CreateTempDirectory();
        foreach (var version in new[] { "1.0.0", "1.1.0" })
        {
            using var archive = System.IO.Compression.ZipFile.Open(
                Path.Combine(sourceDir, $"contoso-agents-{version}.zip"), System.IO.Compression.ZipArchiveMode.Create);
            using var writer = new StreamWriter(archive.CreateEntry(".github/agents/copilot.md").Open());
            writer.Write(version);
        }

        var repo = CreateTempDirectory();
        RepoPinStore.Save(repo, new RepoPin { PackageName = "contoso-agents", Version = "1.0.0" });
        var settings = new AppSettings
        {
            Sources =
            [
                PackageSourceDefinition.CreateDefaultZip(CreateTempDirectory()),
                new PackageSourceDefinition { Alias = "Second", Type = PackageSourceType.Zip, Location = sourceDir }
            ],
            RecentRepos =
            [
                new RecentRepo
                {
                    Path = repo, PinnedPackageName = "contoso-agents", PinnedPackageVersion = "1.0.0", PinnedSource = "Second"
                }
            ]
        };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        try
        {
            var card = Assert.Single(viewModel.RepoCards);
            Assert.True(card.HasPinnedPackage);

            // Act: Change package...
            card.SelectPackageCommand.Execute(null);
            var dialog = Assert.Single(window.OwnedWindows.OfType<SelectPackageWindow>());
            var dialogModel = Assert.IsType<SelectPackageWindowViewModel>(dialog.DataContext);
            for (var attempt = 0; attempt < 1200 && dialogModel.SelectedVersion is null; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(25, TestContext.Current.CancellationToken);
            }

            // Assert: the pin's choice is selected, although a newer version exists
            Assert.Equal("Second", dialogModel.SelectedSource?.Alias);
            Assert.Equal("contoso-agents", dialogModel.SelectedPackageName);
            Assert.Equal("1.0.0", dialogModel.SelectedVersion);
            dialog.Close();
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            window.Close();
        }
    });

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var path in _tempPaths.Where(Directory.Exists))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private MainWindow CreateWindow(out MainWindowViewModel viewModel)
    {
        viewModel = new MainWindowViewModel(new AppSettings(), configDirectory: CreateTempDirectory());
        return new MainWindow { DataContext = viewModel };
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AgentControlHeadless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }

    private static void InvokeStatic(string methodName, object sender) =>
        typeof(MainWindow)
            .GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [sender, new RoutedEventArgs()]);
}
