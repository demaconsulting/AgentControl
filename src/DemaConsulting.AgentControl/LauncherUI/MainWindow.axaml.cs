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
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using DemaConsulting.AgentControl.RepoSync;

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
    ///     Initializes a new <see cref="MainWindow"/>, loading its compiled XAML.
    /// </summary>
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += OnDataContextChanged;
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
    }

    /// <summary>
    ///     Attaches this window's error/release-notes handlers to newly-added repo cards.
    /// </summary>
    /// <param name="sender">The observable collection (unused).</param>
    /// <param name="e">Describes which cards were added.</param>
    private void OnRepoCardsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
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
    ///     <see cref="RepoCardViewModel.ReleaseNotesReady"/>, and
    ///     <see cref="RepoCardViewModel.RemoveRequested"/> events so failures show a message
    ///     box, successful upgrades open a non-modal <see cref="ReleaseNotesViewer"/>, and a
    ///     remove request shows a confirmation dialog before actually removing the card.
    /// </summary>
    /// <param name="card">The card to subscribe to.</param>
    private void AttachCardHandlers(RepoCardViewModel card)
    {
        card.ErrorOccurred += (_, message) => new MessageBoxWindow(message).ShowDialog(this);
        card.ReleaseNotesReady += (_, releaseNotes) =>
            new ReleaseNotesViewer(new ReleaseNotesViewerViewModel(card.RepoName, releaseNotes)).Show();
        card.RemoveRequested += async (_, _) => await ConfirmAndRemoveCard(card);
        card.SelectPackageRequested += async (_, sourceDirectory) => await ShowSelectPackageDialog(card, sourceDirectory);
    }

    /// <summary>
    ///     Shows the modal <see cref="SelectPackageWindow"/> for <paramref name="card"/>, and, if
    ///     the user confirmed a package name/version, applies it via
    ///     <see cref="RepoCardViewModel.ApplySelectedPackage"/>.
    /// </summary>
    /// <param name="card">The card that requested package selection.</param>
    /// <param name="sourceDirectory">The resolved package-source directory to enumerate.</param>
    private async Task ShowSelectPackageDialog(RepoCardViewModel card, string sourceDirectory)
    {
        var viewModel = new SelectPackageWindowViewModel(sourceDirectory, card.PackageVersionCache);
        var result = await new SelectPackageWindow(viewModel).ShowDialog<(string Name, string Version)?>(this);
        if (result is not null)
        {
            card.ApplySelectedPackage(result.Value.Name, result.Value.Version);
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
            viewModel.RemoveRepo(card);
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

        if (!viewModel.AddRepo(path))
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

        var settingsViewModel = new SettingsWindowViewModel(viewModel.Settings, viewModel.ApplySettings);
        var settingsWindow = new SettingsWindow(settingsViewModel);
        await settingsWindow.ShowDialog(this);
    }

    /// <summary>
    ///     Opens the non-modal <see cref="AboutWindow"/>.
    /// </summary>
    /// <param name="sender">The clicked "About" button.</param>
    /// <param name="e">Routed event arguments (unused).</param>
    private static void OpenAboutButton_Click(object? sender, RoutedEventArgs e)
    {
        new AboutWindow().Show();
    }
}
