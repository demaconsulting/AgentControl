## LauncherUI

![LauncherUI Structure](LauncherUIView.svg)

### Overview

The `LauncherUI` subsystem is the Avalonia-based user interface a developer interacts with
directly. It spans the recent-repos main window, per-repo cards, the package-selection
window, the settings window and the package-source editor window (`MainWindowViewModel`,
`RepoCardViewModel`, `SelectPackageWindowViewModel`, `SettingsWindowViewModel`,
`SourceEditorViewModel`), plus their `.axaml` Views (`MainWindow`, `AboutWindow`,
`ConfirmationWindow`, `MessageBoxWindow`, `SelectPackageWindow`, `SettingsWindow`,
`SourceEditorWindow`) and shared MVVM support types (`RelayCommand`, `AsyncRelayCommand`,
`ViewModelBase`, `PackageSourceIcons`, `PackageSourceItemViewModel`,
`SelectPackageSourceViewModel`, `FavoriteIconConverter`, `FavoriteColorConverter`,
`ThirdPartyDependencies`), which are folded
into this subsystem-level description rather than given separate unit files; `ThirdPartyDependencies`
and `AsyncRelayCommand` are the exceptions with dedicated test files
(`ThirdPartyDependenciesTests.cs`, `AsyncRelayCommandTests.cs`), since each is independent of
any Avalonia control. The subsystem contains
five units: `MainWindowViewModel`, `RepoCardViewModel`, `SelectPackageWindowViewModel`,
`SettingsWindowViewModel`, and `SourceEditorViewModel`.

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

- *Type*: In-process .NET `ICommand` (`AsyncRelayCommand` for the operations that read a
  package source or the file system - Launch, Pull, Upgrade, Refresh - and `RelayCommand` for the
  rest), bound to `Button`/`MenuItem` controls.
- *Role*: Provider.
- *Contract*: Each command's `CanExecute` reflects the card's current state (e.g.
  `PullCommand` is only executable when the working tree is clean,
  `AgentControl-LauncherUI-Pull`); `Execute` delegates to the relevant subsystem
  (`GitIntegration`, `RepoSync`, `AgentToolLauncher`, `RepoConfig`) — Launch only once agent
  files are confirmed synced (`AgentControl-LauncherUI-Launch`), Upgrade only when a newer
  version exists at the source (`AgentControl-LauncherUI-Upgrade`) — and raises a view-model
  event (`LaunchRecorded`, `SelectPackageRequested`, `ReleaseNotesReady`,
  `AgentsMdTemplateOfferRequested`, `ErrorOccurred`, `RemoveRequested`) reporting the outcome.
  An asynchronous command ignores a second request while it runs and can be cancelled through
  `CancelOperationCommand` (`AgentControl-LauncherUI-ResponsiveOperations`). The card also
  exposes the chip properties (package, upgrade, branch, status) the view binds to
  (`AgentControl-LauncherUI-RepoCardChips`, `AgentControl-LauncherUI-StatusBadges`).
- *Constraints*: Never mutates state before a required confirmation (see `RemoveRequest`
  below); never launches without a successful ensure-synced check.

**SelectPackageWindowViewModel.ConfirmCommand**: Confirms a source, package and version
selection.

- *Type*: In-process .NET `ICommand`.
- *Role*: Provider.
- *Contract*: Executable only when a source, a package name and a version are selected; raises
  `Confirmed` with the full package reference (source alias, package name, version) for the
  owning `RepoCardViewModel` to apply (`AgentControl-LauncherUI-SelectPackage`).
- *Constraints*: Does not itself mutate any repo state; the caller applies the result.

**SettingsWindowViewModel.SaveCommand**: Persists edited settings.

- *Type*: In-process .NET `ICommand`.
- *Role*: Provider.
- *Contract*: Invokes an `onSave` callback with the current property values, including the
  edited list of package sources, and raises `Saved`, letting
  `MainWindowViewModel.ApplySettings` persist the change and invalidate the cached package
  data of every edited or removed source (`AgentControl-LauncherUI-Settings`).
- *Constraints*: None beyond standard `ICommand` semantics.

**SourceEditorViewModel.SaveCommand**: Accepts an edited package source definition.

- *Type*: In-process .NET `ICommand`.
- *Role*: Provider.
- *Contract*: Executable only while the alias, location and (for a NuGet folder source) package
  identifiers are valid; raises `Saved` with the resulting source definition for the settings
  view model to add to or replace in its list (`AgentControl-LauncherUI-PackageSources`).
- *Constraints*: The alias of an existing source cannot be changed.

### Design

`MainWindowViewModel` owns the `ObservableCollection<RepoCardViewModel>` backing the
recent-repos list; on construction it creates one `RepoCardViewModel` per `RecentRepo` in the
loaded `AppSettings`. It exposes `AddRepo`/`RemoveRepo` (persisting via `SettingsStore` after
each mutation), a `FilterText` property driving `DisplayedRepoCards`, and `ApplySettings`
(invoked when `SettingsWindowViewModel` reports `Saved`), which persists the new settings
while preserving the existing recent-repos list and invalidates the shared
`PackageVersionCache` for each package source that was edited or removed.

Each `RepoCardViewModel` is independently responsible for one repo's status and actions: it reads
the pin via `RepoPinStore`, checks upgrade availability via `PackageSourceRegistry`/
`PackageVersionCache` at the source the pin names (showing an error state, never using another
source, when that source is not configured - `AgentControl-LauncherUI-SourceAwareActions`), resolves
git status/branch via `GitClient`/`CommittedAgentFilesCache`, and drives
Launch/Pull/Upgrade/Select-Package/Favorite/Remove. The card presents its state as chips: a package
chip (robot icon, package name and version, a small icon for the source kind, and a tooltip naming
the source), an amber upgrade chip showing the version an upgrade would move to, a grey "No agent
package" chip when nothing is pinned, a branch chip, and color-coded status chips (uncommitted
changes, committed agent files, folder missing, package source problem with the actual problem in
its tooltip); the Launch button is labelled with a short name of the configured agent tool
(falling back to plain "Launch" for a long or unknown name) and the pull button reads "Git Pull"
(`AgentControl-LauncherUI-RepoCardChips`). A lightweight `RefreshCheap` path (branch
resolution, cached committed-files badge, pin and source problem - it never reads a package
source) is kept separate from the full `Refresh` path (which also
checks working-tree cleanliness) so the main window's frequent UI refresh does not eagerly invoke a
git subprocess for every card. Removal never mutates state directly: `RemoveCommand` raises
`RemoveRequested` and `MainWindowViewModel` shows a `ConfirmationWindow`, removing the card only if
the user confirms (`AgentControl-LauncherUI-RemoveRepo`).

`SelectPackageWindowViewModel` backs the package-selection dialog opened from a card with no
existing pin. It shows three columns - source, package, version. Selecting a source loads that
source's package names from `PackageVersionCache` in the background, with a progress indicator
and cancellation; a source that cannot be read shows its error inline and never blocks the other
sources. Selecting a package lists its versions in descending order (defaulting to latest), and
confirming reports the full package reference - source alias, name and version - so the pin
records the source. The three-column layout was chosen over a grouped single list because the
same package name can exist in several sources and the user must see which source they pick from.
`SettingsWindowViewModel` seeds its editable properties from the current `AppSettings` on
construction and exposes `IsCustomAgentToolSelected` to drive the settings window's
custom-command entry field visibility. It lists the configured package sources as
`PackageSourceItemViewModel` rows (alias, kind icon from `PackageSourceIcons`, shortened
location, and a folder-exists status computed on a background thread), offers Add Zip-Package
Folder, Add NuGet-Package Folder, Edit and Remove, and asks the window to open a
`SourceEditorViewModel` dialog for adding and editing; removal asks for confirmation and warns
that repos pinned to the source will show a source problem. A further "add" action for another
source kind fits beside the existing two buttons. `SourceEditorViewModel` validates the alias
(required, unique ignoring case, fixed once the source exists), the location (absolute folder
path, no embedded credentials) and, for a NuGet folder, a non-empty list of package
identifiers, reusing the `PackageSourceDefinition` validation of `AgentPackageManagement`;
its Save command stays disabled while anything is invalid.

`AsyncRelayCommand` is the asynchronous counterpart of `RelayCommand`: it runs a task-returning
operation with a cancellation token, ignores a new request while one is running, exposes
`IsRunning` and `Cancel`, and reports an unhandled failure to a required error callback instead of
letting it escape. `RepoCardViewModel` uses it so that reading package sources, extracting a
package or pulling never runs on the UI thread, and shows a busy message with a Cancel button
while one runs. Upgrade availability is determined by a separate background check per card
(`RefreshUpgradeStatusAsync`, where a newer check supersedes an older one), which
`MainWindowViewModel` starts a few cards at a time after construction, after a repo is added
and after settings are applied, so that one slow or unreachable source delays only the cards
pinned to it (`AgentControl-LauncherUI-ResponsiveOperations`).

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
convention" decision. `FavoriteColorConverter` complements it, binding the same `IsFavorite`
value to the star glyph's color (gold when favorited, otherwise the icon's default theme
foreground via `AvaloniaProperty.UnsetValue`), so a favorited repo's star also reads as
"activated" through color rather than relying solely on the filled-vs-outline glyph shape.
The `.axaml` Views other than `MainWindow`
(`AboutWindow`, `ConfirmationWindow`, `MessageBoxWindow`, `SelectPackageWindow`,
`SettingsWindow`, `SourceEditorWindow`) contain no logic beyond Avalonia's XAML-loading boilerplate and
`AutomationProperties.AutomationId` assignments that let FlaUI locate controls; all behavior
lives in the view models above. `MainWindow`'s code-behind is the one exception: it owns
dialog-orchestration, view-layer plumbing that requires a live Avalonia `Window` — subscribing
to each `RepoCardViewModel` event (`RemoveRequested`, `SelectPackageRequested`,
`ReleaseNotesReady`, `AgentsMdTemplateOfferRequested`, `ErrorOccurred`) and deciding which
dialog/window to show and how to route the user's response back into the view model (e.g.
calling `AcceptAgentsMdTemplate`/`DeclineAgentsMdTemplate` from the `ConfirmationWindow` shown
for the AGENTS.md template offer). `AboutWindow` (opened via a
toolbar command with no dedicated view model) shows the application logo (reused from
`Assets/AppIcon.ico`, the same icon already used for the main window/taskbar - no new binary
image asset was added), a short mission tagline, the running version and copyright read
from `Program.Version`, and a scrollable list of this application's direct runtime third-party
dependencies and their SPDX license identifiers (package versions are intentionally omitted so
dependency updates do not require edits here), sourced from a static
`ThirdPartyDependencies` list rather than parsed at runtime from the project file
(`AgentControl-LauncherUI-About`). Adding a repo
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
