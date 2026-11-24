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
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Headless tests of selecting several repo cards in the main window as the user does: Ctrl+click,
///     Shift+click and Ctrl+A, dragging the selection to another group, removing it with the Delete key or
///     the card's menu, and how the selection is shown and exposed to assistive technology.
/// </summary>
/// <remarks>
///     The window shows the group Work with <c>w1</c> and <c>w2</c> and, under Ungrouped, <c>u1</c>, <c>u2</c>
///     and <c>u3</c>. Presses, moves and keys are real input events on the headless window; the headless
///     platform has no drag source, so a drag is started with a real press and move (which calls the window's
///     <c>DragStarter</c> seam, here a fake that records the dragged data) and finished with the platform's
///     drag events raised on the window at the pointer position. Runs in the <c>RealProcess</c> collection
///     because showing cards spawns real git subprocesses as part of each card's background repo-state read.
/// </remarks>
[Collection("RealProcess")]
public sealed class RepoMultiSelectWindowTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that Ctrl+click adds a card to the selection and removes it again, and that Shift+click
    ///     selects every card from the last clicked one to the one clicked, across the boundary of a group,
    ///     never selecting a section header.
    /// </summary>
    [Fact]
    public void MainWindow_RepoList_CtrlAndShiftClick_SelectCardsOnlyAcrossGroups() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, viewModel) = CreateWindow();

        try
        {
            // Act: click u1, then Ctrl+click w1 and u3
            Click(window, "u1");
            Click(window, "w1", RawInputModifiers.Control);
            Click(window, "u3", RawInputModifiers.Control);

            // Assert: three cards, in the order the list shows them
            Assert.Equal(["w1", "u1", "u3"], Names(viewModel.SelectedCards));

            // Act: Ctrl+click u1 again
            Click(window, "u1", RawInputModifiers.Control);

            // Assert
            Assert.Equal(["w1", "u3"], Names(viewModel.SelectedCards));

            // Act: click w1, then Shift+click u2: a range across the Ungrouped header
            Click(window, "w1");
            Click(window, "u2", RawInputModifiers.Shift);

            // Assert: the cards of the range only; no header is selected in the list
            Assert.Equal(["w1", "w2", "u1", "u2"], Names(viewModel.SelectedCards));
            Assert.Empty(List(window).SelectedItems!.OfType<RepoGroupHeaderViewModel>());
            Assert.Equal(["w1", "w2", "u1", "u2"], SelectedRows(window));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that Ctrl+A selects every card that is shown and no section header, and that the cards of a
    ///     collapsed group, which are not shown, are not selected.
    /// </summary>
    [Fact]
    public void MainWindow_RepoList_CtrlA_SelectsShownCardsOnly() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, viewModel) = CreateWindow();

        try
        {
            Click(window, "u1");

            // Act
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

            // Assert
            Assert.Equal(["w1", "w2", "u1", "u2", "u3"], Names(viewModel.SelectedCards));
            Assert.Empty(List(window).SelectedItems!.OfType<RepoGroupHeaderViewModel>());

            // Act: collapse Work (which drops w1 and w2 from the selection), then select all again
            viewModel.ToggleGroupCollapsed("Work");
            Dispatcher.UIThread.RunJobs();

            // Assert: the selected rows follow the view model
            Assert.Equal(["u1", "u2", "u3"], Names(viewModel.SelectedCards));
            Assert.Equal(["u1", "u2", "u3"], SelectedRows(window));

            // Act
            Click(window, "u2");
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

            // Assert
            Assert.Equal(["u1", "u2", "u3"], Names(viewModel.SelectedCards));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a plain press on a card that is one of several selected cards keeps the selection (so
    ///     the cards can be dragged together) and that releasing the button without a drag reduces the
    ///     selection to that card, as a file manager does; and that a plain click on an unselected card
    ///     selects that card alone at once.
    /// </summary>
    [Fact]
    public void MainWindow_RepoList_PressOnSelectedCard_KeepsSelectionUntilReleaseWithoutDrag() => HeadlessTestApp.Run(() =>
    {
        // Arrange: u1, u2 and u3 selected
        var (window, viewModel) = CreateWindow();

        try
        {
            Click(window, "u1");
            Click(window, "u3", RawInputModifiers.Shift);
            Assert.Equal(["u1", "u2", "u3"], Names(viewModel.SelectedCards));

            // Act: press (and hold) on u2
            var point = CardPoint(window, "u2");
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();

            // Assert: the selection is still the three cards
            Assert.Equal(["u1", "u2", "u3"], Names(viewModel.SelectedCards));

            // Act: release without moving
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            // Assert: now only u2
            Assert.Equal(["u2"], Names(viewModel.SelectedCards));

            // Act: a plain click on an unselected card
            Click(window, "w1");

            // Assert
            Assert.Equal(["w1"], Names(viewModel.SelectedCards));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that dragging one of several selected cards starts a drag that carries every selected card
    ///     (the grabbed one first), that the selection is not reduced when the button is released, and that
    ///     dropping on a group's header moves all of them into the group, in list order, leaving them selected
    ///     and showing them selected.
    /// </summary>
    [Fact]
    public void MainWindow_RepoList_DragSelectedCard_CarriesAllSelectedAndDropsThemInTheGroup() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: u1 and u3 selected
        var (window, viewModel) = CreateWindow();

        try
        {
            Click(window, "u1");
            Click(window, "u3", RawInputModifiers.Control);

            // Act: grab u3 and move far enough to start a drag
            var drag = BeginDrag(window, "u3");

            // Assert: both cards are carried, the grabbed one first; the selection is intact
            Assert.NotNull(drag.Transfer);
            var carried = drag.Transfer.TryGetValues(MainWindow.RepoCardDataFormat)!
                .Select(path => Path.GetFileName(path)).ToList();
            Assert.Equal(["u3", "u1"], carried);
            Assert.Equal(["u1", "u3"], Names(viewModel.SelectedCards));

            // Act: drop on the Work header
            var work = FindHeaderRow(window, "Work");
            Drop(window, drag, PointIn(window, work, 0.5));
            await drag.CompleteAsync();

            // Assert: in Work after w2, in list order; still selected, and the rows show it
            Assert.Equal(["w1", "w2", "u1", "u3"], Names(viewModel.DisplayedRepoCards.Where(card => card.GroupName == "Work")));
            Assert.Equal("Work", viewModel.RepoCards.Single(card => card.RepoName == "u3").GroupName);
            Assert.Equal(["u1", "u3"], Names(viewModel.SelectedCards));
            Assert.Equal(["u1", "u3"], SelectedRows(window));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that dragging a card that is not selected moves that card alone and selects it alone, and that
    ///     a drop next to one of the dragged cards is refused with nothing shown or changed.
    /// </summary>
    [Fact]
    public void MainWindow_RepoList_DragUnselectedCard_MovesOnlyThatCard() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: u1 and u2 selected, u3 is not
        var (window, viewModel) = CreateWindow();

        try
        {
            Click(window, "u1");
            Click(window, "u2", RawInputModifiers.Control);

            // Act: grab u3
            var drag = BeginDrag(window, "u3");

            // Assert: only u3 is carried and selected
            Assert.NotNull(drag.Transfer);
            var carried = drag.Transfer.TryGetValues(MainWindow.RepoCardDataFormat)!.Select(path => Path.GetFileName(path)).ToList();
            Assert.Equal(["u3"], carried);
            Assert.Equal(["u3"], Names(viewModel.SelectedCards));

            // Act: drop it on Work
            Drop(window, drag, PointIn(window, FindHeaderRow(window, "Work"), 0.5));
            await drag.CompleteAsync();

            // Assert
            Assert.Equal("Work", viewModel.RepoCards.Single(card => card.RepoName == "u3").GroupName);
            Assert.Null(viewModel.RepoCards.Single(card => card.RepoName == "u1").GroupName);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a dragged selection dropped next to one of its own cards is refused: no drop feedback is
    ///     shown, the drop changes nothing and the selection stays.
    /// </summary>
    [Fact]
    public void MainWindow_RepoList_DragSelection_DropNextToOwnCard_IsRefused() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var (window, viewModel) = CreateWindow();

        try
        {
            Click(window, "u1");
            Click(window, "u3", RawInputModifiers.Control);
            var before = Names(viewModel.RepoCards);
            var drag = BeginDrag(window, "u1");

            // Act: over the lower half of u3, one of the dragged cards
            var u3Row = FindRow(window, "u3");
            Raise(window, drag, RawDragEventType.DragOver, PointIn(window, u3Row, 0.8));

            // Assert: no insertion line anywhere
            Assert.All(
                List(window).GetVisualDescendants().OfType<ListBoxItem>(),
                row => Assert.False(row.Classes.Contains("dropAbove") || row.Classes.Contains("dropBelow")));

            // Act
            Raise(window, drag, RawDragEventType.Drop, PointIn(window, u3Row, 0.8));
            await drag.CompleteAsync();

            // Assert
            Assert.Equal(before, Names(viewModel.RepoCards));
            Assert.Equal(["u1", "u3"], Names(viewModel.SelectedCards));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the Delete key asks to remove the selected cards, saying how many, that answering No
    ///     changes nothing and that answering Yes removes exactly the selected cards from the list (nothing
    ///     else); and that with one card selected it asks the question for that card.
    /// </summary>
    [Fact]
    public void MainWindow_RepoList_DeleteKey_ConfirmsWithTheCountAndRemovesTheSelection() => HeadlessTestApp.Run(() =>
    {
        // Arrange: w2, u1 and u2 selected
        var (window, viewModel) = CreateWindow();

        try
        {
            Click(window, "w2");
            Click(window, "u1", RawInputModifiers.Control);
            Click(window, "u2", RawInputModifiers.Control);
            List(window).Focus();

            // Act: Delete, answered No
            window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            var confirmation = Assert.Single(window.OwnedWindows.OfType<ConfirmationWindow>());
            var message = confirmation.FindControl<TextBlock>("ConfirmationMessageText")!.Text!;
            DialogWindowTests.Click(confirmation, "ConfirmationNoButton");
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Contains("Remove 3 repos", message);
            Assert.Contains("anything on disk", message);
            Assert.Equal(5, viewModel.RepoCards.Count);
            Assert.Equal(["w2", "u1", "u2"], Names(viewModel.SelectedCards));

            // Act: Delete, answered Yes
            List(window).Focus();
            window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            confirmation = Assert.Single(window.OwnedWindows.OfType<ConfirmationWindow>());
            DialogWindowTests.Click(confirmation, "ConfirmationYesButton");
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Equal(["w1", "u3"], Names(viewModel.RepoCards));
            Assert.Empty(viewModel.SelectedCards);

            // Act: one card selected, Delete asks about that card
            Click(window, "u3");
            List(window).Focus();
            window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            confirmation = Assert.Single(window.OwnedWindows.OfType<ConfirmationWindow>());
            var single = confirmation.FindControl<TextBlock>("ConfirmationMessageText")!.Text!;
            DialogWindowTests.Click(confirmation, "ConfirmationNoButton");
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Contains("'u3'", single);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the "Remove from list" item of a card that is one of several selected cards says how many
    ///     it removes and removes them all after one confirmation, while the item of a card that is not selected
    ///     removes that card alone.
    /// </summary>
    [Fact]
    public void MainWindow_CardMenu_RemoveOnSelectedCard_RemovesTheWholeSelection() => HeadlessTestApp.Run(() =>
    {
        // Arrange: u1 and u2 selected
        var (window, viewModel) = CreateWindow();

        try
        {
            Click(window, "u1");
            Click(window, "u2", RawInputModifiers.Control);

            // Act: the menu of u2, one of the selected cards
            var menu = OpenCardMenu(window, "u2");
            var item = menu.Items.OfType<MenuItem>().Single(candidate => candidate.Name == "RemoveMenuItem");
            var text = item.Header?.ToString();
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();
            var confirmation = Assert.Single(window.OwnedWindows.OfType<ConfirmationWindow>());
            var message = confirmation.FindControl<TextBlock>("ConfirmationMessageText")!.Text!;
            DialogWindowTests.Click(confirmation, "ConfirmationYesButton");
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Equal("Remove 2 selected repos from list", text);
            Assert.Contains("Remove 2 repos", message);
            Assert.Equal(["w1", "w2", "u3"], Names(viewModel.RepoCards));

            // Act: the menu of u3, which is not selected (w1 is)
            Click(window, "w1");
            menu = OpenCardMenu(window, "u3");
            item = menu.Items.OfType<MenuItem>().Single(candidate => candidate.Name == "RemoveMenuItem");
            var plain = item.Header?.ToString();
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();
            confirmation = Assert.Single(window.OwnedWindows.OfType<ConfirmationWindow>());
            var single = confirmation.FindControl<TextBlock>("ConfirmationMessageText")!.Text!;
            DialogWindowTests.Click(confirmation, "ConfirmationYesButton");
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Equal("Remove from list", plain);
            Assert.Contains("'u3'", single);
            Assert.Equal(["w1", "w2"], Names(viewModel.RepoCards));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a selected card is exposed to assistive technology: its list row reports it is selected,
    ///     the automation peer of the row says so through the selection-item pattern, and the list's peer says
    ///     that several items can be selected.
    /// </summary>
    [Fact]
    public void MainWindow_RepoList_SelectedCards_AreExposedThroughTheRowAndTheAutomationPeers() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, _) = CreateWindow();

        try
        {
            Click(window, "u1");
            Click(window, "u3", RawInputModifiers.Control);

            // Act
            var selectedPeer = ControlAutomationPeer.CreatePeerForElement(FindRow(window, "u1"));
            var otherPeer = ControlAutomationPeer.CreatePeerForElement(FindRow(window, "u2"));
            var listPeer = ControlAutomationPeer.CreatePeerForElement(List(window));

            // Assert
            Assert.True(FindRow(window, "u3").IsSelected);
            Assert.False(FindRow(window, "u2").IsSelected);
            Assert.True(selectedPeer.GetProvider<ISelectionItemProvider>()!.IsSelected);
            Assert.False(otherPeer.GetProvider<ISelectionItemProvider>()!.IsSelected);
            Assert.True(listPeer.GetProvider<ISelectionProvider>()!.CanSelectMultiple);
            Assert.Equal("RepoListBox", Avalonia.Automation.AutomationProperties.GetAutomationId(List(window)));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a selected card is drawn on the selected-card surface and an unselected one on the normal
    ///     card surface, in the light and in the dark theme.
    /// </summary>
    /// <param name="variantName">The theme variant to check.</param>
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void MainWindow_RepoList_SelectedCard_UsesSelectedSurfaceInLightAndDark(string variantName) => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, _) = CreateWindow();
        var application = Application.Current!;
        var original = application.RequestedThemeVariant;
        application.RequestedThemeVariant = variantName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

        try
        {
            Dispatcher.UIThread.RunJobs();

            // Act: u1 selected, u2 not; let the card's short color transition finish
            Click(window, "u1");
            for (var tick = 0; tick < 30; tick++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                Dispatcher.UIThread.RunJobs();
            }

            // Assert
            var selectedBorder = CardBorder(window, "u1");
            var otherBorder = CardBorder(window, "u2");
            Assert.True(selectedBorder.TryFindResource("RepoCardBackgroundSelected", application.ActualThemeVariant, out var selectedBrush));
            Assert.True(otherBorder.TryFindResource("RepoCardBackground", application.ActualThemeVariant, out var cardBrush));
            Assert.Equal(((ISolidColorBrush)selectedBrush!).Color, ((ISolidColorBrush)selectedBorder.Background!).Color);
            Assert.Equal(((ISolidColorBrush)cardBrush!).Color, ((ISolidColorBrush)otherBorder.Background!).Color);
            Assert.NotEqual(((ISolidColorBrush)selectedBrush).Color, ((ISolidColorBrush)cardBrush).Color);
        }
        finally
        {
            application.RequestedThemeVariant = original;
            window.Close();
        }
    });

    /// <summary>
    ///     Deletes the temporary folders the tests created.
    /// </summary>
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
    ///     One drag in progress: the data the window handed to the drag starter and the completion that
    ///     ends the drag.
    /// </summary>
    private sealed class DragSession
    {
        /// <summary>Gets or sets the data being dragged, or <see langword="null"/> before a drag started.</summary>
        public IDataTransfer? Transfer { get; set; }

        /// <summary>Gets the completion that ends the drag, as the operating system does when the button is released.</summary>
        public TaskCompletionSource<DragDropEffects> Completion { get; } = new();

        /// <summary>
        ///     Ends the drag (if it has not ended) and lets the window finish its clean-up.
        /// </summary>
        /// <returns>A task that completes when the window has handled the end of the drag.</returns>
        public async Task CompleteAsync()
        {
            Completion.TrySetResult(DragDropEffects.Move);
            await Completion.Task;
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    ///     Creates a shown main window: the group Work with w1 and w2, and the repos u1, u2 and u3 in no group.
    /// </summary>
    /// <returns>The window and its view model.</returns>
    private (MainWindow Window, MainWindowViewModel ViewModel) CreateWindow()
    {
        var settings = new AppSettings
        {
            HasUserRepoOrder = true,
            RecentRepos =
            [
                new RecentRepo { Path = CreateRepoDirectory("w1"), GroupName = "Work" },
                new RecentRepo { Path = CreateRepoDirectory("w2"), GroupName = "Work" },
                new RecentRepo { Path = CreateRepoDirectory("u1") },
                new RecentRepo { Path = CreateRepoDirectory("u2") },
                new RecentRepo { Path = CreateRepoDirectory("u3") }
            ]
        };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());
        var window = new MainWindow { DataContext = viewModel, Height = 1600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The cards' repo state is read in the background; the tests act on settled cards
        HeadlessTestApp.RunJobsUntilRepoStateSettled(viewModel);
        return (window, viewModel);
    }

    /// <summary>
    ///     Gets the repo list.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <returns>The list.</returns>
    private static ListBox List(MainWindow window) => window.FindControl<ListBox>("RepoListBox")!;

    /// <summary>
    ///     Lists the names of cards.
    /// </summary>
    /// <param name="cards">The cards.</param>
    /// <returns>The repos' folder names, in order.</returns>
    private static List<string> Names(IEnumerable<RepoCardViewModel> cards) => [.. cards.Select(card => card.RepoName)];

    /// <summary>
    ///     Lists the names of the repos whose rows are selected, top to bottom as the user sees them.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <returns>The repo names.</returns>
    private static List<string> SelectedRows(MainWindow window) =>
        [.. List(window).GetVisualDescendants().OfType<ListBoxItem>()
            .Where(row => row.IsSelected && row.DataContext is RepoCardViewModel)
            .OrderBy(row => row.TranslatePoint(new Point(0, 0), window)!.Value.Y)
            .Select(row => ((RepoCardViewModel)row.DataContext!).RepoName)];

    /// <summary>
    ///     Finds the list row of a repo.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="repoName">The repo's folder name.</param>
    /// <returns>The row.</returns>
    private static ListBoxItem FindRow(MainWindow window, string repoName) =>
        List(window).GetVisualDescendants().OfType<ListBoxItem>()
            .Single(row => row.DataContext is RepoCardViewModel card && card.RepoName == repoName);

    /// <summary>
    ///     Finds the border that draws a repo's card.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="repoName">The repo's folder name.</param>
    /// <returns>The border.</returns>
    private static Border CardBorder(MainWindow window, string repoName) =>
        FindRow(window, repoName).GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("RepoCard"));

    /// <summary>
    ///     Finds the list row of a section's header.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="sectionName">The section's name.</param>
    /// <returns>The row.</returns>
    private static ListBoxItem FindHeaderRow(MainWindow window, string sectionName) =>
        List(window).GetVisualDescendants().OfType<ListBoxItem>()
            .Single(row => row.DataContext is RepoGroupHeaderViewModel header && header.Name == sectionName);

    /// <summary>
    ///     Gets a point on a card that is none of its controls (its status bar), in window coordinates.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="repoName">The repo's folder name.</param>
    /// <returns>The point.</returns>
    private static Point CardPoint(MainWindow window, string repoName)
    {
        var row = FindRow(window, repoName);
        return row.TranslatePoint(new Point(3, row.Bounds.Height / 2), window)
               ?? throw new InvalidOperationException("The row is not in the window.");
    }

    /// <summary>
    ///     Gets a point at a fraction of a row's height, in window coordinates.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="row">The row.</param>
    /// <param name="fraction">The fraction of the row's height, from its top.</param>
    /// <returns>The point.</returns>
    private static Point PointIn(MainWindow window, ListBoxItem row, double fraction) =>
        row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height * fraction), window)
        ?? throw new InvalidOperationException("The row is not in the window.");

    /// <summary>
    ///     Clicks a card with the left button, holding the given keys.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="repoName">The repo's folder name.</param>
    /// <param name="keys">The modifier keys held, if any.</param>
    private static void Click(MainWindow window, string repoName, RawInputModifiers keys = RawInputModifiers.None)
    {
        var point = CardPoint(window, repoName);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton | keys);
        window.MouseUp(point, MouseButton.Left, keys);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    ///     Presses the left button on a card and moves the pointer far enough to start a drag, with the
    ///     window's drag starter replaced by one that records the dragged data and leaves the drag open; then
    ///     releases the button, as the operating system's drag loop would not report it.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="repoName">The repo whose card is grabbed.</param>
    /// <returns>The drag; its <see cref="DragSession.Transfer"/> is <see langword="null"/> when no drag
    ///     started.</returns>
    private static DragSession BeginDrag(MainWindow window, string repoName)
    {
        var session = new DragSession();
        window.DragStarter = (_, transfer, _) =>
        {
            session.Transfer = transfer;
            return session.Completion.Task;
        };

        var start = CardPoint(window, repoName);
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseMove(new Point(start.X + 20, start.Y + 2), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(start.X + 20, start.Y + 2), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        return session;
    }

    /// <summary>
    ///     Raises one platform drag event on the window at a point, carrying the dragged data.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="drag">The drag.</param>
    /// <param name="type">The drag event.</param>
    /// <param name="point">The pointer position, in window coordinates.</param>
    private static void Raise(MainWindow window, DragSession drag, RawDragEventType type, Point point)
    {
        window.DragDrop(point, type, drag.Transfer!, DragDropEffects.Move);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    ///     Raises the events of a drop at a point: the pointer enters, moves over the list, and releases.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="drag">The drag.</param>
    /// <param name="point">The pointer position, in window coordinates.</param>
    private static void Drop(MainWindow window, DragSession drag, Point point)
    {
        Raise(window, drag, RawDragEventType.DragEnter, point);
        Raise(window, drag, RawDragEventType.DragOver, point);
        Raise(window, drag, RawDragEventType.Drop, point);
    }

    /// <summary>
    ///     Opens the "..." menu of the named repo's card.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="repoName">The repo's folder name.</param>
    /// <returns>The open menu.</returns>
    private static MenuFlyout OpenCardMenu(MainWindow window, string repoName)
    {
        var button = window.GetVisualDescendantsOf<Button>("MoreActionsButton")
            .Single(candidate => candidate.DataContext is RepoCardViewModel card && card.RepoName == repoName);
        var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
        flyout.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        return flyout;
    }

    /// <summary>
    ///     Creates an empty repo folder with the given name under a unique parent.
    /// </summary>
    /// <param name="name">The folder name.</param>
    /// <returns>The folder's path.</returns>
    private string CreateRepoDirectory(string name)
    {
        var path = Path.Combine(CreateTempDirectory(), name);
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        File.WriteAllText(Path.Combine(path, ".git", "HEAD"), "ref: refs/heads/main\n");
        return path;
    }

    /// <summary>
    ///     Creates a unique temporary directory that is removed on disposal.
    /// </summary>
    /// <returns>The directory's path.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AgentControlMultiSelectWindow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }
}
