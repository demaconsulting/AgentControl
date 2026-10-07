## LauncherUI

![LauncherUI Structure](LauncherUIView.svg)

### Overview

The `LauncherUI` subsystem is the Avalonia-based user interface a developer interacts with
directly. It spans the recent-repos main window, per-repo cards, the package-selection
window, and the settings window (`MainWindowViewModel`, `RepoCardViewModel`,
`SelectPackageWindowViewModel`, `SettingsWindowViewModel`), plus their `.axaml` Views
(`MainWindow`, `AboutWindow`, `ConfirmationWindow`, `MessageBoxWindow`, `SelectPackageWindow`,
`SettingsWindow`) and shared MVVM support types (`RelayCommand`, `ViewModelBase`,
`FavoriteIconConverter`), which have no dedicated test files and are folded into this
subsystem-level description rather than given separate unit files. The subsystem contains
four units: `MainWindowViewModel`, `RepoCardViewModel`, `SelectPackageWindowViewModel`, and
`SettingsWindowViewModel`.

### Interfaces

**MainWindowViewModel.DisplayedRepoCards**: The filtered, sorted collection of repo cards
shown in the main window.

- *Type*: In-process .NET observable collection (data-bound to `MainWindow`'s `ItemsControl`).
- *Role*: Provider.
- *Contract*: Reflects `AddRepo`/`RemoveRepo` mutations, `FilterText` changes, and sort order
  (favorites first, then most-recently-launched descending with never-launched last)
  (`AgentControl-LauncherUI-RecentReposDisplay`, `AgentControl-LauncherUI-SearchFilter`,
  `AgentControl-LauncherUI-SortOrder`).
- *Constraints*: Recomputed synchronously on the UI thread whenever a dependency changes.

**RepoCardViewModel's action commands**: `LaunchCommand`, `PullCommand`,
`SelectPackageCommand`, `UpgradeCommand`, `FavoriteToggleCommand`, `RemoveCommand`.

- *Type*: In-process .NET `ICommand` (`RelayCommand`), bound to `Button`/`MenuItem` controls.
- *Role*: Provider.
- *Contract*: Each command's `CanExecute` reflects the card's current state (e.g.
  `PullCommand` is only executable when the working tree is clean,
  `AgentControl-LauncherUI-Pull`); `Execute` delegates to the relevant subsystem
  (`GitIntegration`, `RepoSync`, `AgentToolLauncher`, `RepoConfig`) — Launch only once agent
  files are confirmed synced (`AgentControl-LauncherUI-Launch`), Upgrade only when a newer
  version exists at the source (`AgentControl-LauncherUI-Upgrade`) — and raises a view-model
  event (`LaunchRecorded`, `SelectPackageRequested`, `ReleaseNotesReady`,
  `AgentsMdTemplateOfferRequested`, `ErrorOccurred`, `RemoveRequested`) reporting the outcome.
  Status badges (upgrade-available, missing-repo, committed-agent-files) are refreshed
  alongside these actions (`AgentControl-LauncherUI-StatusBadges`).
- *Constraints*: Never mutates state before a required confirmation (see `RemoveRequest`
  below); never launches without a successful ensure-synced check.

**SelectPackageWindowViewModel.ConfirmCommand**: Confirms a package name/version selection.

- *Type*: In-process .NET `ICommand`.
- *Role*: Provider.
- *Contract*: Executable only when both a package name and version are selected; raises
  `Confirmed` with the selected pair for the owning `RepoCardViewModel` to apply
  (`AgentControl-LauncherUI-SelectPackage`).
- *Constraints*: Does not itself mutate any repo state; the caller applies the result.

**SettingsWindowViewModel.SaveCommand**: Persists edited settings.

- *Type*: In-process .NET `ICommand`.
- *Role*: Provider.
- *Contract*: Invokes an `onSave` callback with the current property values and raises
  `Saved`, letting `MainWindowViewModel.ApplySettings` persist the change and invalidate any
  cached package version data (`AgentControl-LauncherUI-Settings`).
- *Constraints*: None beyond standard `ICommand` semantics.

### Design

`MainWindowViewModel` owns the `ObservableCollection<RepoCardViewModel>` backing the
recent-repos list; on construction it creates one `RepoCardViewModel` per `RecentRepo` in the
loaded `AppSettings`. It exposes `AddRepo`/`RemoveRepo` (persisting via `SettingsStore` after
each mutation), a `FilterText` property driving `DisplayedRepoCards`, and `ApplySettings`
(invoked when `SettingsWindowViewModel` reports `Saved`), which persists the new settings
while preserving the existing recent-repos list and invalidates the shared
`PackageVersionCache`.

Each `RepoCardViewModel` is independently responsible for one repo's status and actions: it
reads the pin via `RepoPinStore`, checks upgrade availability via `PackageSource`/
`PackageVersionCache`, resolves git status/branch via `GitClient`/`CommittedAgentFilesCache`,
and drives Launch/Pull/Upgrade/Select-Package/Favorite/Remove. A lightweight `RefreshCheap`
path (branch resolution, cached committed-files badge) is kept separate from the full
`Refresh` path (which also checks working-tree cleanliness) so the main window's frequent UI
refresh does not eagerly invoke a git subprocess for every card. Removal never mutates state
directly: `RemoveCommand` raises `RemoveRequested` and `MainWindowViewModel` shows a
`ConfirmationWindow`, removing the card only if the user confirms
(`AgentControl-LauncherUI-RemoveRepo`).

`SelectPackageWindowViewModel` backs the package-selection dialog opened from a card with no
existing pin: it lists package names from `PackageSource`/`PackageVersionCache`, then that
name's versions in descending order (defaulting to latest) once a name is chosen.
`SettingsWindowViewModel` seeds its editable properties from the current `AppSettings` on
construction and exposes `IsCustomAgentToolSelected` to drive the settings window's
custom-command entry field visibility.

`RepoCardViewModel` reuses the existing modal `ConfirmationWindow` (the same Yes/No dialog
shown for Remove confirmation) to offer a package's optional root-level `AGENTS.md` template
when the repo has none of its own: `MainWindow` subscribes to
`AgentsMdTemplateOfferRequested`, shows `ConfirmationWindow` with explanatory text, and calls
back into `AcceptAgentsMdTemplate`/`DeclineAgentsMdTemplate` depending on the user's answer
(`AgentControl-LauncherUI-AgentsMdTemplateOffer`). This mirrors the non-modal
`ReleaseNotesViewer` pattern's event-raise/subscribe structure but intentionally uses a
*modal* dialog instead, since this prompt requires an explicit yes/no decision before
proceeding, unlike release notes which are purely informational.

`ViewModelBase` supplies a minimal hand-rolled `INotifyPropertyChanged` implementation (not a
source-generator package, to keep the dependency surface minimal) used by every view model in
this subsystem. `RelayCommand` is a minimal hand-rolled `ICommand` used for every command
property. `FavoriteIconConverter` binds `RepoCardViewModel.IsFavorite` to a filled or outlined
star `MaterialIconKind` on the repo card's favorite toggle, per architecture.md's "UI icon
convention" decision. The `.axaml` Views other than `MainWindow`
(`AboutWindow`, `ConfirmationWindow`, `MessageBoxWindow`, `SelectPackageWindow`,
`SettingsWindow`) contain no logic beyond Avalonia's XAML-loading boilerplate and
`AutomationProperties.AutomationId` assignments that let FlaUI locate controls; all behavior
lives in the view models above. `MainWindow`'s code-behind is the one exception: it owns
dialog-orchestration, view-layer plumbing that requires a live Avalonia `Window` — subscribing
to each `RepoCardViewModel` event (`RemoveRequested`, `SelectPackageRequested`,
`ReleaseNotesReady`, `AgentsMdTemplateOfferRequested`, `ErrorOccurred`) and deciding which
dialog/window to show and how to route the user's response back into the view model (e.g.
calling `AcceptAgentsMdTemplate`/`DeclineAgentsMdTemplate` from the `ConfirmationWindow` shown
for the AGENTS.md template offer). `AboutWindow` (opened via a
toolbar command with no dedicated view model) shows the application version and copyright
read from `Program.Version` (`AgentControl-LauncherUI-About`). Adding a repo
(`AgentControl-LauncherUI-AddRepo`) is likewise driven directly by `MainWindowViewModel`
rather than a dedicated unit, since its logic (path-existence and duplicate-path validation)
is simple enough not to warrant a separate class.

`MainWindow` explicitly tracks every non-modal child window it opens (`ReleaseNotesViewer`,
`AboutWindow`) in an `_openChildWindows` list via a `ShowTrackedChildWindow(Window)` helper,
which also subscribes to each window's `Closed` event to remove it from the list, and force-
closes any still-open entries from a `MainWindow_Closing` handler before the main window
itself finishes closing. This deliberately does *not* use Avalonia/Win32 window ownership
(`Show(owner)`): an earlier attempt at that approach caused an owned child window to actively
block its owner's own close on Windows, a worse regression than the one being fixed. Without
either mechanism, these non-owned `.Show()` windows can outlive the main window under
Avalonia's default `ShutdownMode.OnLastWindowClose`, keeping the process alive and forcing
slow process-kill fallbacks in automated UI tests that close the main window. Both the
tracked-window-open and forced-close events are logged (`Debug`/`Information`) via
`MainWindow`'s `_logger`.
