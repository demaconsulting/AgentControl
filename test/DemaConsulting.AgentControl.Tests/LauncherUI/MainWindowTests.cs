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

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    ///     Test that attaching a view model to the main window starts its periodic upgrade re-check
    ///     (its ticks posted to the UI thread) and that closing the window disposes the view model, which
    ///     stops it.
    /// </summary>
    [Fact]
    public void MainWindow_ViewModelAttachedThenWindowClosed_StartsPeriodicRecheckAndDisposesViewModel() => HeadlessTestApp.Run(() =>
    {
        // Arrange: a view model on a fake clock with a pinned repo and an in-memory source, attached to a window
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var feed = new Tests.AgentPackageManagement.FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"));
        var repo = CreateTempDirectory();
        RepoPinStore.Save(repo, new RepoPin { PackageName = "contoso-agents", Version = "1.0.0" });
        var settings = new AppSettings
        {
            Sources = [new PackageSourceDefinition { Alias = "Feed", Type = PackageSourceType.Zip, Location = "unused" }],
            RecentRepos = [new RecentRepo { Path = repo }]
        };
        var viewModel = new MainWindowViewModel(
            settings,
            configDirectory: CreateTempDirectory(),
            timeProvider: clock,
            registryFactory: _ => new PackageSourceRegistry(settings.Sources, _ => feed));
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.False(viewModel.RepoCards[0].IsUpgradeAvailable);

        // Act: a version is published and the interval passes; the tick is posted to the UI thread
        feed.Publish("contoso-agents", "1.1.0");
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);
        Dispatcher.UIThread.RunJobs();

        // Assert: the window started the re-check, so the card shows the new version
        Assert.True(viewModel.RepoCards[0].IsUpgradeAvailable);

        // Act: the window is closed
        window.Close();
        Dispatcher.UIThread.RunJobs();

        // Assert: the view model was disposed, so the re-check can no longer be started
        Assert.Throws<ObjectDisposedException>(() => viewModel.StartPeriodicUpgradeRecheck());
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
        Assert.True(viewModel.AddRepo(CreateRepoDirectory()));
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
        viewModel.AddRepo(CreateRepoDirectory());

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
        Assert.True(viewModel.AddRepo(CreateRepoDirectory()));
        Assert.True(viewModel.AddRepo(CreateRepoDirectory()));
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
    ///     Test that the card's "Select Package..." and "Remove from list" menu items, clicked from the
    ///     card's open menu, route through the card's commands: Select Package reports the missing
    ///     source, and Remove asks for confirmation before removing anything.
    /// </summary>
    [Fact]
    public void MainWindow_MenuItemHandlers_RouteThroughCardCommands() => HeadlessTestApp.Run(() =>
    {
        // Arrange: a window with one card and no package source configured
        var window = CreateWindow(out var viewModel);
        window.Show();
        viewModel.AddRepo(CreateRepoDirectory());
        Dispatcher.UIThread.RunJobs();

        // Act: choose "Select Package..." from the card's menu
        ClickCardMenuItem(window, "SelectPackageMenuItem");

        // Assert: the card's command ran and reported that no source is configured
        var message = Assert.Single(window.OwnedWindows.OfType<MessageBoxWindow>());
        message.Close();
        Dispatcher.UIThread.RunJobs();

        // Act: choose "Remove from list" from the card's menu
        ClickCardMenuItem(window, "RemoveMenuItem");

        // Assert: the removal is confirmed first, and nothing is removed yet
        var question = Assert.Single(window.OwnedWindows.OfType<ConfirmationWindow>());
        Assert.Single(viewModel.RepoCards);
        question.Close();
        Dispatcher.UIThread.RunJobs();
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
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A card's background git check may still have the folder as its working directory
                // when the window closes; leaving a temporary folder behind is harmless
            }
        }
    }

    /// <summary>
    ///     Test that a card whose source needs a sign-in shows a clickable "Sign-in needed" chip (not the
    ///     plain problem chip) that says to click it, and that clicking it refreshes the card - reading the
    ///     source again - so the chip goes away once the source answers.
    /// </summary>
    [Fact]
    public void MainWindow_SignInNeededChipClicked_RefreshesTheCardAndClearsTheChip() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a window whose only card's background check found that its source needs a sign-in
        var feed = CreateSignInNeededFeed();
        var viewModel = CreateViewModelOver(feed, CreateTempDirectory());
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        try
        {
            await WaitUntilAsync(() => viewModel.PendingRefresh.IsCompleted);
            var card = Assert.Single(viewModel.RepoCards);
            Assert.True(card.SourceNeedsSignIn);
            Dispatcher.UIThread.RunJobs();
            var chip = Assert.Single(window.GetVisualDescendantsOf<Button>("SignInNeededChip"));
            var plainChip = Assert.Single(window.GetVisualDescendantsOf<Border>("SourceProblemChip"));
            Assert.True(chip.IsEffectivelyVisible);
            Assert.True(chip.IsEffectivelyEnabled);
            Assert.False(plainChip.IsVisible);
            Assert.Equal("Sign-in needed", AutomationProperties.GetName(chip));
            Assert.Contains("Click \"Sign-in needed\"", Assert.IsType<string>(ToolTip.GetTip(chip)), StringComparison.Ordinal);
            Assert.Contains("Refresh", Assert.IsType<string>(ToolTip.GetTip(chip)), StringComparison.Ordinal);
            var callsBefore = feed.ListVersionsCalls;

            // Act: the source answers again, and the user clicks the chip
            feed.ListVersionsException = null;
            feed.Publish("contoso-agents", "1.1.0");
            ClickWithMouse(window, chip);
            await WaitUntilAsync(() => !card.IsBusy);

            // Assert: the card was refreshed (its source read again) and the chip is gone
            Assert.True(feed.ListVersionsCalls > callsBefore);
            Assert.False(card.SourceNeedsSignIn);
            Assert.True(card.IsUpgradeAvailable);
            Assert.False(chip.IsVisible);
            Assert.False(plainChip.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that clicking the "Sign-in needed" chip and then not signing in - canceling or closing the
    ///     sign-in dialog, or typing a sign-in the server rejects - leaves the same clickable chip with the
    ///     same tooltip, and the card says it needs attention rather than that its update check failed.
    /// </summary>
    /// <param name="userCanceled"><see langword="true"/> when the user canceled the sign-in;
    ///     <see langword="false"/> when the server rejected the sign-in typed.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MainWindow_SignInNeededChipClicked_SignInNotGiven_KeepsTheChip(bool userCanceled) => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a window whose only card's background check found that its source needs a sign-in
        var feed = CreateSignInNeededFeed();
        var viewModel = CreateViewModelOver(feed, CreateTempDirectory());
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        try
        {
            await WaitUntilAsync(() => viewModel.PendingRefresh.IsCompleted);
            var card = Assert.Single(viewModel.RepoCards);
            Assert.True(card.SourceNeedsSignIn);
            Dispatcher.UIThread.RunJobs();
            var chip = Assert.Single(window.GetVisualDescendantsOf<Button>("SignInNeededChip"));
            var plainChip = Assert.Single(window.GetVisualDescendantsOf<Border>("SourceProblemChip"));
            var tooltipBefore = Assert.IsType<string>(ToolTip.GetTip(chip));
            var callsBefore = feed.ListVersionsCalls;

            // Act: the user clicks the chip and does not sign in
            feed.ListVersionsException = userCanceled
                ? new PackageSourceAccessException(
                    PackageSourceFailureKind.SignInCanceled,
                    "Sign-in to source 'Feed' was canceled.",
                    signInNeededMessage: "Source 'Feed' needs a sign-in.")
                : new PackageSourceAccessException(
                    PackageSourceFailureKind.AuthenticationFailed,
                    "Source 'Feed' rejected the credentials (the server answered 401).",
                    signInNeededMessage: "Source 'Feed' needs a sign-in.");
            ClickWithMouse(window, chip);
            await WaitUntilAsync(() => !card.IsBusy && !card.IsCheckingUpgrade);
            Dispatcher.UIThread.RunJobs();

            // Assert: the source was read again, and the same clickable chip and tooltip remain
            Assert.True(feed.ListVersionsCalls > callsBefore);
            Assert.True(card.SourceNeedsSignIn);
            Assert.True(chip.IsEffectivelyVisible);
            Assert.True(chip.IsEffectivelyEnabled);
            Assert.False(plainChip.IsVisible);
            Assert.Equal("Sign-in needed", AutomationProperties.GetName(chip));
            Assert.Equal(tooltipBefore, Assert.IsType<string>(ToolTip.GetTip(chip)));
            Assert.Equal("Needs attention", card.StatusText);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a package source problem other than a missing sign-in is shown as the plain chip, which
    ///     explains the problem in its tooltip, and not as the clickable sign-in chip.
    /// </summary>
    [Fact]
    public void MainWindow_SourceProblemOtherThanSignIn_ShowsTheTooltipChipNotTheButton() => HeadlessTestApp.Run(() =>
    {
        // Arrange: a pinned repo and no package source configured
        var repo = CreateTempDirectory();
        RepoPinStore.Save(repo, new RepoPin { PackageName = "contoso-agents", Version = "1.0.0" });
        var settings = new AppSettings { Sources = [], RecentRepos = [new RecentRepo { Path = repo }] };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());

        // Act
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Assert: the plain chip with its explanation, and no sign-in chip
        try
        {
            var chip = Assert.Single(window.GetVisualDescendantsOf<Button>("SignInNeededChip"));
            var plainChip = Assert.Single(window.GetVisualDescendantsOf<Border>("SourceProblemChip"));
            Assert.True(plainChip.IsEffectivelyVisible);
            Assert.False(chip.IsVisible);
            Assert.Contains("No configured source offers", Assert.IsType<string>(ToolTip.GetTip(plainChip)), StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that closing the Settings window without saving checks again the card that needs a sign-in,
    ///     so the chip goes away once the source answers (for example after a sign-in made by Test connection).
    /// </summary>
    [Fact]
    public void MainWindow_SettingsClosedWithoutSaving_RechecksTheCardThatNeedsSignIn() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a window whose only card needs a sign-in, and the Settings window open
        var feed = CreateSignInNeededFeed();
        var viewModel = CreateViewModelOver(feed, CreateTempDirectory());
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        try
        {
            await WaitUntilAsync(() => viewModel.PendingRefresh.IsCompleted);
            var card = Assert.Single(viewModel.RepoCards);
            Assert.True(card.SourceNeedsSignIn);
            DialogWindowTests.Click(window, "OpenSettingsButton");
            Dispatcher.UIThread.RunJobs();
            var settingsWindow = Assert.Single(window.OwnedWindows.OfType<SettingsWindow>());

            // Act: the source answers now, and Settings is closed without saving
            feed.ListVersionsException = null;
            feed.Publish("contoso-agents", "1.1.0");
            settingsWindow.Close();
            await WaitUntilAsync(() => !card.SourceNeedsSignIn && viewModel.PendingRefresh.IsCompleted);

            // Assert: the card was checked again and shows the upgrade instead of the sign-in chip
            Assert.Empty(window.OwnedWindows.OfType<SettingsWindow>());
            Assert.False(card.HasSourceProblem);
            Assert.True(card.IsUpgradeAvailable);
            Assert.False(Assert.Single(window.GetVisualDescendantsOf<Button>("SignInNeededChip")).IsVisible);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that saving the settings checks each card once: closing the Settings window after the save
    ///     (which already checks every card) does not check the card again.
    /// </summary>
    [Fact]
    public void MainWindow_SettingsSaved_ChecksEachCardOnce() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a card that needs a sign-in (a failure is never cached, so every check reads the source)
        var feed = CreateSignInNeededFeed();
        var viewModel = CreateViewModelOver(feed, CreateTempDirectory());
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        try
        {
            await WaitUntilAsync(() => viewModel.PendingRefresh.IsCompleted);
            var card = Assert.Single(viewModel.RepoCards);
            Assert.True(card.SourceNeedsSignIn);
            DialogWindowTests.Click(window, "OpenSettingsButton");
            Dispatcher.UIThread.RunJobs();
            var settingsWindow = Assert.Single(window.OwnedWindows.OfType<SettingsWindow>());
            var settingsViewModel = Assert.IsType<SettingsWindowViewModel>(settingsWindow.DataContext);
            var callsBefore = feed.ListVersionsCalls;

            // Act: save (which closes the window)
            settingsViewModel.SaveCommand.Execute(null);
            await WaitUntilAsync(() => window.OwnedWindows.Count == 0 && viewModel.PendingRefresh.IsCompleted);
            Dispatcher.UIThread.RunJobs();

            // Assert: the source was read once for the card - by the save's check - and not again on closing
            Assert.Equal(callsBefore + 1, feed.ListVersionsCalls);
            Assert.True(card.SourceNeedsSignIn);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that while dropped folders are being checked (held at a gate) the window stays
    ///     responsive - the UI thread runs other work - and the progress strip under the app bar shows
    ///     the count and a Cancel button; once the checks run the folders are added and the strip goes.
    /// </summary>
    [Fact]
    public void MainWindow_DropFolders_WhileChecked_WindowStaysResponsiveAndShowsProgress() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: an empty list whose adds are held at a gate
        var gate = new GatedBackgroundWorkRunner();
        var viewModel = new MainWindowViewModel(new AppSettings(), configDirectory: CreateTempDirectory(), backgroundWork: gate);
        var window = new MainWindow { DataContext = viewModel, Width = 900, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var strip = window.FindControl<Border>("GlobalProgressStrip")!;
        Assert.False(strip.IsVisible);

        try
        {
            // Act: drop two folders on the list area; the add starts and its checks are held
            RepoReorderWindowTests.DropExternal(window, EmptyStateCenter(window), CreateRepoDirectory(), CreateRepoDirectory());
            Dispatcher.UIThread.RunJobs();
            await gate.FirstCall;
            var ran = false;
            await Dispatcher.UIThread.InvokeAsync(() => ran = true);

            // Assert: the UI thread is free, nothing is added yet, and the strip shows the add
            Assert.True(ran);
            Assert.Empty(viewModel.RepoCards);
            Assert.True(strip.IsVisible);
            Assert.Equal("Adding 0 of 2 repositories...", window.FindControl<TextBlock>("GlobalProgressText")!.Text);
            Assert.True(window.FindControl<Button>("GlobalProgressCancelButton")!.IsVisible);
            var bar = window.FindControl<ProgressBar>("GlobalProgressBar")!;
            Assert.Equal("Adding 0 of 2 repositories...", AutomationProperties.GetName(bar));

            // Act: the checks run
            gate.Release();
            await RepoReorderWindowTests.SettleAddsAsync(viewModel);

            // Assert: both added, silently, and the strip is gone
            Assert.Equal(2, viewModel.RepoCards.Count);
            Assert.False(strip.IsVisible);
            Assert.Empty(window.OwnedWindows);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the progress strip's Cancel stops an add before anything is added, without a
    ///     message afterwards.
    /// </summary>
    [Fact]
    public void MainWindow_GlobalProgressCancel_WhileAddingFolders_AddsNothing() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: an add held while its folders are checked
        var gate = new GatedBackgroundWorkRunner();
        var viewModel = new MainWindowViewModel(new AppSettings(), configDirectory: CreateTempDirectory(), backgroundWork: gate);
        var window = new MainWindow { DataContext = viewModel, Width = 900, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            RepoReorderWindowTests.DropExternal(window, EmptyStateCenter(window), CreateRepoDirectory(), CreateRepoDirectory());
            Dispatcher.UIThread.RunJobs();
            await gate.FirstCall;

            // Act: Cancel on the strip, then the held checks finish
            DialogWindowTests.Click(window, "GlobalProgressCancelButton");
            gate.Release();
            await RepoReorderWindowTests.SettleAddsAsync(viewModel);

            // Assert: nothing added, nothing said, the strip is gone
            Assert.Empty(viewModel.RepoCards);
            Assert.Empty(viewModel.Settings.RecentRepos);
            Assert.Empty(window.OwnedWindows);
            Assert.False(window.FindControl<Border>("GlobalProgressStrip")!.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that folders dropped while an add is still running wait for it (their folders are not
    ///     checked meanwhile) and are added after it.
    /// </summary>
    [Fact]
    public void MainWindow_DropFolders_WhileAnAddRuns_AddsThemAfterwards() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: the first drop's checks are held
        var gate = new GatedBackgroundWorkRunner();
        var viewModel = new MainWindowViewModel(new AppSettings(), configDirectory: CreateTempDirectory(), backgroundWork: gate);
        var window = new MainWindow { DataContext = viewModel, Width = 900, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var first = CreateRepoDirectory();
        var second = CreateRepoDirectory();

        try
        {
            RepoReorderWindowTests.DropExternal(window, EmptyStateCenter(window), first);
            Dispatcher.UIThread.RunJobs();
            await gate.FirstCall;

            // Act: a second drop while the first add runs
            RepoReorderWindowTests.DropExternal(window, EmptyStateCenter(window), second);
            Dispatcher.UIThread.RunJobs();

            // Assert: the second add waits - only the first folder is being checked
            Assert.Equal(1, gate.Calls);

            // Act
            gate.Release();
            await RepoReorderWindowTests.SettleAddsAsync(viewModel);

            // Assert: both added, in drop order
            Assert.Equal([first, second], viewModel.RepoCards.Select(card => card.RepoPath));
            Assert.False(window.FindControl<Border>("GlobalProgressStrip")!.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that while dropped folders are being checked (held at a gate) collapsing a section
    ///     and typing a filter take effect at once, and that the new cards then follow the filter in
    ///     force and the collapse made meanwhile is kept.
    /// </summary>
    [Fact]
    public void MainWindow_DropFolders_FilterAndCollapseWhileChecked_TakeEffectAtOnceAndApplyToTheNewCards() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: kiwi and pear in Work, plum in Home, every background read held at a gate
        var gate = new GatedBackgroundWorkRunner();
        var kiwi = CreateNamedRepoDirectory("kiwi");
        var pear = CreateNamedRepoDirectory("pear");
        var plum = CreateNamedRepoDirectory("plum");
        var fig = CreateNamedRepoDirectory("fig");
        var kiwiNew = CreateNamedRepoDirectory("kiwi-new");
        var viewModel = new MainWindowViewModel(
            new AppSettings
            {
                RecentRepos =
                [
                    new RecentRepo { Path = kiwi, GroupName = "Work" },
                    new RecentRepo { Path = pear, GroupName = "Work" },
                    new RecentRepo { Path = plum, GroupName = "Home" }
                ]
            },
            configDirectory: CreateTempDirectory(),
            backgroundWork: gate);
        var window = new MainWindow { DataContext = viewModel, Width = 900, Height = 1000 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var strip = window.FindControl<Border>("GlobalProgressStrip")!;
        var filter = window.GetVisualDescendantsOf<TextBox>("FilterTextBox").Single();

        try
        {
            // Act: drop two folders on the Work header; the add starts and its checks are held
            RepoReorderWindowTests.DropExternal(window, RepoReorderWindowTests.OnHeader(window, "Work"), fig, kiwiNew);
            Dispatcher.UIThread.RunJobs();

            // Assert: the add is held (the cards' own checks share the gate, so its first call is
            // not necessarily the add's)
            Assert.False(viewModel.PendingAddRepos.IsCompleted);
            Assert.True(strip.IsVisible);

            // Act: collapse Home with the pointer
            ClickWithMouse(window, HeaderToggle(window, "Home"));

            // Assert: collapsed at once, while the add is still held
            Assert.True(viewModel.IsGroupCollapsed("Home"));
            Assert.Equal(["kiwi", "pear"], RepoReorderWindowTests.RowNames(window));
            Assert.False(viewModel.PendingAddRepos.IsCompleted);

            // Act: type a filter
            filter.Text = "kiwi";
            Dispatcher.UIThread.RunJobs();

            // Assert: filtered at once, while the add is still held
            Assert.Equal(["kiwi"], RepoReorderWindowTests.RowNames(window));
            Assert.False(viewModel.PendingAddRepos.IsCompleted);

            // Act: the checks run
            gate.Release();
            await RepoReorderWindowTests.SettleAddsAsync(viewModel);

            // Assert: both added into Work, silently; the shown cards follow the filter in force
            Assert.Equal(["kiwi", "kiwi-new"], RepoReorderWindowTests.RowNames(window));
            Assert.Equal("Work", viewModel.RepoCards.Single(card => card.RepoPath == fig).GroupName);
            Assert.Equal("Work", viewModel.RepoCards.Single(card => card.RepoPath == kiwiNew).GroupName);
            Assert.Empty(window.OwnedWindows);
            Assert.False(strip.IsVisible);

            // Act: clear the filter
            filter.Text = string.Empty;
            Dispatcher.UIThread.RunJobs();

            // Assert: the collapse made during the add is kept, and the new repos follow Work's
            // stored repos in drop order
            Assert.True(viewModel.IsGroupCollapsed("Home"));
            Assert.Equal(["kiwi", "pear", "fig", "kiwi-new"], RepoReorderWindowTests.RowNames(window));
        }
        finally
        {
            // Release the gate whatever happened, and let the cards' checks end before the folders go
            gate.Release();
            await Task.WhenAll(viewModel.RepoCards.Select(card => card.GitStatusRefreshTask));
            window.Close();
        }
    });

    /// <summary>
    ///     Test that when an add cannot be saved the window asks Retry or Discard, and Retry (once saving
    ///     works) adds the folders.
    /// </summary>
    [Fact]
    public void MainWindow_AddSaveFails_RetryAddsThem() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: saving fails until the user retries
        var failSaving = true;
        var configDir = CreateTempDirectory();
        var viewModel = new MainWindowViewModel(
            new AppSettings(),
            configDirectory: configDir,
            saveSettings: settings =>
            {
                if (failSaving)
                {
                    throw new InvalidOperationException("The disk is full.");
                }

                SettingsStore.Save(settings, configDir);
            });
        var window = new MainWindow { DataContext = viewModel, Width = 900, Height = 700 };
        window.Show();
        var repo = CreateRepoDirectory();
        window.RepoFolderPicker = () => Task.FromResult<IReadOnlyList<string>>([repo]);

        try
        {
            // Act: add a folder; the save fails and the question appears
            DialogWindowTests.Click(window, "AddRepoButton");
            await WaitUntilAsync(() => window.OwnedWindows.OfType<ConfirmationWindow>().Any());
            var question = window.OwnedWindows.OfType<ConfirmationWindow>().Single();
            Assert.Contains("The disk is full.", question.FindControl<TextBlock>("ConfirmationMessageText")!.Text, StringComparison.Ordinal);
            Assert.Equal("Retry", question.FindControl<Button>("ConfirmationYesButton")!.Content);
            Assert.Equal("Discard", question.FindControl<Button>("ConfirmationNoButton")!.Content);
            Assert.Empty(viewModel.RepoCards);

            // Act: saving works again; Retry
            failSaving = false;
            DialogWindowTests.Click(question, "ConfirmationYesButton");
            await RepoReorderWindowTests.SettleAddsAsync(viewModel);

            // Assert: added and saved, nothing more to say
            Assert.Equal(repo, Assert.Single(viewModel.RepoCards).RepoPath);
            Assert.Single(SettingsStore.Load(configDir).RecentRepos);
            Assert.Empty(window.OwnedWindows);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that Discard after a failed save leaves the list as saved (nothing added) and tells the
    ///     user the folder was not added and why.
    /// </summary>
    [Fact]
    public void MainWindow_AddSaveFails_DiscardLeavesListAsSaved() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: every save fails
        var viewModel = new MainWindowViewModel(
            new AppSettings(),
            configDirectory: CreateTempDirectory(),
            saveSettings: _ => throw new InvalidOperationException("The disk is full."));
        var window = new MainWindow { DataContext = viewModel, Width = 900, Height = 700 };
        window.Show();
        var repo = CreateRepoDirectory();
        window.RepoFolderPicker = () => Task.FromResult<IReadOnlyList<string>>([repo]);

        try
        {
            // Act: add a folder, then Discard
            DialogWindowTests.Click(window, "AddRepoButton");
            await WaitUntilAsync(() => window.OwnedWindows.OfType<ConfirmationWindow>().Any());
            DialogWindowTests.Click(window.OwnedWindows.OfType<ConfirmationWindow>().Single(), "ConfirmationNoButton");
            await RepoReorderWindowTests.SettleAddsAsync(viewModel);
            await WaitUntilAsync(() => window.OwnedWindows.OfType<MessageBoxWindow>().Any());

            // Assert: nothing added, and the user is told why
            Assert.Empty(viewModel.RepoCards);
            Assert.Empty(viewModel.Settings.RecentRepos);
            var message = window.OwnedWindows.OfType<MessageBoxWindow>().Single();
            Assert.Contains("The disk is full.", message.FindControl<TextBlock>("MessageText")!.Text, StringComparison.Ordinal);
            message.Close();
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that saving the settings refreshes the cards in the background: while their reads are
    ///     held the UI thread stays free and the strip shows "Refreshing repositories..." without Cancel;
    ///     once they run the strip goes.
    /// </summary>
    [Fact]
    public void MainWindow_SettingsApplied_RefreshesCardsInBackgroundShowingProgress() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: one repo, every background read held
        var gate = new GatedBackgroundWorkRunner();
        var viewModel = new MainWindowViewModel(
            new AppSettings { RecentRepos = [new RecentRepo { Path = CreateTempDirectory() }] },
            configDirectory: CreateTempDirectory(),
            backgroundWork: gate);
        var window = new MainWindow { DataContext = viewModel, Width = 900, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var strip = window.FindControl<Border>("GlobalProgressStrip")!;

        try
        {
            // Act: save the settings
            DialogWindowTests.Click(window, "OpenSettingsButton");
            Dispatcher.UIThread.RunJobs();
            var settingsWindow = Assert.Single(window.OwnedWindows.OfType<SettingsWindow>());
            Assert.IsType<SettingsWindowViewModel>(settingsWindow.DataContext).SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var ran = false;
            await Dispatcher.UIThread.InvokeAsync(() => ran = true);

            // Assert: the window is free and the strip shows the refresh, without Cancel
            Assert.True(ran);
            Assert.True(strip.IsVisible);
            Assert.Equal("Refreshing repositories...", window.FindControl<TextBlock>("GlobalProgressText")!.Text);
            Assert.False(window.FindControl<Button>("GlobalProgressCancelButton")!.IsVisible);

            // Act: the reads run
            gate.Release();
            await WaitUntilAsync(() => viewModel.PendingRefresh.IsCompleted);

            // Assert
            Assert.False(strip.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a card shown in the window gets its working-tree check in the background: "not
    ///     checked yet" while git is held, git's answer once it runs.
    /// </summary>
    [Fact]
    public void MainWindow_CardShown_ChecksGitStatusInBackground() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a repo with a clean working tree (stub git), every background read held
        var stub = Tests.GitIntegration.GitStub.Create(statusOutput: "", statusExitCode: 0);
        _tempPaths.Add(stub.Path);
        var gate = new GatedBackgroundWorkRunner();
        var viewModel = new MainWindowViewModel(
            new AppSettings { GitExecutablePath = stub.Path, RecentRepos = [new RecentRepo { Path = CreateTempDirectory() }] },
            configDirectory: CreateTempDirectory(),
            backgroundWork: gate);
        var card = Assert.Single(viewModel.RepoCards);

        // Act: show the window; the card's check starts and is held
        var window = new MainWindow { DataContext = viewModel, Width = 900, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            await gate.FirstCall;

            // Assert: not checked yet
            Assert.False(card.GitStatusChecked);
            Assert.False(card.IsWorkingTreeDirty);

            // Act
            gate.Release();
            await card.GitStatusRefreshTask;

            // Assert: git's answer is shown
            Assert.True(card.GitStatusChecked);
            Assert.True(card.CanPull);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Gets the center of the empty-state panel (shown while no repo is listed), in window coordinates.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <returns>The point.</returns>
    private static Point EmptyStateCenter(MainWindow window)
    {
        var panel = window.FindControl<Border>("EmptyStatePanel")!;
        return panel.TranslatePoint(new Point(panel.Bounds.Width / 2, panel.Bounds.Height / 2), window)
               ?? throw new InvalidOperationException("The empty-state panel is not in the window.");
    }

    /// <summary>
    ///     Creates an in-memory source serving <c>contoso-agents 1.0.0</c> that answers every version listing
    ///     with "needs a sign-in" until the test clears <see cref="Tests.AgentPackageManagement.FakePackageSourceProvider.ListVersionsException"/>.
    /// </summary>
    /// <returns>The source.</returns>
    private static Tests.AgentPackageManagement.FakePackageSourceProvider CreateSignInNeededFeed() =>
        new("Feed", ("contoso-agents", "1.0.0"))
        {
            ListVersionsException = new PackageSourceAccessException(
                PackageSourceFailureKind.AuthenticationFailed, "Source 'Feed' needs a sign-in.")
        };

    /// <summary>
    ///     Creates a main-window view model over one repo pinned to <c>contoso-agents 1.0.0</c> whose only
    ///     source (<c>Feed</c>) is served by the given in-memory source, whatever the settings are changed to.
    /// </summary>
    /// <param name="feed">The in-memory source.</param>
    /// <param name="sourceFolder">An existing folder used as the zip source's location.</param>
    /// <returns>The view model.</returns>
    private MainWindowViewModel CreateViewModelOver(Tests.AgentPackageManagement.FakePackageSourceProvider feed, string sourceFolder)
    {
        var repo = CreateTempDirectory();
        RepoPinStore.Save(repo, new RepoPin { PackageName = "contoso-agents", Version = "1.0.0" });
        var settings = new AppSettings
        {
            Sources = [new PackageSourceDefinition { Alias = "Feed", Type = PackageSourceType.Zip, Location = sourceFolder }],
            RecentRepos = [new RecentRepo { Path = repo }]
        };
        return new MainWindowViewModel(
            settings,
            configDirectory: CreateTempDirectory(),
            registryFactory: current => new PackageSourceRegistry(current.Sources, _ => feed));
    }

    /// <summary>
    ///     Runs the UI thread's jobs until the condition holds (or about thirty seconds passed), then asserts it.
    /// </summary>
    /// <param name="condition">The condition to wait for.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 1200 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    /// <summary>
    ///     Clicks a control with the real pointer: a mouse button press and release at its center, as a
    ///     user's click arrives (so the button's own command logic runs).
    /// </summary>
    /// <param name="window">The window containing the control.</param>
    /// <param name="control">The control to click.</param>
    private static void ClickWithMouse(Window window, Control control)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
                     ?? throw new InvalidOperationException("The control is not in the window.");
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
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

    /// <summary>
    ///     Creates a unique temporary folder that is the top folder of a Git repository as far as the
    ///     add is concerned (a <c>.git</c> folder whose <c>HEAD</c> names a branch), removed on disposal.
    /// </summary>
    /// <returns>The folder's path.</returns>
    private string CreateRepoDirectory()
    {
        var path = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        File.WriteAllText(Path.Combine(path, ".git", "HEAD"), "ref: refs/heads/main\n");
        return path;
    }

    /// <summary>
    ///     Creates a repo folder with the given name (the card's name) under a unique temporary parent,
    ///     the top folder of a Git repository as far as the add is concerned, removed on disposal.
    /// </summary>
    /// <param name="name">The folder name.</param>
    /// <returns>The folder's path.</returns>
    private string CreateNamedRepoDirectory(string name)
    {
        var path = Path.Combine(CreateTempDirectory(), name);
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        File.WriteAllText(Path.Combine(path, ".git", "HEAD"), "ref: refs/heads/main\n");
        return path;
    }

    /// <summary>
    ///     Finds the collapse/expand toggle of a section's header.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="sectionName">The section's name.</param>
    /// <returns>The header's toggle button.</returns>
    private static Button HeaderToggle(MainWindow window, string sectionName) =>
        window.GetVisualDescendantsOf<Button>("GroupHeaderToggleButton")
            .Single(button => button.DataContext is RepoGroupHeaderViewModel header && header.Name == sectionName);

    /// <summary>
    ///     Clicks an item of the only card's "..." menu the way a user does: opens the menu from its
    ///     button, raises <see cref="MenuItem.ClickEvent"/> on the named item and closes the menu.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="name">The menu item's XAML name.</param>
    private static void ClickCardMenuItem(MainWindow window, string name)
    {
        var button = window.GetVisualDescendants().OfType<Button>().Single(candidate => candidate.Name == "MoreActionsButton");
        var menu = Assert.IsType<MenuFlyout>(button.Flyout);
        menu.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        try
        {
            menu.Items.OfType<MenuItem>().Single(item => item.Name == name)
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        finally
        {
            menu.Hide();
        }

        Dispatcher.UIThread.RunJobs();
    }
}
