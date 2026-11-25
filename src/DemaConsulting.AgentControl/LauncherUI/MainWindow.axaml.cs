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

using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.Logging;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.AgentControl.RepoConfig;
using DemaConsulting.AgentControl.RepoSync;
using DemaConsulting.AgentControl.Settings;
using Material.Icons;
using Material.Icons.Avalonia;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     Main launcher window: shows the user-ordered repo list and exposes Add-Repo/Settings actions.
/// </summary>
/// <remarks>
///     Everything this class does is view-layer plumbing that requires a live Avalonia
///     <see cref="Window"/>/<see cref="IStorageProvider"/> (folder-browse dialog, opening
///     <see cref="SettingsWindow"/> and <see cref="ReleaseNotesViewer"/>, wiring per-card events
///     to a message box) - all Launch/Pull/Upgrade/settings-persistence business logic lives in
///     <see cref="MainWindowViewModel"/> and <see cref="RepoCardViewModel"/> so it stays testable
///     without this window ever being constructed in a unit test. That includes the reordering
///     rules: this class also owns the drag-and-drop plumbing (starting a drag from a card or a group
///     header, translating the pointer position into a <see cref="RepoDropTarget"/> or a
///     <see cref="GroupDropTarget"/>, drawing the insertion line and the header tint, the timer-driven
///     auto-scroll near and beyond the list's edges, and accepting folders dropped from the file
///     manager), the color palette pop-ups of cards and groups, the folder picker (several folders at
///     once), the Retry/Discard question when an add could not be saved, and the enabling of the card
///     menu's Move items, while <see cref="MainWindowViewModel"/> decides what is allowed and does it.
///     Adds run in the background through <see cref="MainWindowViewModel.EnqueueAddReposAsync"/>, with
///     their progress in the global progress strip under the app bar (bound to
///     <see cref="MainWindowViewModel.GlobalProgress"/>), so the window stays usable meanwhile.
/// </remarks>
internal sealed partial class MainWindow : Window
{
    /// <summary>
    ///     Tracks which repo cards have already had their (lazy, non-cacheable) working-tree check
    ///     (<see cref="RepoCardViewModel.RefreshRepoStateAsync"/>) triggered by this window, so the
    ///     first-attach trigger (see remarks on <see cref="RepoCard_AttachedToVisualTree"/>) does
    ///     not repeatedly re-run the working-tree check every time a virtualized list container
    ///     is recycled/reattached for the same card.
    /// </summary>
    private readonly HashSet<RepoCardViewModel> _dirtyStatusRefreshedCards = [];

    /// <summary>
    ///     Tracks every non-modal <see cref="ReleaseNotesViewer"/>
    ///     opened from this window, so they can be explicitly closed when this window closes
    ///     (see <see cref="MainWindow_Closing"/>). These are deliberately opened without an
    ///     Avalonia owner: owned non-modal windows have been observed to block this window's own
    ///     close on Windows (the owner cannot finish closing while an owned window remains open),
    ///     so this window instead takes responsibility for closing them itself.
    /// </summary>
    private readonly List<Window> _openChildWindows = [];

    /// <summary>
    ///     Gets the non-modal child windows (the release-notes windows) that are open now, so tests can
    ///     tell whether, and when, an upgrade's release notes were shown.
    /// </summary>
    internal IReadOnlyList<Window> OpenChildWindows => _openChildWindows;

    /// <summary>
    ///     Logs window lifecycle events (child windows opened/closed, this window closing) so an
    ///     operator can diagnose orphaned-window or shutdown-timing issues from the log file
    ///     rather than only from live observation.
    /// </summary>
    private readonly ILogger<MainWindow> _logger = AppLogging.Factory.CreateLogger<MainWindow>();

    /// <summary>
    ///     Initializes a new <see cref="MainWindow"/>, loading its compiled XAML.
    /// </summary>
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _autoScrollTimer = new DispatcherTimer { Interval = AutoScrollInterval };
        _autoScrollTimer.Tick += (_, _) => AutoScrollTick();
        WireRepoListDragAndDrop();
        WireRepoListSelection();
        DataContextChanged += OnDataContextChanged;
        Closing += MainWindow_Closing;
        Closed += (_, _) =>
        {
            StopAutoScroll();
            DetachViewModel();
            (DataContext as MainWindowViewModel)?.Dispose();
        };
        // Remember which card's favorite star has the keyboard focus (see RestoreStarFocus)
        AddHandler(GotFocusEvent, OnAnyGotFocus, RoutingStrategies.Bubble);
        Deactivated += (_, _) => _focusedStarCard = null;
    }

    /// <summary>
    ///     The view model whose events this window handles (the data context it was last attached to), or
    ///     <see langword="null"/> when it has none; kept so those subscriptions can be removed again.
    /// </summary>
    private MainWindowViewModel? _subscribedViewModel;

    /// <summary>
    ///     The card whose favorite star had the focus most recently, or <see langword="null"/> when the
    ///     focus went anywhere else since (or the window was deactivated).
    /// </summary>
    private RepoCardViewModel? _focusedStarCard;

    /// <summary>
    ///     How that star got the focus (keyboard or pointer), so it is given back the same way.
    /// </summary>
    private NavigationMethod _focusedStarMethod;

    /// <summary>
    ///     Records which card's favorite star takes the focus, and forgets it when the focus goes to
    ///     any other control.
    /// </summary>
    /// <param name="sender">This window (unused).</param>
    /// <param name="e">The focus event; its source is the control that took the focus.</param>
    private void OnAnyGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is ToggleButton { Name: "FavoriteToggleButton", DataContext: RepoCardViewModel card })
        {
            _focusedStarCard = card;
            _focusedStarMethod = e.NavigationMethod;
        }
        else
        {
            _focusedStarCard = null;
        }
    }

    /// <summary>
    ///     Gives the keyboard focus back to a card's favorite star after the list has been regrouped.
    /// </summary>
    /// <remarks>
    ///     Favoriting (or unfavoriting) moves the card to another section, which re-creates its
    ///     controls, so the star that had the focus is gone and the focus would be lost - a keyboard
    ///     user would have to Tab from the top again. When the star of that repo had the focus
    ///     (reached by keyboard or by the pointer; it is returned the same way, so the focus ring
    ///     shows only for the keyboard), the new star of the same repo takes it once the list has been
    ///     laid out. A card that is not shown afterwards (its new section is collapsed) simply has no
    ///     star to focus; the focus is never moved when the star did not have it.
    /// </remarks>
    /// <param name="card">The card whose favorite state changed.</param>
    private void RestoreStarFocus(RepoCardViewModel card)
    {
        if (_focusedStarCard != card)
        {
            return;
        }

        var method = _focusedStarMethod;
        Dispatcher.UIThread.Post(() => FocusStar(card, method, retry: true), DispatcherPriority.Loaded);
    }

    /// <summary>
    ///     Focuses the favorite star of the card's current row, trying once more later when the row's
    ///     controls are not created yet.
    /// </summary>
    /// <param name="card">The card.</param>
    /// <param name="method">How to give the focus.</param>
    /// <param name="retry">Whether to try once more when the star is not there yet.</param>
    private void FocusStar(RepoCardViewModel card, NavigationMethod method, bool retry)
    {
        var star = this.GetVisualDescendants().OfType<ToggleButton>()
            .FirstOrDefault(button => button.Name == "FavoriteToggleButton" && ReferenceEquals(button.DataContext, card));
        if (star is not null)
        {
            star.Focus(method);
        }
        else if (retry)
        {
            Dispatcher.UIThread.Post(() => FocusStar(card, method, retry: false), DispatcherPriority.Background);
        }
    }

    /// <summary>
    ///     Opens <paramref name="window"/> non-modally, tracking it in
    ///     <see cref="_openChildWindows"/> so <see cref="MainWindow_Closing"/> can close it
    ///     alongside this window, rather than leaving it orphaned.
    /// </summary>
    /// <param name="window">The window to open and track.</param>
    private void ShowTrackedChildWindow(Window window)
    {
        _openChildWindows.Add(window);
        window.Closed += (_, _) =>
        {
            _openChildWindows.Remove(window);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Child window {WindowType} closed", window.GetType().Name);
            }
        };

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Opening tracked child window {WindowType}", window.GetType().Name);
        }

        window.Show();
    }

    /// <summary>
    ///     Closes every still-open tracked child window (see <see cref="_openChildWindows"/>)
    ///     when this window closes, so a <see cref="ReleaseNotesViewer"/>
    ///     never outlives the main window.
    /// </summary>
    /// <param name="sender">This window.</param>
    /// <param name="e">Closing event arguments (unused).</param>
    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Main window closing; force-closing {ChildWindowCount} open tracked child window(s)",
                _openChildWindows.Count);
        }

        // Close() mutates _openChildWindows via each window's Closed handler, so close over a
        // snapshot rather than the live list.
        foreach (var window in _openChildWindows.ToArray())
        {
            window.Close();
        }
    }

    /// <summary>
    ///     Wires up per-repo-card event subscriptions once the view model is attached, and keeps
    ///     them current as cards are added to <see cref="MainWindowViewModel.RepoCards"/>.
    /// </summary>
    /// <remarks>
    ///     A view model this window was attached to before is detached first (see
    ///     <see cref="DetachViewModel"/>), so assigning a data context again - or none - never leaves the
    ///     window handling the events of two view models, or of one twice.
    /// </remarks>
    /// <param name="sender">This window (unused).</param>
    /// <param name="e">Event arguments (unused).</param>
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        DetachViewModel();
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        _subscribedViewModel = viewModel;
        foreach (var card in viewModel.RepoCards)
        {
            AttachCardHandlers(card);
        }

        viewModel.RepoCards.CollectionChanged += OnRepoCardsChanged;
        viewModel.PropertyChanged += ViewModelPropertyChanged;
        // A change that could not be saved in the background is told once per failure streak; the
        // message box is not awaited, so the window keeps working behind it
        viewModel.SettingsSaveFailed += OnSettingsSaveFailed;
        viewModel.GroupRenameRequested += OnGroupRenameRequested;
        viewModel.GroupRemoveRequested += OnGroupRemoveRequested;
        viewModel.GroupRemoveWithContentsRequested += OnGroupRemoveWithContentsRequested;
        viewModel.GroupAddRepoRequested += OnGroupAddRepoRequested;
        viewModel.GroupNewSubgroupRequested += OnGroupNewSubgroupRequested;
        viewModel.AddSaveFailed += OnAddSaveFailed;

        // The upgrade check reads package sources, which may be slow or unreachable, so it starts
        // in the background now that the window has its data rather than delaying the window
        _ = viewModel.BeginUpgradeRefresh();

        // ...and the same check repeats every five minutes while the window stays open, so a
        // package published meanwhile shows up. The timer fires off the UI thread, so each tick is
        // posted back to it.
        viewModel.StartPeriodicUpgradeRecheck(action => Dispatcher.UIThread.Post(action));
    }

    /// <summary>
    ///     Unsubscribes this window from the view model it handles the events of, and from its cards, so
    ///     it stops reacting to a view model it no longer shows (a data context that was replaced or
    ///     cleared, or the window closing).
    /// </summary>
    private void DetachViewModel()
    {
        if (_subscribedViewModel is not { } viewModel)
        {
            return;
        }

        _subscribedViewModel = null;
        viewModel.RepoCards.CollectionChanged -= OnRepoCardsChanged;
        viewModel.PropertyChanged -= ViewModelPropertyChanged;
        viewModel.SettingsSaveFailed -= OnSettingsSaveFailed;
        viewModel.GroupRenameRequested -= OnGroupRenameRequested;
        viewModel.GroupRemoveRequested -= OnGroupRemoveRequested;
        viewModel.GroupRemoveWithContentsRequested -= OnGroupRemoveWithContentsRequested;
        viewModel.GroupAddRepoRequested -= OnGroupAddRepoRequested;
        viewModel.GroupNewSubgroupRequested -= OnGroupNewSubgroupRequested;
        viewModel.AddSaveFailed -= OnAddSaveFailed;
        DetachCardHandlers([.. _cardHandlerRemovers.Keys]);
    }

    /// <summary>
    ///     Keeps the list's selection in step with the view model's selected cards.
    /// </summary>
    /// <param name="sender">The view model.</param>
    /// <param name="args">Names the property that changed.</param>
    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainWindowViewModel.SelectedCards) && sender is MainWindowViewModel viewModel)
        {
            SyncListSelectionFromViewModel(viewModel);
        }
    }

    /// <summary>
    ///     Tells the user that a change could not be saved, without waiting for the answer.
    /// </summary>
    /// <param name="sender">The view model (unused).</param>
    /// <param name="message">What went wrong.</param>
    private void OnSettingsSaveFailed(object? sender, string message) =>
        _ = new MessageBoxWindow(message).ShowDialog(this);

    /// <summary>
    ///     Asks for the new name of a group whose header asked to be renamed.
    /// </summary>
    /// <param name="sender">The view model.</param>
    /// <param name="name">The group.</param>
    private async void OnGroupRenameRequested(object? sender, string name)
    {
        if (sender is MainWindowViewModel viewModel)
        {
            await ShowRenameGroupPrompt(viewModel, name);
        }
    }

    /// <summary>
    ///     Confirms and deletes a group, keeping its contents, whose header asked for it.
    /// </summary>
    /// <param name="sender">The view model.</param>
    /// <param name="name">The group.</param>
    private async void OnGroupRemoveRequested(object? sender, string name)
    {
        if (sender is MainWindowViewModel viewModel)
        {
            await ConfirmAndRemoveGroup(viewModel, name);
        }
    }

    /// <summary>
    ///     Confirms and deletes a group together with its contents, whose header asked for it.
    /// </summary>
    /// <param name="sender">The view model.</param>
    /// <param name="name">The group.</param>
    private async void OnGroupRemoveWithContentsRequested(object? sender, string name)
    {
        if (sender is MainWindowViewModel viewModel)
        {
            await ConfirmAndRemoveGroupWithContents(viewModel, name);
        }
    }

    /// <summary>
    ///     Asks for folders to add to a group whose header asked for it, and tells the outcome.
    /// </summary>
    /// <param name="sender">The view model.</param>
    /// <param name="name">The group.</param>
    private async void OnGroupAddRepoRequested(object? sender, string name)
    {
        if (sender is MainWindowViewModel viewModel)
        {
            await ShowNoticeAsync(await AddRepoAsync(viewModel, name));
        }
    }

    /// <summary>
    ///     Asks for the name of a new group inside a group whose header asked for it.
    /// </summary>
    /// <param name="sender">The view model.</param>
    /// <param name="name">The parent group.</param>
    private async void OnGroupNewSubgroupRequested(object? sender, string name)
    {
        if (sender is MainWindowViewModel viewModel)
        {
            await ShowNewSubgroupPrompt(viewModel, name);
        }
    }

    /// <summary>
    ///     Asks whether to retry or discard an add whose save failed.
    /// </summary>
    /// <param name="sender">The view model (unused).</param>
    /// <param name="request">The failed save.</param>
    private async void OnAddSaveFailed(object? sender, SettingsSaveRetryRequest request) =>
        await AskAddSaveRetryAsync(request);

    /// <summary>
    ///     Attaches this window's error/release-notes handlers to newly-added repo cards.
    /// </summary>
    /// <param name="sender">The observable collection (unused).</param>
    /// <param name="e">Describes which cards were added.</param>
    private void OnRepoCardsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A reorder only moves cards within the list; they stay attached, and the per-card state
        // this window keeps (pending questions, shown dialogs) must survive it
        if (e.Action == NotifyCollectionChangedAction.Move)
        {
            return;
        }
        // Cards that left the list: undo what was attached and drop what was remembered about them
        if (e.OldItems is not null)
        {
            DetachCardHandlers(e.OldItems.OfType<RepoCardViewModel>());
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            DetachCardHandlers([.. _cardHandlerRemovers.Keys]);
        }

        if (e.NewItems is null)
        {
            return;
        }

        foreach (var newItem in e.NewItems)
        {
            if (newItem is RepoCardViewModel card)
            {
                AttachCardHandlers(card);
            }
        }
    }

    /// <summary>
    ///     Subscribes to a repo card's <see cref="RepoCardViewModel.ErrorOccurred"/>,
    ///     <see cref="RepoCardViewModel.ReleaseNotesReady"/>,
    ///     <see cref="RepoCardViewModel.RemoveRequested"/>,
    ///     <see cref="RepoCardViewModel.AgentsMdTemplateOfferRequested"/>, and
    ///     <see cref="RepoCardViewModel.ContentChangeConfirmationRequested"/>, and
    ///     <see cref="RepoCardViewModel.CommittedFilesConversionRequested"/>, and
    ///     <see cref="RepoCardViewModel.AgentFilesChoiceRequested"/> events so failures show
    ///     a message box, successful upgrades open a non-modal <see cref="ReleaseNotesViewer"/>
    ///     tracked by this window (see <see cref="ShowTrackedChildWindow"/>, so it closes when
    ///     this window does, rather than outliving it), a remove request shows a confirmation
    ///     dialog before actually removing the card, an AGENTS.md template offer shows a
    ///     modal <see cref="ConfirmationWindow"/> before writing the template or recording a
    ///     decline, a republished package version asks for confirmation before it replaces
    ///     the copy the repo was set up with, and a repo whose agent folders are tracked by git asks
    ///     for confirmation before they are taken out of git, and a launch whose agent files differ
    ///     from the pin (or could not be restored) asks what to do before the agent tool starts.
    /// </summary>
    /// <param name="card">The card to subscribe to.</param>
    private void AttachCardHandlers(RepoCardViewModel card)
    {
        // Each handler is a local so exactly the same delegate can be detached again when the card is
        // removed: a removed card must not keep this window alive (or be kept alive by it)
        void OnFavoriteChanged(object? sender, EventArgs e) => RestoreStarFocus(card);
        void OnErrorOccurred(object? sender, string message) => new MessageBoxWindow(message).ShowDialog(this);
        async void OnReleaseNotesReady(object? sender, string releaseNotes) =>
            await ShowReleaseNotesAsync(card, releaseNotes);
        async void OnRemoveRequested(object? sender, EventArgs e) => await ConfirmAndRemoveCard(card);
        async void OnSelectPackageRequested(object? sender, EventArgs e) => await ShowSelectPackageDialog(card);
        async void OnNewGroupRequested(object? sender, EventArgs e) => await ShowNewGroupPrompt(card);
        async void OnTemplateOfferRequested(object? sender, string templateContent)
        {
            // Remember the question until it is answered, so the release notes that follow it can wait
            // (the handler runs up to its first await before returning, so this is in place in time)
            var answered = new TaskCompletionSource();
            _agentsMdPrompts[card] = answered.Task;
            try
            {
                await ShowAgentsMdTemplateOfferDialog(card, templateContent);
            }
            finally
            {
                answered.SetResult();
            }
        }

        async void OnContentChangeConfirmationRequested(object? sender, ContentChangeConfirmationRequest request) =>
            await ConfirmContentChangeAsync(card, request);

        async void OnCommittedFilesConversionRequested(object? sender, CommittedFilesConversionRequest request) =>
            await ConfirmCommittedFilesConversionAsync(request);

        async void OnAgentFilesChoiceRequested(object? sender, AgentFilesChoiceRequest request) =>
            await ConfirmAgentFilesChoiceAsync(request);

        // Runs after the view model has regrouped the list (its handler was attached when the card was created)
        card.FavoriteChanged += OnFavoriteChanged;
        card.ErrorOccurred += OnErrorOccurred;
        card.ReleaseNotesReady += OnReleaseNotesReady;
        card.RemoveRequested += OnRemoveRequested;
        card.SelectPackageRequested += OnSelectPackageRequested;
        card.NewGroupRequested += OnNewGroupRequested;
        card.AgentsMdTemplateOfferRequested += OnTemplateOfferRequested;
        card.ContentChangeConfirmationRequested += OnContentChangeConfirmationRequested;
        card.CommittedFilesConversionRequested += OnCommittedFilesConversionRequested;
        card.AgentFilesChoiceRequested += OnAgentFilesChoiceRequested;

        _cardHandlerRemovers[card] = () =>
        {
            card.FavoriteChanged -= OnFavoriteChanged;
            card.ErrorOccurred -= OnErrorOccurred;
            card.ReleaseNotesReady -= OnReleaseNotesReady;
            card.RemoveRequested -= OnRemoveRequested;
            card.SelectPackageRequested -= OnSelectPackageRequested;
            card.NewGroupRequested -= OnNewGroupRequested;
            card.AgentsMdTemplateOfferRequested -= OnTemplateOfferRequested;
            card.ContentChangeConfirmationRequested -= OnContentChangeConfirmationRequested;
            card.CommittedFilesConversionRequested -= OnCommittedFilesConversionRequested;
            card.AgentFilesChoiceRequested -= OnAgentFilesChoiceRequested;
        };
    }

    /// <summary>
    ///     Detaches the handlers of <see cref="AttachCardHandlers"/> from each card and forgets what
    ///     this window remembered about it.
    /// </summary>
    /// <remarks>
    ///     Called when cards leave <see cref="MainWindowViewModel.RepoCards"/>. Without it a removed
    ///     card stayed referenced by this window's handlers and by its per-card bookkeeping
    ///     (<see cref="_dirtyStatusRefreshedCards"/>, <see cref="_agentsMdPrompts"/>, the focused
    ///     star), and a pending operation could still show a message box or release notes for a repo
    ///     that is no longer listed.
    /// </remarks>
    /// <param name="cards">The cards that were removed.</param>
    private void DetachCardHandlers(IEnumerable<RepoCardViewModel> cards)
    {
        foreach (var card in cards)
        {
            if (_cardHandlerRemovers.Remove(card, out var remove))
            {
                remove();
            }

            _dirtyStatusRefreshedCards.Remove(card);
            _agentsMdPrompts.Remove(card);
            _paletteCard = _paletteCard == card ? null : _paletteCard;
            _focusedStarCard = _focusedStarCard == card ? null : _focusedStarCard;
        }
    }

    /// <summary>
    ///     Removes, per card, what <see cref="AttachCardHandlers"/> attached, so
    ///     <see cref="DetachCardHandlers"/> can undo it.
    /// </summary>
    private readonly Dictionary<RepoCardViewModel, Action> _cardHandlerRemovers = [];

    /// <summary>
    ///     Gets how many cards this window currently holds handlers for (and remembers things about);
    ///     it equals the number of cards in the list, which is what tests of the removal clean-up check.
    /// </summary>
    internal int TrackedCardCount => _cardHandlerRemovers.Count;

    /// <summary>
    ///     The AGENTS.md template questions that are open (a card has one at most), so the release notes
    ///     of the same upgrade are shown only after the question is answered.
    /// </summary>
    private readonly Dictionary<RepoCardViewModel, Task> _agentsMdPrompts = [];

    /// <summary>
    ///     Shows an upgrade's release notes in a non-modal <see cref="ReleaseNotesViewer"/>, after the
    ///     AGENTS.md template question of the same upgrade (when there is one) has been answered.
    /// </summary>
    /// <remarks>
    ///     Upgrading may end with both the question (modal, over this window) and the release notes (a
    ///     window of their own). Shown together, the question covered the notes, or the notes window
    ///     was left behind the question. So the question comes first and the notes follow it, opening
    ///     in front as the completion of the upgrade. Nothing is shown when this window has closed in
    ///     the meantime, so the notes never outlive it.
    /// </remarks>
    /// <param name="card">The card that was upgraded.</param>
    /// <param name="releaseNotes">The new package's release notes.</param>
    /// <returns>A task that completes when the notes window has been opened (or skipped).</returns>
    private async Task ShowReleaseNotesAsync(RepoCardViewModel card, string releaseNotes)
    {
        if (_agentsMdPrompts.Remove(card, out var question))
        {
            await question;
        }

        if (!IsVisible)
        {
            return;
        }

        var viewer = new ReleaseNotesViewer(new ReleaseNotesViewerViewModel(card.RepoName, releaseNotes));
        ShowTrackedChildWindow(viewer);
        viewer.Activate();
    }

    /// <summary>
    ///     Shows the modal <see cref="SelectPackageWindow"/> for <paramref name="card"/>, and, if
    ///     the user confirmed a package, applies it via
    ///     <see cref="RepoCardViewModel.ApplySelectedPackageAsync"/>.
    /// </summary>
    /// <param name="card">The card that requested package selection.</param>
    private async Task ShowSelectPackageDialog(RepoCardViewModel card)
    {
        SelectPackageWindowViewModel viewModel;
        try
        {
            viewModel = card.IsPackageSelectionNeeded
                ? new SelectPackageWindowViewModel(card.CreateSourceRegistry(), card.PackageVersionCache)
                : new SelectPackageWindowViewModel(
                    card.CreateSourceRegistry(),
                    card.PackageVersionCache,
                    card.SupplyingSource,
                    card.PinnedPackageName,
                    card.PinnedPackageVersion);
        }
        catch (InvalidOperationException ex)
        {
            // Invalid source configuration must be reported to the user, not crash the
            // application from this fire-and-forget event handler
            await new MessageBoxWindow($"Package sources are unavailable: {SecretRedactor.SafeMessage(ex)}").ShowDialog(this);
            return;
        }

        var result = await new SelectPackageWindow(viewModel).ShowDialog<PackageReference?>(this);
        if (result is not null)
        {
            await card.ApplySelectedPackageAsync(result);
        }
    }

    /// <summary>
    ///     Handles a card's request to be removed: when the card is one of several selected cards the
    ///     request is for all of them (the menu item says how many), otherwise for the card alone. The
    ///     user confirms first; see <see cref="ConfirmAndRemoveCards"/> and
    ///     <see cref="ConfirmAndRemoveOneCard"/>.
    /// </summary>
    /// <param name="card">The card whose "Remove from list" item the user chose.</param>
    private async Task ConfirmAndRemoveCard(RepoCardViewModel card)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            viewModel.SelectedCards.Count > 1 &&
            viewModel.SelectedCards.Contains(card))
        {
            await ConfirmAndRemoveCards(viewModel, [.. viewModel.SelectedCards]);
            return;
        }

        await ConfirmAndRemoveOneCard(card);
    }

    /// <summary>
    ///     Shows the <see cref="ConfirmationWindow"/> asking the user to confirm removing
    ///     <paramref name="card"/> from AgentControl's recent-repos list, and only calls
    ///     <see cref="MainWindowViewModel.RemoveRepo"/> if the user confirms. Never removes
    ///     anything from disk - the message text makes this explicit.
    /// </summary>
    /// <param name="card">The card the user asked to remove.</param>
    private async Task ConfirmAndRemoveOneCard(RepoCardViewModel card)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var confirmed = await new ConfirmationWindow(
                $"Remove '{card.RepoName}' from AgentControl's recent-repos list?\n\n" +
                "This only removes the repo from AgentControl's list - it does not delete or " +
                "otherwise modify anything on disk.")
            .ShowDialog<bool>(this);

        if (confirmed)
        {
            // Applied at once and saved in the background; a failed save is reported once through
            // SettingsSaveFailed
            viewModel.RemoveRepo(card);
        }
    }

    /// <summary>
    ///     Shows the <see cref="ConfirmationWindow"/> asking whether a republished package version
    ///     (same version, different content) may replace the copy <paramref name="card"/>'s repo was
    ///     set up with, and applies the package with the user's confirmation only if they agree.
    /// </summary>
    /// <param name="card">The card whose Select Package found the change.</param>
    /// <param name="request">The question and the selection to re-apply, as carried by
    ///     <see cref="RepoCardViewModel.ContentChangeConfirmationRequested"/>.</param>
    /// <remarks>
    ///     The default (focused) answer is No, as for every <see cref="ConfirmationWindow"/>, so
    ///     pressing Enter or closing the dialog leaves the repo untouched.
    /// </remarks>
    private async Task ConfirmContentChangeAsync(RepoCardViewModel card, ContentChangeConfirmationRequest request)
    {
        var confirmed = await new ConfirmationWindow(request.Message).ShowDialog<bool>(this);
        if (confirmed)
        {
            await card.ApplySelectedPackageAsync(request.Selection, acceptContentChange: true);
        }
    }

    /// <summary>
    ///     Shows the <see cref="ConfirmationWindow"/> asking whether AgentControl may stop git tracking
    ///     the agent folders of a repo before applying a package, and reports the answer to the card's
    ///     waiting package operation.
    /// </summary>
    /// <param name="request">The question and the way to answer it, as carried by
    ///     <see cref="RepoCardViewModel.CommittedFilesConversionRequested"/>.</param>
    /// <remarks>
    ///     The dialog is modal, so the user cannot act on the window behind it while the question is
    ///     open, and the card stays busy until it is answered. The default (focused) answer is No, as
    ///     for every <see cref="ConfirmationWindow"/>, so pressing Enter or closing the dialog changes
    ///     nothing. The request is always answered - an unexpected failure to show the dialog counts as
    ///     No - because the card's operation would otherwise wait for ever.
    /// </remarks>
    private async Task ConfirmCommittedFilesConversionAsync(CommittedFilesConversionRequest request)
    {
        var confirmed = false;
        try
        {
            confirmed = await new ConfirmationWindow(request.Message).ShowDialog<bool>(this);
        }
        catch (Exception ex)
        {
            // This runs from an async void handler: an escaping exception would be unhandled on the
            // UI thread, so a failure to show the dialog is logged and counts as No
            _logger.LogError(ex, "Could not show the committed-agent-files conversion question");
        }
        finally
        {
            request.Respond(confirmed);
        }
    }

    /// <summary>
    ///     Shows the <see cref="ConfirmationWindow"/> asking the user about a repo's agent files
    ///     before a launch - keep or update files that differ from the pin, or launch anyway or
    ///     cancel after a failed restore - with the request's own button labels, and reports the
    ///     answer to the card's waiting launch.
    /// </summary>
    /// <param name="request">The question, its two button labels and the way to answer it, as
    ///     carried by <see cref="RepoCardViewModel.AgentFilesChoiceRequested"/>.</param>
    /// <remarks>
    ///     The dialog is modal and the card stays busy until it is answered. The default (focused)
    ///     answer is the decline side - "Keep current files" or "Cancel" - so pressing Enter, Escape
    ///     or closing the dialog never replaces files or launches past a failure. The request is
    ///     always answered - an unexpected failure to show the dialog counts as the decline side -
    ///     because the card's launch would otherwise wait for ever.
    /// </remarks>
    private async Task ConfirmAgentFilesChoiceAsync(AgentFilesChoiceRequest request)
    {
        var confirmed = false;
        try
        {
            confirmed = await new ConfirmationWindow(request.Message, request.ConfirmText, request.DeclineText)
                .ShowDialog<bool>(this);
        }
        catch (Exception ex)
        {
            // This runs from an async void handler: an escaping exception would be unhandled on the
            // UI thread, so a failure to show the dialog is logged and counts as the safe side
            _logger.LogError(ex, "Could not show the agent files question");
        }
        finally
        {
            request.Respond(confirmed);
        }
    }

    /// <summary>
    ///     Shows the <see cref="ConfirmationWindow"/> offering to place the package's optional
    ///     <c>AGENTS.md</c> template at <paramref name="card"/>'s repo root, and calls back into
    ///     <see cref="RepoCardViewModel.AcceptAgentsMdTemplateAsync"/> or
    ///     <see cref="RepoCardViewModel.DeclineAgentsMdTemplateAsync"/> depending on the user's choice,
    ///     waiting for the card to write the answer (it does so off the UI thread).
    /// </summary>
    /// <param name="card">The card that just completed a sync offering the template.</param>
    /// <param name="templateContent">The template's text content, as carried by
    ///     <see cref="RepoCardViewModel.AgentsMdTemplateOfferRequested"/>.</param>
    private async Task ShowAgentsMdTemplateOfferDialog(RepoCardViewModel card, string templateContent)
    {
        var confirmed = await new ConfirmationWindow(
                $"The '{card.RepoName}' agent package includes a starting AGENTS.md template, " +
                "and this repo does not have one yet.\n\n" +
                "Would you like AgentControl to add it to the repo root?\n\n" +
                "This is only a starting point - you are responsible for reviewing and " +
                "customizing it for this repo afterward.")
            .ShowDialog<bool>(this);

        if (confirmed)
        {
            await card.AcceptAgentsMdTemplateAsync(templateContent);
        }
        else
        {
            await card.DeclineAgentsMdTemplateAsync();
        }
    }

    /// <summary>
    ///     Triggers a card's user-clicked "Remove from list" menu item. Deliberately routes
    ///     through <see cref="RepoCardViewModel.RemoveCommand"/> (which only raises
    ///     <see cref="RepoCardViewModel.RemoveRequested"/>) rather than calling
    ///     <see cref="MainWindowViewModel.RemoveRepo"/> directly, so the confirmation flow
    ///     wired up in <see cref="AttachCardHandlers"/> always runs first.
    /// </summary>
    /// <param name="sender">The clicked "Remove from list" <see cref="MenuItem"/>.</param>
    /// <param name="e">Routed event arguments (unused).</param>
    private static void RemoveMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: RepoCardViewModel card } && card.RemoveCommand.CanExecute(null))
        {
            card.RemoveCommand.Execute(null);
        }
    }

    /// <summary>
    ///     Triggers a card's user-clicked "Select Package..." menu item. Deliberately routes
    ///     through <see cref="RepoCardViewModel.SelectPackageCommand"/> (which only raises
    ///     <see cref="RepoCardViewModel.SelectPackageRequested"/>) rather than opening the dialog
    ///     directly, so the package-source validation wired up in
    ///     <see cref="RepoCardViewModel.SelectPackageCommand"/>'s execute delegate always runs
    ///     first, exactly like <see cref="RemoveMenuItem_Click"/>.
    /// </summary>
    /// <param name="sender">The clicked "Select Package..." <see cref="MenuItem"/>.</param>
    /// <param name="e">Routed event arguments (unused).</param>
    private static void SelectPackageMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: RepoCardViewModel card } && card.SelectPackageCommand.CanExecute(null))
        {
            card.SelectPackageCommand.Execute(null);
        }
    }

    /// <summary>
    ///     Starts a newly-realized repo card's lazy working-tree check through the shared refresh queue
    ///     (<see cref="RepoCardViewModel.RefreshRepoStateAsync"/> at the priority of a visible card,
    ///     which runs git off the UI thread) the first time its list container attaches to the visual
    ///     tree, and moves any of the card's reads that still wait in the queue ahead of the background
    ///     ones.
    /// </summary>
    /// <remarks>
    ///     <b>Simplification note:</b> this is a "first-realization" trigger, not true
    ///     viewport-visibility tracking. The list is virtualized: its default
    ///     <see cref="VirtualizingStackPanel"/> realizes only the rows in and near the viewport, and a
    ///     recycled container's content is cleared and rebuilt, so the trigger fires for the cards
    ///     realized when the window loads and later for cards as they are scrolled into view.
    ///     <see cref="_dirtyStatusRefreshedCards"/> keeps it to once per card, so a card scrolled out of
    ///     and back into view is not checked again. Per the plan's own guidance, literal
    ///     viewport-visibility tracking is not required for this feature; this approximation keeps the
    ///     working-tree dirty check lazy (never run during the cheap/eager startup pass) while still
    ///     populating it for the user shortly after a card is first shown, without the added
    ///     complexity of scroll-position-aware tracking.
    /// </remarks>
    /// <param name="sender">The card's container <see cref="Border"/>.</param>
    /// <param name="e">Event arguments (unused).</param>
    private void RepoCard_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Border { DataContext: RepoCardViewModel card } ||
            !_dirtyStatusRefreshedCards.Add(card))
        {
            return;
        }

        // The check runs in the background through the shared queue, a couple of repos at a time:
        // showing many cards at once (startup, or a batch of repos just added) never waits for git and
        // never starts a git per card at once; each card shows "not checked yet" until it answers. A
        // card on screen goes ahead of the cards nobody sees yet.
        card.PrioritizeRepoState(RepoRefreshPriority.Visible);
        _ = card.RefreshRepoStateAsync(RepoRefreshPriority.Visible, cheapFacts: false, workingTree: true);
    }

    /// <summary>
    ///     Fills the "Move to group" submenu of a card's "..." menu just before the menu opens: one
    ///     entry per existing group (the repo's own group is marked), then "New group..." and, when
    ///     the repo is in a group, "Remove from group".
    /// </summary>
    /// <remarks>
    ///     The entries depend on the groups that exist at that moment, which a XAML template cannot
    ///     express as one mixed list of items and commands, so the code-behind builds them from the
    ///     card's own data and commands; the choices themselves are carried out by
    ///     <see cref="RepoCardViewModel"/>. The card is found through the flyout's placement
    ///     target, whose data context is the card.
    /// </remarks>
    /// <param name="sender">The card's <see cref="MenuFlyout"/>.</param>
    /// <param name="e">Event arguments (unused).</param>
    private static void CardMenu_Opening(object? sender, EventArgs e)
    {
        if (sender is not MenuFlyout { Target.DataContext: RepoCardViewModel card } flyout)
        {
            return;
        }

        UpdateMoveMenuItems(flyout, card);

        var submenu = flyout.Items.OfType<MenuItem>().FirstOrDefault(item => item.Name == "MoveToGroupMenuItem");
        if (submenu is null)
        {
            return;
        }

        var entries = new List<object>();
        var groups = card.GetExistingGroupNames();
        var viewModel = TopLevel.GetTopLevel(flyout.Target)?.DataContext as MainWindowViewModel;
        foreach (var group in groups)
        {
            // The header is a TextBlock so an underscore in a group name is not taken as an access key;
            // a nested group's tooltip gives its path so groups inside different groups are told apart
            var isCurrent = RepoGroupName.Comparer.Equals(group, card.GroupName ?? string.Empty);
            var path = viewModel?.GetGroupPath(group) ?? group;
            var entry = new MenuItem
            {
                Header = new TextBlock { Text = group },
                Icon = isCurrent ? new MaterialIcon { Kind = MaterialIconKind.Check } : null
            };
            AutomationProperties.SetAutomationId(entry, "MoveToGroupItem");
            AutomationProperties.SetName(entry, group);
            ToolTip.SetTip(
                entry,
                isCurrent ? $"{card.RepoName} is in the group {path}." : $"Move {card.RepoName} to the group {path}.");
            entry.Click += (_, _) => card.MoveToGroup(group);
            entries.Add(entry);
        }

        if (groups.Count > 0)
        {
            entries.Add(new Separator());
        }

        var newGroup = new MenuItem
        {
            Header = "New group...",
            Icon = new MaterialIcon { Kind = MaterialIconKind.FolderPlus },
            Command = card.NewGroupCommand
        };
        AutomationProperties.SetAutomationId(newGroup, "NewGroupMenuItem");
        ToolTip.SetTip(newGroup, $"Create a group and move {card.RepoName} into it");
        entries.Add(newGroup);

        if (card.HasGroup)
        {
            var remove = new MenuItem
            {
                Header = "Remove from group",
                Icon = new MaterialIcon { Kind = MaterialIconKind.FolderRemove },
                Command = card.RemoveFromGroupCommand
            };
            AutomationProperties.SetAutomationId(remove, "RemoveFromGroupMenuItem");
            ToolTip.SetTip(remove, $"Take {card.RepoName} out of the group {card.GroupName}");
            entries.Add(remove);
        }

        submenu.Items.Clear();
        foreach (var entry in entries)
        {
            submenu.Items.Add(entry);
        }
    }

    /// <summary>
    ///     Enables or disables the Reorder submenu and its four Move items of a card's menu just before
    ///     it opens, and gives a disabled item the reason as its tooltip.
    /// </summary>
    /// <remarks>
    ///     The rules (the filter has text, or the repo is already at that end of its section, or it is
    ///     the only repo of its section) belong to <see cref="MainWindowViewModel.GetMoveDisabledReason"/>
    ///     and <see cref="MainWindowViewModel.GetReorderDisabledReason"/>; this only shows them. The
    ///     menu cannot change while it is open, so deciding once on opening is enough. The window is
    ///     found through the flyout's placement target.
    /// </remarks>
    /// <param name="flyout">The card's menu.</param>
    /// <param name="card">The card the menu belongs to.</param>
    private static void UpdateMoveMenuItems(MenuFlyout flyout, RepoCardViewModel card)
    {
        if (TopLevel.GetTopLevel(flyout.Target)?.DataContext is not MainWindowViewModel viewModel ||
            flyout.Items.OfType<MenuItem>().FirstOrDefault(item => item.Name == "ReorderMenuItem") is not { } reorder)
        {
            return;
        }

        var reorderReason = viewModel.GetReorderDisabledReason(card);
        reorder.IsEnabled = reorderReason is null;
        ToolTip.SetTip(reorder, reorderReason ?? "Move this repo within its section");

        var moves = new (string Name, RepoMove Move, string EnabledTooltip)[]
        {
            ("MoveToTopMenuItem", RepoMove.Top, "Put this repo first in its section"),
            ("MoveUpMenuItem", RepoMove.Up, "Move this repo one place up in its section"),
            ("MoveDownMenuItem", RepoMove.Down, "Move this repo one place down in its section"),
            ("MoveToBottomMenuItem", RepoMove.Bottom, "Put this repo last in its section")
        };
        foreach (var (name, move, enabledTooltip) in moves)
        {
            if (reorder.Items.OfType<MenuItem>().FirstOrDefault(item => item.Name == name) is not { } item)
            {
                continue;
            }

            var reason = viewModel.GetMoveDisabledReason(card, move);
            item.IsEnabled = reason is null;
            ToolTip.SetTip(item, reason ?? enabledTooltip);
        }
    }

    /// <summary>
    ///     Moves the card of the clicked Move menu item through the view model.
    /// </summary>
    /// <param name="sender">The clicked <see cref="MenuItem"/>; its data context is the card.</param>
    /// <param name="move">The move the item stands for.</param>
    private void MoveMenuItemClicked(object? sender, RepoMove move)
    {
        if (sender is MenuItem { DataContext: RepoCardViewModel card } && DataContext is MainWindowViewModel viewModel)
        {
            // Applied at once and saved in the background; a failed save is reported by the view model
            viewModel.MoveCard(card, move);
        }
    }

    /// <summary>
    ///     Handles the "Move to top" item.
    /// </summary>
    /// <param name="sender">The clicked <see cref="MenuItem"/>.</param>
    /// <param name="e">Routed event arguments (unused).</param>
    private void MoveToTopMenuItem_Click(object? sender, RoutedEventArgs e) => MoveMenuItemClicked(sender, RepoMove.Top);

    /// <summary>
    ///     Handles the "Move up" item.
    /// </summary>
    /// <param name="sender">The clicked <see cref="MenuItem"/>.</param>
    /// <param name="e">Routed event arguments (unused).</param>
    private void MoveUpMenuItem_Click(object? sender, RoutedEventArgs e) => MoveMenuItemClicked(sender, RepoMove.Up);

    /// <summary>
    ///     Handles the "Move down" item.
    /// </summary>
    /// <param name="sender">The clicked <see cref="MenuItem"/>.</param>
    /// <param name="e">Routed event arguments (unused).</param>
    private void MoveDownMenuItem_Click(object? sender, RoutedEventArgs e) => MoveMenuItemClicked(sender, RepoMove.Down);

    /// <summary>
    ///     Handles the "Move to bottom" item.
    /// </summary>
    /// <param name="sender">The clicked <see cref="MenuItem"/>.</param>
    /// <param name="e">Routed event arguments (unused).</param>
    private void MoveToBottomMenuItem_Click(object? sender, RoutedEventArgs e) =>
        MoveMenuItemClicked(sender, RepoMove.Bottom);

    /// <summary>
    ///     Whether the list's selection is being set from the view model, so the selection events that
    ///     raises are not taken for the user's choice.
    /// </summary>
    private bool _isSyncingSelection;

    /// <summary>
    ///     Connects the repo list's selection: what the user selects (with Ctrl+click, Shift+click or
    ///     Ctrl+A) is told to the view model, and the Delete key asks to remove the selected cards.
    /// </summary>
    private void WireRepoListSelection()
    {
        if (this.FindControl<ListBox>("RepoListBox") is not RepoListBox list)
        {
            return;
        }

        list.SelectionChanged += RepoList_SelectionChanged;
        list.DeleteRequested += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel { SelectedCards.Count: > 0 } viewModel)
            {
                await ConfirmAndRemoveCards(viewModel, [.. viewModel.SelectedCards]);
            }
        };
    }

    /// <summary>
    ///     Tells the view model which cards are selected, after taking the section headers out of the
    ///     selection (a range, a click on a header or a keyboard move can select one, but only cards are
    ///     ever selected).
    /// </summary>
    /// <param name="sender">The list.</param>
    /// <param name="e">The selection change (unused).</param>
    private void RepoList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection || sender is not ListBox list || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var headers = list.SelectedItems?.OfType<RepoGroupHeaderViewModel>().ToList() ?? [];
        if (headers.Count > 0)
        {
            _isSyncingSelection = true;
            try
            {
                foreach (var header in headers)
                {
                    list.SelectedItems!.Remove(header);
                }
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }

        viewModel.SetSelectedCards(list.SelectedItems?.OfType<RepoCardViewModel>());
    }

    /// <summary>
    ///     Brings the list's selection up to date with the view model's: the view model drops the cards it
    ///     no longer shows from the selection, and the list deselects the rows it moved (after a drop, for
    ///     one), so the cards that are still selected are selected again.
    /// </summary>
    /// <param name="viewModel">The window's view model.</param>
    private void SyncListSelectionFromViewModel(MainWindowViewModel viewModel)
    {
        if (_isSyncingSelection || this.FindControl<ListBox>("RepoListBox") is not { } list)
        {
            return;
        }

        var wanted = viewModel.SelectedCards;
        var current = list.SelectedItems!.Cast<object>().ToList();
        if (current.Count == wanted.Count && wanted.All(card => current.Contains(card)))
        {
            return;
        }

        _isSyncingSelection = true;
        list.Selection.BeginBatchUpdate();
        try
        {
            list.Selection.Clear();
            foreach (var card in wanted)
            {
                var index = list.Items.IndexOf(card);
                if (index >= 0)
                {
                    list.Selection.Select(index);
                }
            }
        }
        finally
        {
            list.Selection.EndBatchUpdate();
            _isSyncingSelection = false;
        }
    }

    /// <summary>
    ///     Shows the <see cref="ConfirmationWindow"/> asking the user to confirm removing the given cards
    ///     from AgentControl's recent-repos list - for one card the question of
    ///     <see cref="ConfirmAndRemoveCard"/>, for several a question that says how many - and removes them
    ///     (in one change) only if the user confirms. Never removes anything from disk, which the message
    ///     says.
    /// </summary>
    /// <param name="viewModel">The window's view model.</param>
    /// <param name="cards">The cards the user asked to remove.</param>
    private async Task ConfirmAndRemoveCards(MainWindowViewModel viewModel, IReadOnlyList<RepoCardViewModel> cards)
    {
        if (cards.Count == 1)
        {
            await ConfirmAndRemoveOneCard(cards[0]);
            return;
        }

        var confirmed = await new ConfirmationWindow(
                $"Remove {cards.Count} repos from AgentControl's recent-repos list?\n\n" +
                "This only removes the repos from AgentControl's list - it does not delete or " +
                "otherwise modify anything on disk.")
            .ShowDialog<bool>(this);

        if (confirmed)
        {
            // Applied at once and saved in the background; a failed save is reported once through
            // SettingsSaveFailed
            viewModel.RemoveRepos(cards);
        }
    }

    /// <summary>
    ///     The data format of a dragged repo card: one string item for each dragged card, its repo path (the
    ///     card the drag started on first).
    /// </summary>
    internal static readonly DataFormat<string> RepoCardDataFormat =
        DataFormat.CreateStringApplicationFormat("DemaConsulting.AgentControl.RepoCard");

    /// <summary>
    ///     The data format of a dragged group header: one string item, the group's name. Separate from
    ///     <see cref="RepoCardDataFormat"/>, so a dragged group is never taken for a card (or the other
    ///     way round) and neither is ever taken for folders from the file manager.
    /// </summary>
    internal static readonly DataFormat<string> RepoGroupDataFormat =
        DataFormat.CreateStringApplicationFormat("DemaConsulting.AgentControl.RepoGroup");

    /// <summary>
    ///     How far (in device-independent pixels) the pointer must move with the button held on a card
    ///     or a group header before a drag starts, so a plain click or a slightly unsteady one never
    ///     drags.
    /// </summary>
    private const double DragThreshold = 6;

    /// <summary>
    ///     How close to the top or bottom edge of the list (in device-independent pixels) a drag must be
    ///     for the list to scroll; the zone also extends beyond the edge (over the app bar above the
    ///     list, the margins below it).
    /// </summary>
    internal const double AutoScrollEdge = 32;

    /// <summary>
    ///     How far past the inner edge of the scroll zone (in device-independent pixels, measured from
    ///     the zone's inner edge) the pointer must be for the scroll to reach its top speed: the edge
    ///     zone plus this much beyond the list's edge.
    /// </summary>
    internal const double AutoScrollOvershoot = 96;

    /// <summary>
    ///     How far the list scrolls per tick (in device-independent pixels) when the pointer has just
    ///     entered the scroll zone: slow enough to place a card precisely.
    /// </summary>
    internal const double AutoScrollMinStep = 4;

    /// <summary>
    ///     How far the list scrolls per tick at most (in device-independent pixels): with
    ///     <see cref="AutoScrollInterval"/>, 900 DIP a second - fast enough to cross a long list,
    ///     slow enough to keep track of it.
    /// </summary>
    internal const double AutoScrollMaxStep = 36;

    /// <summary>
    ///     How often the list scrolls while a drag is held in the scroll zone (25 ticks a second, so the
    ///     movement looks smooth without redrawing the list needlessly often).
    /// </summary>
    private static readonly TimeSpan AutoScrollInterval = TimeSpan.FromMilliseconds(40);

    /// <summary>
    ///     The smallest scroll (in device-independent pixels) worth making; anything less means the
    ///     pointer is outside the scroll zones or the list is already at its top or bottom.
    /// </summary>
    private const double MinimumScrollDistance = 0.5;

    /// <summary>
    ///     Scrolls the list while a drag is held in the scroll zone, even when the pointer does not
    ///     move (the drag only reports the pointer when it moves).
    /// </summary>
    private readonly DispatcherTimer _autoScrollTimer;

    /// <summary>
    ///     What is being dragged and where the pointer was last reported (relative to this window),
    ///     for the auto-scroll ticks; <see langword="null"/> when no auto-scroll is running.
    /// </summary>
    private (IDataTransfer Data, Point PointInWindow)? _autoScrollPointer;

    /// <summary>
    ///     Gets a value indicating whether the list is auto-scrolling for a drag now (its timer runs), so
    ///     tests can tell when it starts and stops.
    /// </summary>
    internal bool IsAutoScrolling => _autoScrollTimer.IsEnabled;

    /// <summary>
    ///     Starts the operating system's drag-and-drop for a card or a group header;
    ///     <see langword="null"/> uses <see cref="DragDrop.DoDragDropAsync"/>.
    /// </summary>
    /// <remarks>
    ///     A test seam, like <see cref="RepoFolderPicker"/>: the headless platform has no drag source, so
    ///     tests supply a starter that records the data being dragged and complete the drag by raising
    ///     the drop events on the list themselves. The delegate receives the pointer press that
    ///     started the drag, the data to drag and the allowed effects, and returns the effect the
    ///     drop had.
    /// </remarks>
    internal Func<PointerPressedEventArgs, IDataTransfer, DragDropEffects, Task<DragDropEffects>>? DragStarter { get; set; }

    /// <summary>
    ///     The card or group header the pointer was pressed on (and the press that did it) while it may
    ///     still turn into a drag; <see langword="null"/> when no press is pending.
    /// </summary>
    private (object Item, ListBoxItem Container, PointerPressedEventArgs Press, Point Start)? _dragCandidate;

    /// <summary>
    ///     The list row that currently shows drop feedback and the class that does it, so the feedback
    ///     can be cleared again.
    /// </summary>
    private (ListBoxItem Container, string Class)? _dropFeedback;

    /// <summary>
    ///     Connects the repo list's drag source and drop target handlers, and the list area's drop
    ///     target for folders from the file manager.
    /// </summary>
    /// <remarks>
    ///     The press is observed on the way down (and also when something handled it), so a card or a
    ///     group header that is pressed on can become a drag whatever its list row does with the press.
    ///     Every business rule - what may be dropped where, and what a drop does - is the view model's
    ///     (<see cref="MainWindowViewModel.CanDrop"/>/<see cref="MainWindowViewModel.DropCard"/> for
    ///     cards, <see cref="MainWindowViewModel.CanDropGroup"/>/<see cref="MainWindowViewModel.DropGroup"/>
    ///     for groups, <see cref="MainWindowViewModel.EnqueueAddReposAsync"/> for folders); this class only
    ///     translates pointer positions into targets and shows the feedback. A drag is told apart by its
    ///     data, always in the same order - a card, then a group, then files - so folders from the file
    ///     manager are never taken for a card move, and a card or a group is never taken for folders.
    ///     The list handles every drag over it; the list area's handlers only see the drags the list did
    ///     not handle (over the empty-state panel or the margins). The app bar refuses every drop but
    ///     keeps the list scrolling up while a drag is held over it.
    /// </remarks>
    private void WireRepoListDragAndDrop()
    {
        var list = this.FindControl<ListBox>("RepoListBox");
        if (list is null)
        {
            return;
        }

        list.AddHandler(PointerPressedEvent, RepoList_PointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        list.AddHandler(PointerMovedEvent, RepoList_PointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        list.AddHandler(PointerReleasedEvent, RepoList_PointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        list.AddHandler(PointerCaptureLostEvent, RepoList_PointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        DragDrop.AddDragEnterHandler(list, RepoList_DragOver);
        DragDrop.AddDragOverHandler(list, RepoList_DragOver);
        DragDrop.AddDragLeaveHandler(list, RepoList_DragLeave);
        DragDrop.AddDropHandler(list, RepoList_Drop);

        var area = this.FindControl<Grid>("RepoListArea");
        if (area is not null)
        {
            DragDrop.AddDragEnterHandler(area, RepoListArea_DragOver);
            DragDrop.AddDragOverHandler(area, RepoListArea_DragOver);
            DragDrop.AddDragLeaveHandler(area, RepoListArea_DragLeave);
            DragDrop.AddDropHandler(area, RepoListArea_Drop);
        }

        // Above the list: holding a drag over the app bar scrolls the list up; nothing drops there
        var appBar = this.FindControl<Border>("AppBar");
        if (appBar is not null)
        {
            DragDrop.AddDragEnterHandler(appBar, AppBar_DragOver);
            DragDrop.AddDragOverHandler(appBar, AppBar_DragOver);
            DragDrop.AddDragLeaveHandler(appBar, AppBar_DragLeave);
            DragDrop.AddDropHandler(appBar, AppBar_Drop);
        }
    }

    /// <summary>
    ///     Remembers the card, or the user's group header, the left button was pressed on as a possible
    ///     drag, unless reordering is off or the press is on one of the row's own controls (for a card:
    ///     a button, the star, a menu or a text box; for a header: its "..." menu button).
    /// </summary>
    /// <param name="sender">The list.</param>
    /// <param name="e">The press.</param>
    private void RepoList_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragCandidate = null;
        if (sender is not ListBox list ||
            DataContext is not MainWindowViewModel { IsReorderEnabled: true } ||
            !e.GetCurrentPoint(list).Properties.IsLeftButtonPressed ||
            e.Source is not Visual source)
        {
            return;
        }

        var ancestors = source.GetSelfAndVisualAncestors().ToList();
        var container = ancestors.OfType<ListBoxItem>().FirstOrDefault();
        var inRow = ancestors.TakeWhile(visual => visual is not ListBoxItem).ToList();
        // A press on a card's button, star, menu or text box is that control's, not the card's
        if (container is { DataContext: RepoCardViewModel card } &&
            !inRow.Any(visual => visual is Button or TextBox or MenuItem or ScrollBar))
        {
            _dragCandidate = (card, container, e, e.GetPosition(list));
        }

        // The whole header of a user's group drags, except its "..." menu button; Favorites and
        // Ungrouped never do
        else if (container is { DataContext: RepoGroupHeaderViewModel { IsUserGroup: true } header } &&
                 !inRow.Any(visual => visual is Button { Name: "GroupHeaderMenuButton" } or MenuItem or TextBox or ScrollBar))
        {
            _dragCandidate = (header, container, e, e.GetPosition(list));
        }
    }

    /// <summary>
    ///     Starts the drag once the pointer has moved far enough from where it was pressed on a card or
    ///     a group header.
    /// </summary>
    /// <param name="sender">The list.</param>
    /// <param name="e">The move.</param>
    private void RepoList_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragCandidate is not { } candidate || sender is not ListBox list)
        {
            return;
        }

        if (!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed ||
            DataContext is not MainWindowViewModel { IsReorderEnabled: true })
        {
            _dragCandidate = null;
            return;
        }

        var delta = e.GetPosition(list) - candidate.Start;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
        {
            return;
        }

        _dragCandidate = null;
        _ = candidate.Item switch
        {
            RepoCardViewModel card => StartDragAsync(card, candidate.Container, candidate.Press),
            RepoGroupHeaderViewModel header => StartGroupDragAsync(header, candidate.Container, candidate.Press),
            _ => Task.CompletedTask
        };
    }

    /// <summary>
    ///     Forgets a pending press when the button is released (a plain click) or the pointer capture is lost.
    /// </summary>
    /// <param name="sender">The list (unused).</param>
    /// <param name="e">The event (unused).</param>
    private void RepoList_PointerReleased(object? sender, RoutedEventArgs e) => _dragCandidate = null;

    /// <summary>
    ///     Runs a drag of a card: dims the card, hands the card's path to the drag source and, when the
    ///     drag ends however it ends, removes the dimming and any drop feedback. When the card is one of
    ///     several selected cards the drag carries all of them (one item each, the grabbed card first) and
    ///     dims them all.
    /// </summary>
    /// <param name="card">The card the drag started on.</param>
    /// <param name="container">The card's list row.</param>
    /// <param name="press">The press that started the drag.</param>
    /// <returns>A task that completes when the drag has ended.</returns>
    private async Task StartDragAsync(RepoCardViewModel card, ListBoxItem container, PointerPressedEventArgs press)
    {
        // The selection stays as it is: the press that began this drag must not reduce it on release
        var list = this.FindControl<ListBox>("RepoListBox");
        (list as RepoListBox)?.CancelDeferredSelection();
        var cards = DataContext is MainWindowViewModel viewModel ? viewModel.GetDragCards(card) : [card];
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(RepoCardDataFormat, card.RepoPath));
        foreach (var other in cards.Where(other => !ReferenceEquals(other, card)))
        {
            transfer.Add(DataTransferItem.Create(RepoCardDataFormat, other.RepoPath));
        }

        var dimmed = cards.Select(other => ReferenceEquals(other, card) ? container : list?.ContainerFromItem(other))
            .OfType<Control>().ToList();
        foreach (var row in dimmed)
        {
            row.Classes.Add("dragging");
        }

        try
        {
            var starter = DragStarter ?? DragDrop.DoDragDropAsync;
            await starter(press, transfer, DragDropEffects.Move);
        }
        finally
        {
            foreach (var row in dimmed)
            {
                row.Classes.Remove("dragging");
            }

            ClearDropFeedback();
        }
    }

    /// <summary>
    ///     Runs a drag of a group header: dims the header, hands the group's name to the drag source
    ///     and, when the drag ends however it ends, removes the dimming and any drop feedback.
    /// </summary>
    /// <remarks>
    ///     The header's toggle button took the pointer capture when it was pressed; the capture is
    ///     released first, so the button loses its pressed state and the release at the end of the drag
    ///     is not taken as a click that would collapse or expand the section. A collapsed group stays
    ///     collapsed.
    /// </remarks>
    /// <param name="header">The dragged group's header.</param>
    /// <param name="container">The header's list row.</param>
    /// <param name="press">The press that started the drag.</param>
    /// <returns>A task that completes when the drag has ended.</returns>
    private async Task StartGroupDragAsync(
        RepoGroupHeaderViewModel header, ListBoxItem container, PointerPressedEventArgs press)
    {
        press.Pointer.Capture(null);
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(RepoGroupDataFormat, header.Name));
        container.Classes.Add("dragging");
        try
        {
            var starter = DragStarter ?? DragDrop.DoDragDropAsync;
            await starter(press, transfer, DragDropEffects.Move);
        }
        finally
        {
            container.Classes.Remove("dragging");
            ClearDropFeedback();
        }
    }

    /// <summary>
    ///     Gets the cards that are being dragged over the list, from the data of the drag.
    /// </summary>
    /// <param name="data">The data of the drag.</param>
    /// <param name="viewModel">The window's view model.</param>
    /// <param name="cards">The dragged cards, the grabbed one first, when this returns
    ///     <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the drag carries at least one repo card that is in the list;
    ///     a path that no card has any more is left out.</returns>
    private bool TryGetDraggedCards(
        IDataTransfer data,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MainWindowViewModel? viewModel,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IReadOnlyList<RepoCardViewModel>? cards)
    {
        viewModel = DataContext as MainWindowViewModel;
        cards = null;
        if (viewModel is null)
        {
            return false;
        }

        var found = new List<RepoCardViewModel>();
        foreach (var path in data.TryGetValues(RepoCardDataFormat) ?? [])
        {
            var card = viewModel.RepoCards.FirstOrDefault(other => MainWindowViewModel.IsSameRepoPath(other.RepoPath, path));
            if (card is not null && !found.Contains(card))
            {
                found.Add(card);
            }
        }

        cards = found;
        return found.Count > 0;
    }

    /// <summary>
    ///     Gets whether the dragged cards may be dropped at a place: one card by the rules for a single
    ///     card, several by the rules for a selection.
    /// </summary>
    /// <param name="viewModel">The window's view model.</param>
    /// <param name="cards">The dragged cards.</param>
    /// <param name="target">The place.</param>
    /// <returns><see langword="true"/> when the drop would be accepted.</returns>
    private static bool CanDropCardsAt(MainWindowViewModel viewModel, IReadOnlyList<RepoCardViewModel> cards, RepoDropTarget target) =>
        cards.Count == 1 ? viewModel.CanDrop(cards[0], target) : viewModel.CanDropCards(cards, target);

    /// <summary>
    ///     Gets the group whose header is being dragged over the list, from the data of the drag.
    /// </summary>
    /// <param name="data">The data of the drag.</param>
    /// <param name="viewModel">The window's view model.</param>
    /// <param name="groupName">The dragged group's name when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the drag carries a group name and the window has its view
    ///     model.</returns>
    private bool TryGetDraggedGroup(
        IDataTransfer data,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MainWindowViewModel? viewModel,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? groupName)
    {
        viewModel = DataContext as MainWindowViewModel;
        groupName = viewModel is null ? null : data.TryGetValue(RepoGroupDataFormat);
        return viewModel is not null && !string.IsNullOrWhiteSpace(groupName);
    }

    /// <summary>
    ///     Decides whether a card or a group header is dropped before or after the row it is over, from
    ///     the pointer's height within that row.
    /// </summary>
    /// <param name="y">The pointer's vertical position within the row.</param>
    /// <param name="height">The row's height.</param>
    /// <returns><see langword="true"/> for the upper half (insert before),
    ///     <see langword="false"/> for the lower half (insert after).</returns>
    internal static bool IsUpperHalf(double y, double height) => y < height / 2;

    /// <summary>
    ///     Gets the list row at a point of this window (where the pointer of a drag is).
    /// </summary>
    /// <remarks>
    ///     Found from the realized rows' layout bounds rather than a renderer hit test, so it is right
    ///     straight after an auto-scroll moved the rows (before the next frame is drawn).
    /// </remarks>
    /// <param name="pointInWindow">The point, relative to this window.</param>
    /// <returns>The row, or <see langword="null"/> when the point is not over one (or the list is
    ///     hidden).</returns>
    private ListBoxItem? GetRowAt(Point pointInWindow)
    {
        var list = this.FindControl<ListBox>("RepoListBox");
        if (list is null || !list.IsVisible ||
            this.TranslatePoint(pointInWindow, list) is not { } point ||
            !new Rect(list.Bounds.Size).Contains(point))
        {
            return null;
        }

        return list.GetRealizedContainers()
            .OfType<ListBoxItem>()
            .FirstOrDefault(row => row.IsVisible &&
                                   this.TranslatePoint(pointInWindow, row) is { } inRow &&
                                   new Rect(row.Bounds.Size).Contains(inRow));
    }

    /// <summary>
    ///     Gets the height of a point of this window within a control, for the upper/lower half rule.
    /// </summary>
    /// <param name="pointInWindow">The point, relative to this window.</param>
    /// <param name="control">The control.</param>
    /// <returns>The point's vertical position relative to the control.</returns>
    private double GetYWithin(Point pointInWindow, Visual control) =>
        (this.TranslatePoint(pointInWindow, control) ?? default).Y;

    /// <summary>
    ///     Works out where a drop at the pointer's position would put a card.
    /// </summary>
    /// <param name="pointInWindow">The pointer's position, relative to this window.</param>
    /// <returns>The place and the row it was worked out from, or <see langword="null"/> when the pointer
    ///     is not over a card or a section header.</returns>
    private (RepoDropTarget Target, ListBoxItem Container)? GetDropTarget(Point pointInWindow)
    {
        var container = GetRowAt(pointInWindow);
        switch (container?.DataContext)
        {
            case RepoGroupHeaderViewModel header:
                return (RepoDropTarget.ForHeader(header), container);

            case RepoCardViewModel anchor:
                var section = MainWindowViewModel.GetSectionOf(anchor);
                var y = GetYWithin(pointInWindow, container);
                return (RepoDropTarget.ForCard(
                    anchor, IsUpperHalf(y, container.Bounds.Height), section.Kind, section.Name), container);

            default:
                return null;
        }
    }

    /// <summary>
    ///     Works out where a drop at the pointer's position would put a dragged group: next to the user's
    ///     group whose header the pointer is over (before it over the upper third, after it over the
    ///     lower third) or inside it (over the middle third).
    /// </summary>
    /// <param name="pointInWindow">The pointer's position, relative to this window.</param>
    /// <returns>The place and the header's row, or <see langword="null"/> when the pointer is not over
    ///     the header of one of the user's groups (a card, Favorites, Ungrouped or empty space).</returns>
    private (GroupDropTarget Target, ListBoxItem Container)? GetGroupDropTarget(Point pointInWindow)
    {
        var container = GetRowAt(pointInWindow);
        if (container is not { DataContext: RepoGroupHeaderViewModel { IsUserGroup: true } header })
        {
            return null;
        }

        var y = GetYWithin(pointInWindow, container);
        var (insertBefore, inside) = GetGroupDropZone(y, container.Bounds.Height);
        return (new GroupDropTarget(header.Name, insertBefore, inside), container);
    }

    /// <summary>
    ///     Decides whether a group header is dropped before, after or inside the group header it is
    ///     over, from the pointer's height within that header's row.
    /// </summary>
    /// <remarks>
    ///     The row is split in thirds: the upper third is "before" and the lower third "after" (the two
    ///     places the halves were before groups nested, so "upper" and "lower" keep their meaning), and
    ///     the middle third puts the dragged group inside the header's group.
    /// </remarks>
    /// <param name="y">The pointer's vertical position within the row.</param>
    /// <param name="height">The row's height.</param>
    /// <returns><c>InsertBefore</c> for the upper third and <c>Inside</c> for the middle third; both
    ///     <see langword="false"/> for the lower third (insert after).</returns>
    internal static (bool InsertBefore, bool Inside) GetGroupDropZone(double y, double height)
    {
        if (y < height / 3)
        {
            return (true, false);
        }

        return y < height * 2 / 3 ? (false, true) : (false, false);
    }

    /// <summary>
    ///     Answers a drag over the list: a dragged card, a dragged group header, or folders from the file
    ///     manager, told apart in that order by the data of the drag.
    /// </summary>
    /// <param name="sender">The list.</param>
    /// <param name="e">The drag event.</param>
    private void RepoList_DragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(RepoCardDataFormat))
        {
            CardDragOver(e);
        }
        else if (e.DataTransfer.Contains(RepoGroupDataFormat))
        {
            GroupDragOver(e);
        }
        else if (e.DataTransfer.Contains(DataFormat.File))
        {
            ExternalDragOver(e, overList: true);
        }
    }

    /// <summary>
    ///     Answers a card dragged over the list: says whether a drop here is possible, shows where the
    ///     card would land and keeps the list scrolling while the pointer is near its top or bottom edge.
    /// </summary>
    /// <param name="e">The drag event.</param>
    private void CardDragOver(DragEventArgs e)
    {
        if (!TryGetDraggedCards(e.DataTransfer, out var viewModel, out var cards))
        {
            return;
        }

        e.Handled = true;
        var point = e.GetPosition(this);
        e.DragEffects = ShowCardDropFeedback(viewModel, cards, point) ? DragDropEffects.Move : DragDropEffects.None;
        UpdateAutoScroll(e.DataTransfer, point);
    }

    /// <summary>
    ///     Shows where dragged cards would land at a point (the insertion line or the header tint), or
    ///     hides the feedback when they cannot be dropped there.
    /// </summary>
    /// <param name="viewModel">The window's view model.</param>
    /// <param name="cards">The dragged cards.</param>
    /// <param name="pointInWindow">The pointer's position, relative to this window.</param>
    /// <returns><see langword="true"/> when the cards can be dropped there.</returns>
    private bool ShowCardDropFeedback(MainWindowViewModel viewModel, IReadOnlyList<RepoCardViewModel> cards, Point pointInWindow)
    {
        var found = GetDropTarget(pointInWindow);
        var allowed = found is { } target && CanDropCardsAt(viewModel, cards, target.Target);

        // Nothing is shown next to a dragged card itself: dropping it there changes nothing
        if (allowed && found is { } shown && !cards.Any(card => ReferenceEquals(shown.Target.Anchor, card)))
        {
            ShowDropFeedback(shown.Container, shown.Target);
        }
        else
        {
            HideDropFeedback();
        }

        return allowed;
    }

    /// <summary>
    ///     Answers a group header dragged over the list: says whether a drop here is possible (only on
    ///     another of the user's group headers), shows the line above or below that header and keeps the
    ///     list scrolling near its edges.
    /// </summary>
    /// <param name="e">The drag event.</param>
    private void GroupDragOver(DragEventArgs e)
    {
        if (!TryGetDraggedGroup(e.DataTransfer, out var viewModel, out var groupName))
        {
            return;
        }

        e.Handled = true;
        var point = e.GetPosition(this);
        e.DragEffects = ShowGroupDropFeedback(viewModel, groupName, point) ? DragDropEffects.Move : DragDropEffects.None;
        UpdateAutoScroll(e.DataTransfer, point);
    }

    /// <summary>
    ///     Shows where a dragged group header would land at a point (the line above or below another
    ///     group's header, or the tint of the header it would go inside), or hides the feedback when it
    ///     cannot be dropped there.
    /// </summary>
    /// <param name="viewModel">The window's view model.</param>
    /// <param name="groupName">The dragged group's name.</param>
    /// <param name="pointInWindow">The pointer's position, relative to this window.</param>
    /// <returns><see langword="true"/> when the group can be dropped there.</returns>
    private bool ShowGroupDropFeedback(MainWindowViewModel viewModel, string groupName, Point pointInWindow)
    {
        var found = GetGroupDropTarget(pointInWindow);
        var allowed = found is { } target && viewModel.CanDropGroup(groupName, target.Target);
        if (allowed && found is { } shown)
        {
            var feedbackClass = shown.Target.InsertBefore ? "dropAbove" : "dropBelow";
            ShowDropFeedback(shown.Container, shown.Target.Inside ? "dropHeader" : feedbackClass);
        }
        else
        {
            HideDropFeedback();
        }

        return allowed;
    }

    /// <summary>
    ///     Removes the drop feedback when the drag leaves the list, and stops the auto-scroll when the
    ///     pointer really left it (not when it only moved from one row to the next).
    /// </summary>
    /// <param name="sender">The list.</param>
    /// <param name="e">The drag event.</param>
    private void RepoList_DragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Visual list && IsInside(list, e))
        {
            HideDropFeedback();
        }
        else
        {
            ClearDropFeedback();
        }
    }

    /// <summary>
    ///     Drops what is dragged where the pointer is: a card or a group header through the view model's
    ///     reordering, or folders from the file manager through its add.
    /// </summary>
    /// <param name="sender">The list.</param>
    /// <param name="e">The drag event.</param>
    private void RepoList_Drop(object? sender, DragEventArgs e)
    {
        ClearDropFeedback();
        if (e.DataTransfer.Contains(RepoCardDataFormat))
        {
            CardDrop(e);
        }
        else if (e.DataTransfer.Contains(RepoGroupDataFormat))
        {
            GroupDrop(e);
        }
        else if (e.DataTransfer.Contains(DataFormat.File))
        {
            ExternalDrop(e, overList: true);
        }
    }

    /// <summary>
    ///     Drops the dragged card, or cards, where the pointer is, through the view model.
    /// </summary>
    /// <param name="e">The drag event.</param>
    private void CardDrop(DragEventArgs e)
    {
        if (!TryGetDraggedCards(e.DataTransfer, out var viewModel, out var cards))
        {
            return;
        }

        e.Handled = true;
        if (GetDropTarget(e.GetPosition(this)) is { } found &&
            (cards.Count == 1 ? viewModel.DropCard(cards[0], found.Target) : viewModel.DropCards(cards, found.Target)))
        {
            e.DragEffects = DragDropEffects.Move;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    /// <summary>
    ///     Drops the dragged group header before or after the group header the pointer is over, through
    ///     the view model.
    /// </summary>
    /// <param name="e">The drag event.</param>
    private void GroupDrop(DragEventArgs e)
    {
        if (!TryGetDraggedGroup(e.DataTransfer, out var viewModel, out var groupName))
        {
            return;
        }

        e.Handled = true;
        e.DragEffects = GetGroupDropTarget(e.GetPosition(this)) is { } found && viewModel.DropGroup(groupName, found.Target)
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    /// <summary>
    ///     Answers a drag over the list area that the list did not handle (the empty-state panel or the
    ///     margins): folders from the file manager would be added ungrouped; anything else cannot be
    ///     dropped there, but a card or a group header held below the list keeps it scrolling down.
    /// </summary>
    /// <param name="sender">The list area.</param>
    /// <param name="e">The drag event.</param>
    private void RepoListArea_DragOver(object? sender, DragEventArgs e)
    {
        if (IsExternalFileDrag(e.DataTransfer))
        {
            ExternalDragOver(e, overList: false);
        }
        else
        {
            e.Handled = true;
            e.DragEffects = DragDropEffects.None;
            HideDropFeedback();
            UpdateAutoScroll(e.DataTransfer, e.GetPosition(this));
        }
    }

    /// <summary>
    ///     Removes every drop feedback when the drag leaves the list area, and stops the auto-scroll
    ///     when the pointer really left it.
    /// </summary>
    /// <param name="sender">The list area.</param>
    /// <param name="e">The drag event.</param>
    private void RepoListArea_DragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Visual area && IsInside(area, e))
        {
            ShowExternalDropOverlay(false);
            HideDropFeedback();
        }
        else
        {
            ClearExternalDropFeedback();
        }
    }

    /// <summary>
    ///     Adds the folders dropped on the list area outside the list (the empty-state panel or the
    ///     margins), ungrouped.
    /// </summary>
    /// <param name="sender">The list area.</param>
    /// <param name="e">The drag event.</param>
    private void RepoListArea_Drop(object? sender, DragEventArgs e)
    {
        ClearExternalDropFeedback();
        if (IsExternalFileDrag(e.DataTransfer))
        {
            ExternalDrop(e, overList: false);
        }
    }

    /// <summary>
    ///     Answers a drag held over the app bar, above the list: nothing can be dropped there, but the
    ///     list keeps scrolling up, faster the higher the pointer is.
    /// </summary>
    /// <param name="sender">The app bar (unused).</param>
    /// <param name="e">The drag event.</param>
    private void AppBar_DragOver(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        e.DragEffects = DragDropEffects.None;
        ShowExternalDropOverlay(false);
        HideDropFeedback();
        UpdateAutoScroll(e.DataTransfer, e.GetPosition(this));
    }

    /// <summary>
    ///     Stops the auto-scroll when a drag leaves the app bar (unless it only moved between the app
    ///     bar's own controls).
    /// </summary>
    /// <param name="sender">The app bar.</param>
    /// <param name="e">The drag event.</param>
    private void AppBar_DragLeave(object? sender, DragEventArgs e)
    {
        if (sender is not Visual appBar || !IsInside(appBar, e))
        {
            ClearDropFeedback();
        }
    }

    /// <summary>
    ///     Refuses a drop on the app bar and stops the auto-scroll.
    /// </summary>
    /// <param name="sender">The app bar (unused).</param>
    /// <param name="e">The drag event.</param>
    private void AppBar_Drop(object? sender, DragEventArgs e)
    {
        ClearExternalDropFeedback();
        e.Handled = true;
        e.DragEffects = DragDropEffects.None;
    }

    /// <summary>
    ///     Gets whether the pointer of a drag is still within a control's bounds: a drag-leave raised
    ///     while it is, only moved from one of the control's children to another.
    /// </summary>
    /// <param name="control">The control.</param>
    /// <param name="e">The drag event.</param>
    /// <returns><see langword="true"/> when the pointer is inside the control.</returns>
    private static bool IsInside(Visual control, DragEventArgs e) =>
        new Rect(control.Bounds.Size).Contains(e.GetPosition(control));

    /// <summary>
    ///     Gets whether a drag carries files or folders from outside the application (and not a card or
    ///     a group header of this window).
    /// </summary>
    /// <param name="data">The data of the drag.</param>
    /// <returns><see langword="true"/> for files or folders from the file manager.</returns>
    private static bool IsExternalFileDrag(IDataTransfer data) =>
        !data.Contains(RepoCardDataFormat) &&
        !data.Contains(RepoGroupDataFormat) &&
        data.Contains(DataFormat.File);

    /// <summary>
    ///     Works out which group folders dropped at the pointer's position would join.
    /// </summary>
    /// <remarks>
    ///     Over the header of one of the user's groups, or over a card listed in such a group's section,
    ///     the folders join that group (and that header is the one to tint, when its row is realized).
    ///     Everywhere else - Favorites (a favorite is a per-repo mark, not a place to add to), Ungrouped,
    ///     the flat list and empty space - they are added ungrouped.
    /// </remarks>
    /// <param name="pointInWindow">The pointer's position, relative to this window.</param>
    /// <returns>The group (or <see langword="null"/> for none) and the row of its header to tint (or
    ///     <see langword="null"/>).</returns>
    private (string? GroupName, ListBoxItem? HeaderContainer) GetExternalDropGroup(Point pointInWindow)
    {
        var container = GetRowAt(pointInWindow);
        switch (container?.DataContext)
        {
            case RepoGroupHeaderViewModel { IsUserGroup: true } header:
                return (header.Name, container);

            case RepoCardViewModel { IsFavorite: false, GroupName: { } groupName }
                when DataContext is MainWindowViewModel viewModel:
                var groupHeader = viewModel.DisplayedItems
                    .OfType<RepoGroupHeaderViewModel>()
                    .FirstOrDefault(other => other.IsUserGroup && RepoGroupName.Comparer.Equals(other.Name, groupName));
                var list = this.FindControl<ListBox>("RepoListBox");
                var headerContainer = groupHeader is null ? null : list?.ContainerFromItem(groupHeader) as ListBoxItem;
                return (groupName, headerContainer);

            default:
                return (null, null);
        }
    }

    /// <summary>
    ///     Answers folders from the file manager dragged over the list or the list area: they can be
    ///     added (a copy - nothing is moved on disk) and the feedback shows where - the tinted header of
    ///     the group they would join, or the outlined list area when they would be added ungrouped. The
    ///     list keeps scrolling while the pointer is near its top or bottom edge.
    /// </summary>
    /// <param name="e">The drag event.</param>
    /// <param name="overList">Whether the drag is over the list (where the row under the pointer can
    ///     name a group) rather than elsewhere in the list area.</param>
    private void ExternalDragOver(DragEventArgs e, bool overList)
    {
        if (DataContext is not MainWindowViewModel)
        {
            return;
        }

        e.Handled = true;
        e.DragEffects &= DragDropEffects.Copy;
        if (e.DragEffects == DragDropEffects.None)
        {
            ClearExternalDropFeedback();
            return;
        }

        var point = e.GetPosition(this);
        ShowExternalDropFeedback(point, overList);
        UpdateAutoScroll(e.DataTransfer, point);
    }

    /// <summary>
    ///     Shows where dropped folders would go: the tinted header of the group they would join, or the
    ///     outlined list area when they would be added ungrouped.
    /// </summary>
    /// <param name="pointInWindow">The pointer's position, relative to this window.</param>
    /// <param name="overList">Whether the pointer is over the list (where the row under it can name a
    ///     group).</param>
    private void ShowExternalDropFeedback(Point pointInWindow, bool overList)
    {
        var (groupName, headerContainer) = overList ? GetExternalDropGroup(pointInWindow) : (null, null);
        if (groupName is null)
        {
            HideDropFeedback();
            ShowExternalDropOverlay(true);
        }
        else
        {
            ShowExternalDropOverlay(false);
            if (headerContainer is not null)
            {
                ShowDropFeedback(headerContainer, "dropHeader");
            }
            else
            {
                HideDropFeedback();
            }
        }
    }

    /// <summary>
    ///     Accepts folders dropped from the file manager - into the group they were dropped on, or
    ///     ungrouped - and adds them in the background.
    /// </summary>
    /// <remarks>
    ///     The drop is answered at once and the add is posted to the UI thread, so neither the add nor
    ///     any dialog runs inside the operating system's drag-and-drop loop. The add is queued after
    ///     any add that is still running (see <see cref="MainWindowViewModel.EnqueueAddReposAsync"/>):
    ///     its progress shows in the strip under the app bar and the window stays usable meanwhile.
    ///     Dropped files are not repositories and are reported as such.
    /// </remarks>
    /// <param name="e">The drag event.</param>
    /// <param name="overList">Whether the drop is on the list (where the row under the pointer can name
    ///     a group) rather than elsewhere in the list area.</param>
    private void ExternalDrop(DragEventArgs e, bool overList)
    {
        ClearExternalDropFeedback();
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        var paths = GetDroppedPaths(e);
        if ((e.DragEffects & DragDropEffects.Copy) == DragDropEffects.None || paths.Count == 0)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var groupName = overList ? GetExternalDropGroup(e.GetPosition(this)).GroupName : null;
        e.DragEffects = DragDropEffects.Copy;
        Dispatcher.UIThread.Post(() => _ = AddDroppedFoldersAsync(viewModel, paths, groupName));
    }

    /// <summary>
    ///     Adds dropped folders through the view model's queued background add and then tells the user,
    ///     in one message, what was not added and why (nothing after a cancel).
    /// </summary>
    /// <param name="viewModel">The window's view model.</param>
    /// <param name="paths">The dropped paths.</param>
    /// <param name="groupName">The group they were dropped on, or <see langword="null"/>.</param>
    /// <returns>A task that completes when the add has ended and its message (if any) was closed.</returns>
    private async Task AddDroppedFoldersAsync(MainWindowViewModel viewModel, IReadOnlyList<string> paths, string? groupName)
    {
        try
        {
            var result = await viewModel.EnqueueAddReposAsync(paths, groupName);
            if (!result.Canceled)
            {
                await ShowNoticeAsync(MainWindowViewModel.DescribeAddOutcomes(result.Outcomes, groupName));
            }
        }
        catch (Exception ex)
        {
            // Nobody awaits this posted task: an unexpected failure is logged rather than lost
            _logger.LogError(ex, "Adding the dropped folders failed");
        }
    }

    /// <summary>
    ///     Gets the local paths of the files and folders a drag carries.
    /// </summary>
    /// <param name="e">The drag event.</param>
    /// <returns>The paths, in the order the drag lists them; items without a local path are left
    ///     out.</returns>
    private static List<string> GetDroppedPaths(DragEventArgs e) =>
    [
        .. (e.DataTransfer.TryGetFiles() ?? [])
            .Select(item => item.TryGetLocalPath() ?? (item.Path is { IsAbsoluteUri: true, IsFile: true } uri ? uri.LocalPath : null))
            .OfType<string>()
    ];

    /// <summary>
    ///     Shows or hides the outline over the list area that says dropped folders would be added
    ///     ungrouped.
    /// </summary>
    /// <param name="show">Whether to show it.</param>
    private void ShowExternalDropOverlay(bool show) =>
        this.FindControl<Grid>("RepoListArea")?.Classes.Set("externalDrop", show);

    /// <summary>
    ///     Removes every feedback of a drag from the file manager (the area outline and a header tint)
    ///     and stops the auto-scroll.
    /// </summary>
    private void ClearExternalDropFeedback()
    {
        ShowExternalDropOverlay(false);
        ClearDropFeedback();
    }

    /// <summary>
    ///     Shows where a card would land: a line above or below the card the pointer is over, or a tint on
    ///     the header it is over.
    /// </summary>
    /// <param name="container">The list row the pointer is over.</param>
    /// <param name="target">The place.</param>
    private void ShowDropFeedback(ListBoxItem container, RepoDropTarget target)
    {
        var feedbackClass = "dropHeader";
        if (target.Anchor is not null)
        {
            feedbackClass = target.InsertBefore ? "dropAbove" : "dropBelow";
        }

        ShowDropFeedback(container, feedbackClass);
    }

    /// <summary>
    ///     Shows drop feedback on one list row: <c>dropAbove</c> or <c>dropBelow</c> (the insertion
    ///     line) or <c>dropHeader</c> (the header tint), removing any feedback shown elsewhere.
    /// </summary>
    /// <param name="container">The list row.</param>
    /// <param name="feedbackClass">The feedback class.</param>
    private void ShowDropFeedback(ListBoxItem container, string feedbackClass)
    {
        if (_dropFeedback is { } current && ReferenceEquals(current.Container, container) && current.Class == feedbackClass)
        {
            return;
        }

        HideDropFeedback();
        container.Classes.Add(feedbackClass);
        _dropFeedback = (container, feedbackClass);
    }

    /// <summary>
    ///     Removes the drop feedback, if any is shown, leaving the auto-scroll running (the drag goes on).
    /// </summary>
    private void HideDropFeedback()
    {
        if (_dropFeedback is { } current)
        {
            current.Container.Classes.Remove(current.Class);
            _dropFeedback = null;
        }
    }

    /// <summary>
    ///     Ends a drag's feedback: removes the drop feedback and stops the auto-scroll. Called when the
    ///     drag leaves, drops or ends.
    /// </summary>
    private void ClearDropFeedback()
    {
        HideDropFeedback();
        StopAutoScroll();
    }

    /// <summary>
    ///     Works out how far the list scrolls per auto-scroll tick for a pointer at a height relative to
    ///     the list's viewport: nothing in the middle, slowly just inside the top or bottom edge zone,
    ///     faster the deeper the pointer is into the zone and beyond the edge (over the app bar above the
    ///     list, the margins below it), up to <see cref="AutoScrollMaxStep"/>.
    /// </summary>
    /// <remarks>
    ///     The zone is <see cref="AutoScrollEdge"/> deep inside each edge (half the viewport when the
    ///     viewport is shorter than two zones, so the nearer edge always wins). The depth is measured
    ///     from the zone's inner boundary; the step grows linearly from <see cref="AutoScrollMinStep"/>
    ///     to <see cref="AutoScrollMaxStep"/>, which is reached <see cref="AutoScrollEdge"/> +
    ///     <see cref="AutoScrollOvershoot"/> deep. Pure.
    /// </remarks>
    /// <param name="pointerY">The pointer's height relative to the top of the list's viewport;
    ///     negative above it, beyond <paramref name="viewportHeight"/> below it.</param>
    /// <param name="viewportHeight">The viewport's height.</param>
    /// <returns>The step in device-independent pixels: negative to scroll up, positive to scroll down,
    ///     0 for no scroll (also for a non-positive or unknown height, or an unknown position).</returns>
    internal static double ComputeAutoScrollStep(double pointerY, double viewportHeight)
    {
        if (double.IsNaN(pointerY) || double.IsNaN(viewportHeight) || viewportHeight <= 0)
        {
            return 0;
        }

        // The zones never overlap: a short viewport splits in two halves
        var edge = Math.Min(AutoScrollEdge, viewportHeight / 2);
        double depth;
        double direction;
        if (pointerY < edge)
        {
            depth = edge - pointerY;
            direction = -1;
        }
        else if (pointerY > viewportHeight - edge)
        {
            depth = pointerY - (viewportHeight - edge);
            direction = 1;
        }
        else
        {
            return 0;
        }

        // Linear ramp with the depth, capped
        var ramp = Math.Clamp(depth / (AutoScrollEdge + AutoScrollOvershoot), 0, 1);
        return direction * (AutoScrollMinStep + ((AutoScrollMaxStep - AutoScrollMinStep) * ramp));
    }

    /// <summary>
    ///     Gets the scroll viewer of the repo list, or <see langword="null"/> when it is not there (the
    ///     list is hidden, or not laid out yet).
    /// </summary>
    /// <returns>The scroll viewer.</returns>
    private ScrollViewer? GetRepoListScroller()
    {
        var list = this.FindControl<ListBox>("RepoListBox");
        return list is { IsVisible: true } ? list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() : null;
    }

    /// <summary>
    ///     Computes the auto-scroll step for a pointer position of this window (see
    ///     <see cref="ComputeAutoScrollStep"/>).
    /// </summary>
    /// <param name="scroller">The list's scroll viewer.</param>
    /// <param name="pointInWindow">The pointer's position, relative to this window.</param>
    /// <returns>The step; 0 when there is nothing to scroll.</returns>
    private double GetAutoScrollStep(ScrollViewer scroller, Point pointInWindow)
    {
        if (this.TranslatePoint(pointInWindow, scroller) is not { } point)
        {
            return 0;
        }

        // Before the first layout the viewport is unknown; the scroller's own height stands in
        var height = scroller.Viewport.Height > 0 ? scroller.Viewport.Height : scroller.Bounds.Height;
        return ComputeAutoScrollStep(point.Y, height);
    }

    /// <summary>
    ///     Remembers where a drag's pointer is and starts the auto-scroll timer when the pointer is in a
    ///     scroll zone (or stops it when it left the zones). Called from every drag-over of the list, the
    ///     list area and the app bar.
    /// </summary>
    /// <remarks>
    ///     A running timer is not restarted, so frequent drag-over events never delay its ticks.
    /// </remarks>
    /// <param name="data">The data of the drag, so a tick can redraw the drop feedback.</param>
    /// <param name="pointInWindow">The pointer's position, relative to this window.</param>
    private void UpdateAutoScroll(IDataTransfer data, Point pointInWindow)
    {
        if (GetRepoListScroller() is not { } scroller || Math.Abs(GetAutoScrollStep(scroller, pointInWindow)) < MinimumScrollDistance)
        {
            StopAutoScroll();
            return;
        }

        _autoScrollPointer = (data, pointInWindow);
        if (!_autoScrollTimer.IsEnabled)
        {
            _autoScrollTimer.Start();
        }
    }

    /// <summary>
    ///     Stops the auto-scroll and forgets the pointer.
    /// </summary>
    private void StopAutoScroll()
    {
        _autoScrollTimer.Stop();
        _autoScrollPointer = null;
    }

    /// <summary>
    ///     One auto-scroll tick (the timer's handler): scrolls the list by the step for the last known
    ///     pointer position, then redraws the drop feedback for what is now under the pointer.
    /// </summary>
    /// <remarks>
    ///     The list moves under a pointer that may not move, so the row under it changes: the feedback
    ///     (the insertion line, the tint, the overlay) is worked out again after the scroll, exactly as a
    ///     drag-over would. The scroll stops at the top or the bottom of the list, and when the pointer
    ///     is no longer in a zone.
    /// </remarks>
    internal void AutoScrollTick()
    {
        if (_autoScrollPointer is not { } pointer || GetRepoListScroller() is not { } scroller)
        {
            StopAutoScroll();
            return;
        }

        // Scroll by the step, within the list's extent; nothing left to scroll ends the auto-scroll
        var step = GetAutoScrollStep(scroller, pointer.PointInWindow);
        var limit = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
        var offset = Math.Clamp(scroller.Offset.Y + step, 0, limit);
        if (Math.Abs(offset - scroller.Offset.Y) < MinimumScrollDistance)
        {
            StopAutoScroll();
            return;
        }

        scroller.Offset = new Vector(scroller.Offset.X, offset);
        scroller.UpdateLayout();

        // What is under the pointer changed: show the feedback for it
        UpdateDragFeedback(pointer.Data, pointer.PointInWindow);
    }

    /// <summary>
    ///     Redraws the drop feedback of a drag for a pointer position, as the drag-over of the element
    ///     under the pointer would.
    /// </summary>
    /// <param name="data">The data of the drag.</param>
    /// <param name="pointInWindow">The pointer's position, relative to this window.</param>
    private void UpdateDragFeedback(IDataTransfer data, Point pointInWindow)
    {
        var list = this.FindControl<ListBox>("RepoListBox");
        var overList = list is { IsVisible: true } &&
                       this.TranslatePoint(pointInWindow, list) is { } point &&
                       new Rect(list.Bounds.Size).Contains(point);
        if (!overList)
        {
            // Over the app bar or the margins nothing drops (folders in the area are added ungrouped)
            HideDropFeedback();
            var area = this.FindControl<Grid>("RepoListArea");
            var overArea = area is not null &&
                           this.TranslatePoint(pointInWindow, area) is { } areaPoint &&
                           new Rect(area.Bounds.Size).Contains(areaPoint);
            ShowExternalDropOverlay(overArea && IsExternalFileDrag(data));
            return;
        }

        if (TryGetDraggedCards(data, out var viewModel, out var cards))
        {
            ShowCardDropFeedback(viewModel, cards, pointInWindow);
        }
        else if (TryGetDraggedGroup(data, out viewModel, out var groupName))
        {
            ShowGroupDropFeedback(viewModel, groupName, pointInWindow);
        }
        else if (IsExternalFileDrag(data))
        {
            ShowExternalDropFeedback(pointInWindow, overList: true);
        }
    }

    /// <summary>
    ///     The card whose color palette is open, so a swatch click knows which card to color; set
    ///     while the pop-up is shown.
    /// </summary>
    private RepoCardViewModel? _paletteCard;

    /// <summary>
    ///     The open color palette pop-up, so a swatch click can close it; <see langword="null"/>
    ///     when none is open.
    /// </summary>
    private FlyoutBase? _paletteFlyout;

    /// <summary>
    ///     Opens the color palette pop-up of the card whose "Card color..." menu item was clicked,
    ///     anchored to that card's "..." button.
    /// </summary>
    /// <remarks>
    ///     The pop-up is shown after the menu has finished closing (posted to the UI thread), because
    ///     showing it while the menu's own light-dismiss is still pending would close it at once.
    /// </remarks>
    /// <param name="sender">The clicked "Card color..." <see cref="MenuItem"/>.</param>
    /// <param name="e">Routed event arguments (unused).</param>
    private void CardColorMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: RepoCardViewModel card })
        {
            return;
        }

        var button = FindControlOfItem<Button>(card, "MoreActionsButton");
        if (button is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            _paletteCard = card;
            _paletteFlyout = FlyoutBase.GetAttachedFlyout(button);
            FlyoutBase.ShowAttachedFlyout(button);
        });
    }

    /// <summary>
    ///     Applies the color chosen in a card's palette (a swatch, or "Automatic") to the card whose
    ///     palette is open, then closes the palette.
    /// </summary>
    /// <param name="sender">The card's <see cref="ColorPalettePanel"/> (unused).</param>
    /// <param name="e">The chosen color's name, or <see langword="null"/> for Automatic.</param>
    private void CardColorPalette_ColorChosen(object? sender, ColorChosenEventArgs e)
    {
        _paletteCard?.SetCardColor(e.ColorName);

        _paletteFlyout?.Hide();
        _paletteFlyout = null;
        _paletteCard = null;
    }

    /// <summary>
    ///     The group whose color palette is open, so a swatch click knows which group to color; set
    ///     while the pop-up is shown.
    /// </summary>
    private RepoGroupHeaderViewModel? _paletteHeader;

    /// <summary>
    ///     Opens the color palette pop-up of the group whose "Group color..." menu item was clicked,
    ///     anchored to that header's "..." button.
    /// </summary>
    /// <remarks>
    ///     Shown after the menu has finished closing (posted), for the same reason as
    ///     <see cref="CardColorMenuItem_Click"/>.
    /// </remarks>
    /// <param name="sender">The clicked "Group color..." <see cref="MenuItem"/>.</param>
    /// <param name="e">Routed event arguments (unused).</param>
    private void GroupColorMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: RepoGroupHeaderViewModel { IsUserGroup: true } header })
        {
            return;
        }

        var button = FindControlOfItem<Button>(header, "GroupHeaderMenuButton");
        if (button is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            _paletteHeader = header;
            _paletteFlyout = FlyoutBase.GetAttachedFlyout(button);
            FlyoutBase.ShowAttachedFlyout(button);
        });
    }

    /// <summary>
    ///     Applies the color chosen in a group's palette (a swatch, or "None") to the group whose
    ///     palette is open, through the view model, then closes the palette.
    /// </summary>
    /// <param name="sender">The group's <see cref="ColorPalettePanel"/> (unused).</param>
    /// <param name="e">The chosen color's name, or <see langword="null"/> for None.</param>
    private void GroupColorPalette_ColorChosen(object? sender, ColorChosenEventArgs e)
    {
        // Applied at once and saved in the background; a failed save is reported by the view model
        if (_paletteHeader is { } header && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetGroupColor(header.Name, e.ColorName);
        }

        _paletteFlyout?.Hide();
        _paletteFlyout = null;
        _paletteHeader = null;
    }

    /// <summary>
    ///     Finds a named control inside the list row of an item (a card or a section header).
    /// </summary>
    /// <typeparam name="T">The control type.</typeparam>
    /// <param name="item">The card or header whose row to search.</param>
    /// <param name="name">The control's <see cref="StyledElement.Name"/>.</param>
    /// <returns>The control, or <see langword="null"/> when the item is not shown (for example a
    ///     card in a collapsed group, or filtered out).</returns>
    private T? FindControlOfItem<T>(object item, string name)
        where T : Control
    {
        var list = this.FindControl<ListBox>("RepoListBox");
        return list?.ContainerFromItem(item)?
            .GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(control => control.Name == name);
    }

    /// <summary>
    ///     Shows the name prompt for a new group and, when a name is chosen, moves
    ///     <paramref name="card"/>'s repo into that group.
    /// </summary>
    /// <param name="card">The card that asked for a new group.</param>
    private async Task ShowNewGroupPrompt(RepoCardViewModel card)
    {
        var prompt = new GroupNameViewModel(
            "New group",
            $"Name of the new group for \"{card.RepoName}\":",
            "Create",
            card.GetExistingGroupNames());
        var name = await new GroupNameWindow(prompt).ShowDialog<string?>(this);
        if (name is not null)
        {
            card.MoveToGroup(name);
        }
    }

    /// <summary>
    ///     Shows the name prompt for a group created inside another group and, when a name is chosen,
    ///     creates it.
    /// </summary>
    /// <remarks>
    ///     Group names are unique across the whole tree, so the prompt refuses the name of every existing
    ///     group (an empty or nested one included) instead of letting the new group join it.
    /// </remarks>
    /// <param name="viewModel">The main view model that owns the groups.</param>
    /// <param name="parentName">The group the new group is created inside.</param>
    private async Task ShowNewSubgroupPrompt(MainWindowViewModel viewModel, string parentName)
    {
        var prompt = new GroupNameViewModel(
            "New subgroup",
            $"Name of the new group inside \"{parentName}\":",
            "Create",
            viewModel.GroupNames,
            requireNewName: true);
        var name = await new GroupNameWindow(prompt).ShowDialog<string?>(this);
        if (name is not null)
        {
            // The prompt allows only a usable, unused name; the group is created at once and saved in
            // the background
            viewModel.CreateSubgroup(parentName, name);
        }
    }

    /// <summary>
    ///     Shows the name prompt for renaming a group and, when a new name is chosen, renames it.
    /// </summary>
    /// <param name="viewModel">The main view model that owns the groups.</param>
    /// <param name="groupName">The group to rename.</param>
    private async Task ShowRenameGroupPrompt(MainWindowViewModel viewModel, string groupName)
    {
        var prompt = new GroupNameViewModel(
            "Rename group",
            $"New name for the group \"{groupName}\":",
            "Rename",
            viewModel.GroupNames,
            renaming: groupName);
        var name = await new GroupNameWindow(prompt).ShowDialog<string?>(this);
        if (name is not null)
        {
            // The prompt allows only a usable name; the rename is applied at once and saved in the
            // background
            viewModel.RenameGroup(groupName, name);
        }
    }

    /// <summary>
    ///     Asks the user to confirm deleting a group but keeping what is in it and, if confirmed, deletes
    ///     it (its repos and subgroups go to the group it was inside; at the top level the repos become
    ///     ungrouped and the subgroups become top-level groups). Nothing on disk or in any repository
    ///     changes, which the message says.
    /// </summary>
    /// <remarks>
    ///     The message names the group as it is stored, counts what is directly in the group and leaves
    ///     out the part of the sentence about repos or subgroups when the group has none, so it never
    ///     reads "0 repo(s)".
    /// </remarks>
    /// <param name="viewModel">The main view model that owns the groups.</param>
    /// <param name="groupName">The group to delete.</param>
    private async Task ConfirmAndRemoveGroup(MainWindowViewModel viewModel, string groupName)
    {
        var contents = viewModel.GetGroupContents(groupName);
        var parent = viewModel.GetGroupParent(contents.Name);
        var message = $"Delete the group \"{contents.Name}\"?\n\n";
        if (contents.IsEmpty)
        {
            message += "The group has no repos or subgroups.";
        }
        else
        {
            var sentences = new List<string>();
            if (contents.DirectRepoCount > 0)
            {
                var where = parent is null ? "ungrouped" : $"in the group \"{parent}\"";
                sentences.Add($"Its {contents.DirectRepoCount} repo(s) stay in AgentControl's list, {where}.");
            }

            if (contents.DirectSubgroupCount > 0)
            {
                var target = parent is null ? "to the top level" : $"into \"{parent}\"";
                var also = contents.DirectRepoCount > 0 ? " too" : string.Empty;
                sentences.Add($"Its {contents.DirectSubgroupCount} subgroup(s) move {target}{also}.");
            }

            sentences.Add("This only changes how AgentControl lists them - nothing on disk or in any repository is changed.");
            message += string.Join(' ', sentences);
        }

        var confirmed = await new ConfirmationWindow(message).ShowDialog<bool>(this);
        if (confirmed)
        {
            // Applied at once and saved in the background
            viewModel.RemoveGroup(groupName);
        }
    }

    /// <summary>
    ///     Asks the user to confirm deleting a group together with everything in it - its repos, favorites
    ///     included, and the groups inside it with theirs - saying how many go, and, if confirmed, deletes
    ///     them from AgentControl's list. Nothing on disk or in any repository is deleted, which the message
    ///     says.
    /// </summary>
    /// <remarks>
    ///     The message names the group as it is stored and leaves out the part about repos or subgroups
    ///     when there are none, so it never reads "0 repo(s)".
    /// </remarks>
    /// <param name="viewModel">The main view model that owns the groups.</param>
    /// <param name="groupName">The group to delete with its contents.</param>
    private async Task ConfirmAndRemoveGroupWithContents(MainWindowViewModel viewModel, string groupName)
    {
        var contents = viewModel.GetGroupContents(groupName);
        var question = $"Delete the group \"{contents.Name}\"";
        if (contents.SubgroupCount > 0)
        {
            question += $" and its {contents.SubgroupCount} subgroup(s)";
        }

        if (contents.RepoCount > 0)
        {
            var favorites = contents.FavoriteCount > 0 ? $" ({contents.FavoriteCount} of them favorites)" : string.Empty;
            question += $" together with its {contents.RepoCount} repo(s){favorites}";
        }

        var removed = contents.RepoCount > 0 ? "The repos are removed from AgentControl's list." : "The groups are removed from AgentControl's list.";
        var confirmed = await new ConfirmationWindow(
                $"{question}?\n\n{removed} This only removes them from AgentControl - nothing " +
                "on disk or in any repository is deleted.")
            .ShowDialog<bool>(this);

        if (confirmed)
        {
            // Applied at once and saved in the background
            viewModel.RemoveGroupWithContents(groupName);
        }
    }

    /// <summary>
    ///     Adds a repo to the recent-repos list. Always shows a native folder-browse dialog first;
    ///     if the user cancels the picker (empty result), does nothing - it never shows a
    ///     spurious error message box for a deliberate cancel.
    /// </summary>
    /// <param name="sender">The clicked "Add Repo" button.</param>
    /// <param name="e">Routed event arguments (unused).</param>
    private async void AddRepoButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await ShowNoticeAsync(await AddRepoAsync(viewModel, groupName: null));
    }

    /// <summary>
    ///     Shows a message box for a notice returned by <see cref="AddRepoAsync"/>, if there is one.
    /// </summary>
    /// <param name="notice">The notice, or <see langword="null"/> when there is nothing to tell.</param>
    /// <returns>A task that completes when the message box was closed (or none was needed).</returns>
    private async Task ShowNoticeAsync(string? notice)
    {
        if (notice is not null)
        {
            await new MessageBoxWindow(notice).ShowDialog(this);
        }
    }

    /// <summary>
    ///     Gets or sets how the user picks repository folders; <see langword="null"/> uses the
    ///     native folder-browse dialog (which allows several folders). A seam so tests can answer with
    ///     paths (or an empty list, for a cancel) without a real dialog.
    /// </summary>
    internal Func<Task<IReadOnlyList<string>>>? RepoFolderPicker { get; set; }

    /// <summary>
    ///     The add-repository flow shared by the top-bar "Add Repo" button and a group header's
    ///     "Add repository..." menu item: asks the user for one or more folders, adds them through the
    ///     view model, and says what the user needs to know about the outcome.
    /// </summary>
    /// <remarks>
    ///     A cancelled picker changes nothing and says nothing. The folders are added in the background
    ///     after any add still running (see <see cref="MainWindowViewModel.EnqueueAddReposAsync"/>), with
    ///     progress in the strip under the app bar; an add the user cancels there says nothing. Each
    ///     folder is handled as a single add would handle it: with a group, a repo that
    ///     is already listed is moved into the group; without one it is refused; a folder that is not
    ///     the top folder of a Git repository is refused. One folder gets the single-add message,
    ///     several get one combined summary (see <see cref="MainWindowViewModel.DescribeAddOutcomes"/>).
    /// </remarks>
    /// <param name="viewModel">The main view model that owns the repo list.</param>
    /// <param name="groupName">The group to put the repos in, or <see langword="null"/> for the plain
    ///     add.</param>
    /// <returns>A message for the user, or <see langword="null"/> when everything was added (or the
    ///     picker or the add was cancelled, or the failure was already reported).</returns>
    internal async Task<string?> AddRepoAsync(MainWindowViewModel viewModel, string? groupName)
    {
        var paths = await (RepoFolderPicker ?? BrowseForRepoPathsAsync)();
        if (paths.Count == 0)
        {
            return null;
        }

        // Added in the background, after any add still running; a cancel tells nothing
        var result = await viewModel.EnqueueAddReposAsync(paths, groupName);
        return result.Canceled ? null : MainWindowViewModel.DescribeAddOutcomes(result.Outcomes, groupName);
    }

    /// <summary>
    ///     Asks the user, when an add could not be saved, whether to try saving again or to discard the
    ///     add, and reports the answer to the waiting add.
    /// </summary>
    /// <remarks>
    ///     A modal <see cref="ConfirmationWindow"/> with Retry and Discard; Discard is the default
    ///     (focused) answer and Escape or closing the dialog also discards, so nothing is ever retried
    ///     without the user asking for it. The request is always answered - a failure to show the
    ///     dialog counts as Discard - because the add (and every add queued after it) would otherwise
    ///     wait for ever.
    /// </remarks>
    /// <param name="request">The question and the way to answer it, as carried by
    ///     <see cref="MainWindowViewModel.AddSaveFailed"/>.</param>
    /// <returns>A task that completes when the request was answered.</returns>
    private async Task AskAddSaveRetryAsync(SettingsSaveRetryRequest request)
    {
        var retry = false;
        try
        {
            retry = await new ConfirmationWindow(request.Message, "Retry", "Discard").ShowDialog<bool>(this);
        }
        catch (Exception ex)
        {
            // This runs from an async void handler: an escaping exception would be unhandled on the
            // UI thread, so a failure to show the dialog is logged and counts as Discard
            _logger.LogError(ex, "Could not show the question about the add that could not be saved");
        }
        finally
        {
            request.Respond(retry);
        }
    }

    /// <summary>
    ///     Shows the native folder-browse dialog, allowing several folders, and returns the chosen
    ///     folders' local paths.
    /// </summary>
    /// <returns>The selected folders' local paths (folders without a local path are left out), or
    ///     an empty list if the user canceled the dialog (honoring the OK/Cancel result rather than
    ///     assuming a folder was always chosen).</returns>
    private async Task<IReadOnlyList<string>> BrowseForRepoPathsAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select one or more repository folders",
            AllowMultiple = true
        });

        return [.. folders.Select(folder => folder.TryGetLocalPath()).OfType<string>()];
    }

    /// <summary>
    ///     Opens the modal <see cref="SettingsWindow"/>, applying any saved changes back to the
    ///     view model (and thereby refreshing every repo card), and once it closes re-checks the
    ///     cards that still show a package source problem or a failed upgrade check.
    /// </summary>
    /// <remarks>
    ///     The re-check after closing is what makes a sign-in typed during <b>Test connection</b>
    ///     (or a connection that came back) show on the cards even when the user closes Settings
    ///     without saving; it is started in the background and not awaited, so the window stays
    ///     usable. Whether the settings were saved is passed along so a save, which already
    ///     re-checks every card, is not followed by a second check.
    /// </remarks>
    /// <param name="sender">The clicked "Settings" button.</param>
    /// <param name="e">Routed event arguments (unused).</param>
    private async void OpenSettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var settingsViewModel = new SettingsWindowViewModel(
            viewModel.Settings,
            viewModel.ApplySettingsAsync,
            providerFactory: viewModel.CreateConnectionTestProvider,
            credentials: viewModel.Credentials);

        // Saved is raised only when the settings were applied successfully
        var settingsSaved = false;
        settingsViewModel.Saved += (_, _) => settingsSaved = true;
        var settingsWindow = new SettingsWindow(settingsViewModel);
        await settingsWindow.ShowDialog(this);

        // A Test connection may have signed in, or the network may be back: check the cards that
        // show a problem again in the background (nothing more after a save, which checked them all)
        _ = viewModel.RecheckAfterSettingsClosed(settingsSaved);
    }

    /// <summary>
    ///     Opens the modal <see cref="AboutWindow"/> over this window, like every other dialog: this
    ///     window cannot be used (or closed) until About is closed, and keyboard focus stays in it.
    /// </summary>
    /// <param name="sender">The clicked "About" button.</param>
    /// <param name="e">Routed event arguments (unused).</param>
    private async void OpenAboutButton_Click(object? sender, RoutedEventArgs e)
    {
        await new AboutWindow().ShowDialog(this);
    }
}
