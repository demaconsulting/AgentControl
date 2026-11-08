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
using DemaConsulting.AgentControl.RepoSync;
using DemaConsulting.AgentControl.Settings;
using Material.Icons;
using Material.Icons.Avalonia;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     Main launcher window: shows the recent-repos list and exposes Add-Repo/Settings actions.
/// </summary>
/// <remarks>
///     Everything this class does is view-layer plumbing that requires a live Avalonia
///     <see cref="Window"/>/<see cref="IStorageProvider"/> (folder-browse dialog, opening
///     <see cref="SettingsWindow"/> and <see cref="ReleaseNotesViewer"/>, wiring per-card events
///     to a message box) - all Launch/Pull/Upgrade/settings-persistence business logic lives in
///     <see cref="MainWindowViewModel"/> and <see cref="RepoCardViewModel"/> so it stays testable
///     without this window ever being constructed in a unit test.
/// </remarks>
internal sealed partial class MainWindow : Window
{
    /// <summary>
    ///     Tracks which repo cards have already had their (lazy, non-cacheable)
    ///     <see cref="RepoCardViewModel.RefreshDirtyStatus"/> triggered by this window, so the
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
        DataContextChanged += OnDataContextChanged;
        Closing += MainWindow_Closing;

        // Remember which card's favorite star has the keyboard focus (see RestoreStarFocus)
        AddHandler(GotFocusEvent, OnAnyGotFocus, RoutingStrategies.Bubble);
        Deactivated += (_, _) => _focusedStarCard = null;
    }

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
    /// <param name="sender">This window (unused).</param>
    /// <param name="e">Event arguments (unused).</param>
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        foreach (var card in viewModel.RepoCards)
        {
            AttachCardHandlers(card);
        }

        viewModel.RepoCards.CollectionChanged += OnRepoCardsChanged;
        viewModel.SettingsSaveFailed += (_, message) => new MessageBoxWindow(message).ShowDialog(this);
        viewModel.GroupRenameRequested += async (_, name) => await ShowRenameGroupPrompt(viewModel, name);
        viewModel.GroupRemoveRequested += async (_, name) => await ConfirmAndRemoveGroup(viewModel, name);

        // The upgrade check reads package sources, which may be slow or unreachable, so it starts
        // in the background now that the window has its data rather than delaying the window
        _ = viewModel.BeginUpgradeRefresh();
    }

    /// <summary>
    ///     Attaches this window's error/release-notes handlers to newly-added repo cards.
    /// </summary>
    /// <param name="sender">The observable collection (unused).</param>
    /// <param name="e">Describes which cards were added.</param>
    private void OnRepoCardsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
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
    ///     <see cref="RepoCardViewModel.RemoveRequested"/>, and
    ///     <see cref="RepoCardViewModel.AgentsMdTemplateOfferRequested"/> events so failures show
    ///     a message box, successful upgrades open a non-modal <see cref="ReleaseNotesViewer"/>
    ///     tracked by this window (see <see cref="ShowTrackedChildWindow"/>, so it closes when
    ///     this window does, rather than outliving it), a remove request shows a confirmation
    ///     dialog before actually removing the card, and an AGENTS.md template offer shows a
    ///     modal <see cref="ConfirmationWindow"/> before writing the template or recording a
    ///     decline.
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

        // Runs after the view model has regrouped the list (its handler was attached when the card was created)
        card.FavoriteChanged += OnFavoriteChanged;
        card.ErrorOccurred += OnErrorOccurred;
        card.ReleaseNotesReady += OnReleaseNotesReady;
        card.RemoveRequested += OnRemoveRequested;
        card.SelectPackageRequested += OnSelectPackageRequested;
        card.NewGroupRequested += OnNewGroupRequested;
        card.AgentsMdTemplateOfferRequested += OnTemplateOfferRequested;

        _cardHandlerRemovers[card] = () =>
        {
            card.FavoriteChanged -= OnFavoriteChanged;
            card.ErrorOccurred -= OnErrorOccurred;
            card.ReleaseNotesReady -= OnReleaseNotesReady;
            card.RemoveRequested -= OnRemoveRequested;
            card.SelectPackageRequested -= OnSelectPackageRequested;
            card.NewGroupRequested -= OnNewGroupRequested;
            card.AgentsMdTemplateOfferRequested -= OnTemplateOfferRequested;
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
            viewModel = new SelectPackageWindowViewModel(card.CreateSourceRegistry(), card.PackageVersionCache);
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
    ///     Shows the <see cref="ConfirmationWindow"/> asking the user to confirm removing
    ///     <paramref name="card"/> from AgentControl's recent-repos list, and only calls
    ///     <see cref="MainWindowViewModel.RemoveRepo"/> if the user confirms. Never removes
    ///     anything from disk - the message text makes this explicit.
    /// </summary>
    /// <param name="card">The card the user asked to remove.</param>
    private async Task ConfirmAndRemoveCard(RepoCardViewModel card)
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
            try
            {
                viewModel.RemoveRepo(card);
            }
            catch (InvalidOperationException ex)
            {
                // The removal was not saved, so the repo is still listed; say why
                await new MessageBoxWindow($"The repo was not removed. {SecretRedactor.SafeMessage(ex)}").ShowDialog(this);
            }
        }
    }

    /// <summary>
    ///     Shows the <see cref="ConfirmationWindow"/> offering to place the package's optional
    ///     <c>AGENTS.md</c> template at <paramref name="card"/>'s repo root, and calls back into
    ///     <see cref="RepoCardViewModel.AcceptAgentsMdTemplate"/> or
    ///     <see cref="RepoCardViewModel.DeclineAgentsMdTemplate"/> depending on the user's choice.
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
            card.AcceptAgentsMdTemplate(templateContent);
        }
        else
        {
            card.DeclineAgentsMdTemplate();
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
    ///     Triggers a newly-realized repo card's lazy <see cref="RepoCardViewModel.RefreshDirtyStatus"/>
    ///     check the first time its list container attaches to the visual tree.
    /// </summary>
    /// <remarks>
    ///     <b>Simplification note:</b> this is a "first-attach" trigger, not true
    ///     viewport-visibility tracking - it fires once per card shortly after the card's
    ///     container is realized (which, since <see cref="ListBox"/> here is not virtualizing a
    ///     huge list, is effectively "shortly after window load" for all initially-visible
    ///     cards). It intentionally does not re-fire if a card scrolls out of and back into view,
    ///     nor does it defer work for off-screen cards in a long list. Per the plan's own
    ///     guidance, literal viewport-visibility tracking is not required for this feature; this
    ///     approximation keeps the working-tree dirty check lazy (never run during the
    ///     cheap/eager startup pass) while still populating it for the user shortly after
    ///     launch, without the added complexity of scroll-position-aware virtualization tracking.
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

        card.RefreshDirtyStatus();
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

        var submenu = flyout.Items.OfType<MenuItem>().FirstOrDefault(item => item.Name == "MoveToGroupMenuItem");
        if (submenu is null)
        {
            return;
        }

        var entries = new List<object>();
        var groups = card.GetExistingGroupNames();
        foreach (var group in groups)
        {
            // The header is a TextBlock so an underscore in a group name is not taken as an access key
            var isCurrent = RepoGroupName.Comparer.Equals(group, card.GroupName ?? string.Empty);
            var entry = new MenuItem
            {
                Header = new TextBlock { Text = group },
                Icon = isCurrent ? new MaterialIcon { Kind = MaterialIconKind.Check } : null
            };
            AutomationProperties.SetAutomationId(entry, "MoveToGroupItem");
            AutomationProperties.SetName(entry, group);
            ToolTip.SetTip(
                entry,
                isCurrent ? $"{card.RepoName} is in the group {group}." : $"Move {card.RepoName} to the group {group}.");
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

        var button = FindControlOfCard<Button>(card, "MoreActionsButton");
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
    ///     Applies the color of the clicked swatch (or "Automatic") to the card whose palette is open,
    ///     then closes the palette.
    /// </summary>
    /// <param name="sender">The clicked swatch or "Automatic" <see cref="Button"/>; its tag is the
    ///     palette color's name, or empty for Automatic.</param>
    /// <param name="e">Routed event arguments (unused).</param>
    private void CardColorSwatch_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name } && _paletteCard is { } card)
        {
            card.SetCardColor(name.Length == 0 ? null : name);
        }

        _paletteFlyout?.Hide();
        _paletteFlyout = null;
        _paletteCard = null;
    }

    /// <summary>
    ///     Finds a named control inside a card's list row.
    /// </summary>
    /// <typeparam name="T">The control type.</typeparam>
    /// <param name="card">The card whose row to search.</param>
    /// <param name="name">The control's <see cref="StyledElement.Name"/>.</param>
    /// <returns>The control, or <see langword="null"/> when the card is not shown (for example it
    ///     is in a collapsed group or filtered out).</returns>
    private T? FindControlOfCard<T>(RepoCardViewModel card, string name)
        where T : Control
    {
        var list = this.FindControl<ListBox>("RepoListBox");
        return list?.ContainerFromItem(card)?
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
            try
            {
                viewModel.RenameGroup(groupName, name);
            }
            catch (InvalidOperationException ex)
            {
                await new MessageBoxWindow($"The group was not renamed. {SecretRedactor.SafeMessage(ex)}").ShowDialog(this);
            }
        }
    }

    /// <summary>
    ///     Asks the user to confirm removing a group and, if confirmed, removes it (its repos become
    ///     ungrouped). Nothing on disk or in any repository changes, which the message says.
    /// </summary>
    /// <param name="viewModel">The main view model that owns the groups.</param>
    /// <param name="groupName">The group to remove.</param>
    private async Task ConfirmAndRemoveGroup(MainWindowViewModel viewModel, string groupName)
    {
        var members = viewModel.RepoCards.Count(card => RepoGroupName.Comparer.Equals(card.GroupName ?? string.Empty, groupName));
        var confirmed = await new ConfirmationWindow(
                $"Remove the group \"{groupName}\"?\n\n" +
                $"Its {members} repo(s) stay in AgentControl's list, ungrouped. This only changes how AgentControl " +
                "lists them - nothing on disk or in any repository is changed.")
            .ShowDialog<bool>(this);

        if (confirmed)
        {
            try
            {
                viewModel.RemoveGroup(groupName);
            }
            catch (InvalidOperationException ex)
            {
                await new MessageBoxWindow($"The group was not removed. {SecretRedactor.SafeMessage(ex)}").ShowDialog(this);
            }
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

        var path = await BrowseForRepoPathAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        bool added;
        try
        {
            added = viewModel.AddRepo(path);
        }
        catch (InvalidOperationException ex)
        {
            // The repo was not added because the list could not be saved; say why
            await new MessageBoxWindow($"'{path}' could not be added. {SecretRedactor.SafeMessage(ex)}").ShowDialog(this);
            return;
        }

        if (!added)
        {
            await new MessageBoxWindow($"'{path}' could not be added: it does not exist or is already in the list.")
                .ShowDialog(this);
        }
    }

    /// <summary>
    ///     Shows the native folder-browse dialog and returns the chosen folder's local path.
    /// </summary>
    /// <returns>The selected folder's local path, or <see langword="null"/> if the user
    ///     canceled the dialog (honoring the OK/Cancel result rather than assuming a folder was
    ///     always chosen).</returns>
    private async Task<string?> BrowseForRepoPathAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select a repository folder",
            AllowMultiple = false
        });

        var folder = folders.Count > 0 ? folders[0] : null;
        return folder?.TryGetLocalPath();
    }

    /// <summary>
    ///     Opens the modal <see cref="SettingsWindow"/>, applying any saved changes back to the
    ///     view model (and thereby refreshing every repo card).
    /// </summary>
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
            viewModel.ApplySettings,
            providerFactory: viewModel.CreateConnectionTestProvider,
            credentials: viewModel.Credentials);
        var settingsWindow = new SettingsWindow(settingsViewModel);
        await settingsWindow.ShowDialog(this);
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
