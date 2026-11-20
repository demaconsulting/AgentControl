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
        viewModel.AddRepo(CreateTempDirectory());
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
            Directory.Delete(path, recursive: true);
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
