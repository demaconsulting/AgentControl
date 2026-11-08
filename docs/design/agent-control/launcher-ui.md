## LauncherUI

![LauncherUI Structure](LauncherUIView.svg)

### Overview

The `LauncherUI` subsystem is the Avalonia-based user interface a developer interacts with
directly. It spans the recent-repos main window, per-repo cards, the package-selection
window, the settings window, the package-source editor window and the sign-in dialog
(`MainWindowViewModel`, `RepoCardViewModel`, `SelectPackageWindowViewModel`, `SettingsWindowViewModel`,
`SourceEditorViewModel`, `CredentialPromptViewModel`), plus their `.axaml` Views (`MainWindow`,
`AboutWindow`, `ConfirmationWindow`, `MessageBoxWindow`, `SelectPackageWindow`, `SettingsWindow`,
`SourceEditorWindow`, `CredentialPromptWindow`) and shared MVVM support types (`RelayCommand`,
`AsyncRelayCommand`, `ViewModelBase`, `PackageSourceIcons`, `PackageSourceItemViewModel`,
`SelectPackageSourceViewModel`, `AvaloniaCredentialPrompt`, `ConnectionTestResultKind`,
`FavoriteIconConverter`, `AgentToolKindDisplayNameConverter`, `ThemePreferenceDisplayNameConverter`,
`RepoCardStatus`, `RepoImageLocator`, `ThirdPartyDependencies`), the shared theme resources and styles
(`AppStyles.axaml`, included by `App.axaml`), which are folded
into this subsystem-level description rather than given separate unit files; `ThirdPartyDependencies`
and `AsyncRelayCommand` are the exceptions with dedicated test files
(`ThirdPartyDependenciesTests.cs`, `AsyncRelayCommandTests.cs`), since each is independent of
any Avalonia control. The subsystem contains
six units: `MainWindowViewModel`, `RepoCardViewModel`, `SelectPackageWindowViewModel`,
`SettingsWindowViewModel`, `SourceEditorViewModel` and `CredentialPromptViewModel`. It depends on the
`AgentPackageManagement` subsystem (sources, providers, failure causes) and on the `PackageCredentials`
subsystem (`PackageCredentialContext`, and the `ICredentialPrompt` that `AvaloniaCredentialPrompt` implements) and
on `SignatureVerificationSupport` (whether the signature requirement can be offered). The repo card's package chip
shows a small shield (`RequiresSignature`, `SignatureTooltip` of `RepoCardViewModel`) when the pinned NuGet source
requires signed packages, with an automation identifier `PackageSignatureIcon`
(`AgentControl-LauncherUI-SignatureIndicator`).

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
  `CancelOperationCommand` (`AgentControl-LauncherUI-OneOperationPerRepo`,
  `AgentControl-LauncherUI-CancelOperation`, `AgentControl-LauncherUI-OperationProgress`). The card also
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

**SourceEditorViewModel.SaveCommand** / **TestConnectionCommand**: Accepts an edited package source
definition, and tests a server source's connection.

- *Type*: In-process .NET `ICommand` (`TestConnectionCommand` is an `AsyncRelayCommand`).
- *Role*: Provider.
- *Contract*: `SaveCommand` is executable only while the alias, location and (for a NuGet source) package
  identifiers are valid - for a server, an `https` address ending in `index.json`, or `http` only while
  "Allow insecure HTTP" is ticked; it raises `Saved` with the resulting source definition for the settings
  view model to add to or replace in its list (`AgentControl-LauncherUI-PackageSourceValidation`,
  `AgentControl-LauncherUI-AddPackageSource`, `AgentControl-LauncherUI-EditPackageSource`).
  `TestConnectionCommand` lists the versions of the first package ID, with progress and a Cancel action, and
  reports a plain-language result; it may open the sign-in dialog (`AgentControl-LauncherUI-TestConnection`).
- *Constraints*: The alias of an existing source cannot be changed. The test saves nothing.

**ICredentialPrompt.PromptAsync** (implemented by `AvaloniaCredentialPrompt`): Asks the user to sign in.

- *Type*: In-process .NET interface implementation (asynchronous), shown as a modal dialog.
- *Role*: Provider (to the `PackageCredentials` subsystem).
- *Contract*: Returns the answer the user gave in the sign-in dialog - the credential they typed and whether
  they ticked "Remember on this computer" - or no answer when the user
  cancels or the dialog is closed; cancelling the supplied token closes the dialog
  (`AgentControl-LauncherUI-SignIn`, `AgentControl-LauncherUI-SignInCancelsWithOperation`,
  `AgentControl-LauncherUI-RememberSignIn`).
- *Constraints*: See *Sign-in dialog* in the design below.

**SourceEditorViewModel signature options and ForgetSavedSignInCommand**: The "Package signatures" section of
a NuGet source's editor and the "Forget saved sign-in" action of a server source's editor.

- *Type*: In-process .NET properties and `ICommand`, plus an event asking the view to confirm.
- *Role*: Provider.
- *Contract*: A NuGet source (folder or server) can require signed packages and pin the publisher's certificate
  fingerprint; the checkbox is disabled with an explanation where the platform cannot verify signatures; a zip
  source has no such section (`AgentControl-LauncherUI-SignatureOptions`). A server source can forget the saved
  sign-in, but only after the view has shown a confirmation and the user agreed
  (`AgentControl-LauncherUI-ForgetSavedSignIn`).
- *Constraints*: The fingerprint is saved normalized, and only while signatures are required.

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
chip (robot icon, package name and version, the source's alias as muted text beside a small icon for the
source kind - a server has its own network-server icon - and a tooltip naming the source), an amber
upgrade chip reading "Upgrade to" and the version an upgrade would move to, a grey "No agent
package" chip when nothing is pinned, a branch chip, and color-coded status chips (uncommitted
changes, committed agent files, folder missing, and a red chip for a source problem with the actual problem in
its tooltip - "Sign-in needed" when the pinned server needs a sign-in, otherwise "Package source problem"
(`SourceProblemChipText`)); the outcome of the last Launch/Pull/Upgrade action (`StatusMessage`) is shown
under the repo path; a repo whose folder is missing offers no upgrade; the Launch button is labelled with a
short name of the configured agent tool
(falling back to plain "Launch" for a long or unknown name) and Git Pull is an item of the card's "..." menu
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
and cancellation; a source that cannot be read shows its error inline, with a **Retry** button that reads
only that source (or, when only a package's versions failed, only those) again, and never blocks the other
sources (`AgentControl-LauncherUI-ResponsiveOperations`, `AgentControl-LauncherUI-SourceFailureIsolation`,
`AgentControl-LauncherUI-RetryFailedRead`). A source whose folder is missing is explained in
words, naming the folder; until a package is selected the version column says "Select a package to see its
versions." (`ShowVersionsHint`); each source row has a UI Automation name made of its alias, kind and status
(`AutomationName`). Selecting a package lists its versions in
descending order (defaulting to latest), and
confirming reports the full package reference - source alias, name and version - so the pin
records the source. The three-column layout was chosen over a grouped single list because the
same package name can exist in several sources and the user must see which source they pick from.
`SettingsWindowViewModel` seeds its editable properties from the current `AppSettings` on
construction and exposes `IsCustomAgentToolSelected` to drive the settings window's
custom-command entry field visibility. It lists the configured package sources as
`PackageSourceItemViewModel` rows (alias, kind icon from `PackageSourceIcons`, shortened
location, and a folder-exists status computed on a background thread; a server source shows "Not checked"
because it is never contacted merely to draw the list), offers one **Add** button whose menu lists the
source types (zip-package folder, NuGet-package folder, NuGet-package server), with Edit and Remove (which act
on the selected row) beside it on the list's header line, and also exposes the **Theme** choice (system
default, light or dark) saved with the other settings, and asks the window to open a
`SourceEditorViewModel` dialog for adding and editing; removal asks for confirmation and warns
that repos pinned to the source will show a source problem. `SourceEditorViewModel` validates the alias
(required, unique ignoring case, fixed once the source exists), the location (absolute folder
path, no embedded credentials; for a server an `https` address ending in `index.json`, or `http` only after an
explicit "Allow insecure HTTP" tick, whose warning is shown while it is on) and, for a NuGet source, a non-empty
list of package
identifiers, reusing the `PackageSourceDefinition` validation of `AgentPackageManagement`;
its Save command stays disabled while anything is invalid. For a server it also offers
**Test connection**, which lists the versions of the first package ID with progress and Cancel, and states
the result in words (*Connection OK* with the version count and newest; *Sign-in failed*; *Sign-in canceled*;
*Package not found*; *Timed out*; *Server unreachable*; *Connection failed*), and shows example addresses for
JFrog Artifactory and Azure Artifacts
(`AgentControl-LauncherUI-TestConnection`, `AgentControl-LauncherUI-ServerSourceEditor`,
`AgentControl-LauncherUI-InsecureHttpChoice`). For a NuGet source it also
offers the **Package signatures** section: a "Require signed packages" checkbox (off by default; disabled, with
its tooltip shown even though it is disabled, where `SignatureVerificationSupport` says signatures cannot be
verified) with help text that explains the limit of requiring signatures without a pinned publisher, and a
"Trusted signer certificate fingerprint (SHA-256)" field that is enabled only while signatures are required,
accepts the fingerprint as pasted and explains how to find it. A server's editor also has **Forget saved
sign-in**: the view shows a `ConfirmationWindow` that names the server, and only on "Yes" calls the view model, which
reports the outcome in words below the button; the button is disabled, with a tooltip saying why, when no
credential helper can be reached from the window. `SettingsWindowViewModel` passes the application's
`PackageCredentialContext` to the editor for that purpose
(`AgentControl-LauncherUI-SignatureOptions`, `AgentControl-LauncherUI-ForgetSavedSignIn`).

`AsyncRelayCommand` is the asynchronous counterpart of `RelayCommand`: it runs a task-returning
operation with a cancellation token, ignores a new request while one is running, exposes
`IsRunning` and `Cancel`, and reports an unhandled failure to a required error callback instead of
letting it escape. `RepoCardViewModel` uses it so that reading package sources, extracting a
package or pulling never runs on the UI thread, and shows a busy message with a Cancel button
while one runs. Upgrade availability is determined by a separate background check per card
(`RefreshUpgradeStatusAsync`, where a newer check supersedes an older one), which
`MainWindowViewModel` starts a few cards at a time after construction, after a repo is added
and after settings are applied, so that one slow or unreachable source delays only the cards
pinned to it (`AgentControl-LauncherUI-ResponsiveOperations`, `AgentControl-LauncherUI-SourceFailureIsolation`,
`AgentControl-LauncherUI-StaleResultsDiscarded`). That background check never opens the
sign-in dialog (`RefreshUpgradeStatusAsync` defaults to `allowPrompt: false`): for a server it uses the
credentials already typed this session or saved with the user's Git credential helper (read without any dialog)
and otherwise sets the card's source problem to say a sign-in is
needed and to use the card's menu > Refresh, which is a check the user started and so may ask
(`AgentControl-LauncherUI-BackgroundChecksDoNotPrompt`).

**Sign-in dialog.** When a NuGet server needs credentials for something the user started - Select Package,
Upgrade, Refresh, or the editor's Test connection - the `PackageCredentials` subsystem calls
`ICredentialPrompt`, which `AvaloniaCredentialPrompt` implements
(`AgentControl-LauncherUI-SignIn`, `AgentControl-LauncherUI-SignInPlacement`). The NuGet library asks on worker
threads while a window can only be shown
on the UI thread, so each question is marshalled with `Dispatcher.UIThread.InvokeAsync` and the caller awaits
asynchronously, which means an operation started from a UI-thread command cannot deadlock against its own
dialog. Questions are serialized with a `SemaphoreSlim` gate, so if two sources need a sign-in at once the
second dialog appears after the first is answered rather than on top of it. The dialog
(`CredentialPromptWindow`, backed by `CredentialPromptViewModel`) is modal over the top-most visible window
(the Settings window, the Select Package window or the main window), shows the server that will receive the
credential prominently (scheme, host and port, a phishing-resistance cue), the source alias, a warning when
the server uses plain `http`, a notice when the last credential was rejected, the user name and a masked
password or token field, and a note that says what becomes of the credential - "Used for this session only -
AgentControl does not save it." - together with the **"Remember on this computer (saved by your Git credential
helper)"** checkbox. The checkbox is unticked by default (keeping a secret is the user's explicit decision); it is
enabled only when the question says a credential helper can keep the sign-in, and is otherwise shown disabled with
a tooltip that explains why (and is never acted on), so the option is discoverable but never a promise that cannot
be kept. Ticking it changes the note to "Saved by your Git credential helper on this computer - AgentControl does
not store it.", so the dialog always states who keeps the secret
(`AgentControl-LauncherUI-RememberSignIn`). Sign in is enabled only when both fields are filled; Cancel,
Escape and closing the window all answer "no credential". Cancelling the question's token (the operation was
cancelled or timed out) closes the dialog as a cancel. The typed password is handed to the caller and never
logged.

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
convention" decision; the toggle's checked style (`AppStyles.axaml`) colors the glyph gold from a theme
resource, so a favorited repo's star also reads as "activated" through color rather than relying solely on
the filled-vs-outline glyph shape.
`AgentToolKindDisplayNameConverter` shows each `AgentToolKind` in the Settings window's agent tool picker
under a friendly name (`GitHub Copilot CLI`, `Cursor`, `Claude Code`, `Custom command`) while the picker's
selected value, and therefore the persisted setting, stays the enum value.
`ThemePreferenceDisplayNameConverter` does the same for the Appearance picker (`System default`, `Light`,
`Dark`).

**Appearance and theme resources.** The user's theme choice (`ThemePreference`: system default, light or
dark) is a setting edited in the Settings window's Appearance picker (`ThemeComboBox`) and persisted by
`SettingsStore`. `App` maps it to an Avalonia theme variant (`ToThemeVariant`: `ThemeVariant.Default` for the
system default, which makes Avalonia follow the operating system's light/dark setting live, otherwise
`Light` or `Dark`) and applies it to the running application (`ApplyTheme`) before the main window is shown and
again whenever `MainWindowViewModel.ApplySettings` has saved new settings (through an injected
`Action<ThemePreference>`, so the view model stays free of the UI framework), so every open window changes
at once without a restart (`AgentControl-LauncherUI-Appearance`). Every color the application's own XAML uses
lives in `AppStyles.axaml` as a resource in a `Light` and a `Dark` theme dictionary (surfaces, borders,
shadows, the status accent colors, chip fills and text, button states, the avatar palette, validation,
caution and success text, the page background) consumed with `DynamicResource`; no template or setter holds a
hard-coded color. Text colors were chosen for at least 4.5:1 contrast against the fill and surface they are
drawn on, status accents and icons for 3:1, and `ThemeResourceTests` checks both dictionaries define the same
keys and meet those ratios. The same file defines the shared styles: the repo card (surface, soft shadow,
hover lift with 140 ms transitions, selection and keyboard-focus ring), the status accent bar, the avatar
tile, the chips (one height, padding and font, tinted fill with a matching foreground, a flat button wrapper
for clickable chips), the action buttons (primary accent with hover, pressed and disabled states, and a
tinted secondary), the ghost icon buttons without a border (favorite star, "more actions"), the group headers of the
card menu, and the empty state. Per-control color setters are avoided on purpose.

**Repo card layout.** Each card has a colored status accent bar on its left edge (`RepoCardViewModel.Status`:
green up to date, amber upgrade available, red a problem, grey no agent package; the bar has an accessible
name and a tooltip, and the chips state the same facts in words). The first line holds the favorite star (a
name, ghost toggle (no border, gold and filled when favorited) and the repo name a few pixels to its right, with the
actions - **Launch** (primary) and the **...** menu - on the right of the same line. Below it sit the avatar
tile, left-aligned under the star and as tall as the chip row plus the path row, beside the chips and the
path. The tile shows the repo's own `icon.png`/`logo.png` when it has one (see `RepoCardViewModel`), otherwise
the repo name's initials on a color chosen by a deterministic hash of the repo path, otherwise a folder glyph.
The **...** menu is grouped: *Repository* (**Git Pull**, with its enablement rules and its explanatory tooltip
shown even while it is disabled, and **Refresh**), *Agent files* (**Select Package...** or **Upgrade to
x.y.z**, whichever applies; the group is hidden when neither does) and **Remove from list**. The upgrade
chip is a button that runs the same `UpgradeCommand` as the menu's Upgrade item
(`AgentControl-RepoCardViewModel-UpgradeFromChip`).

**Settings window package-source actions.** One **Add** button opens a menu with the source types that can be
added (zip-package folder, NuGet-package folder, NuGet-package server), each with its type's icon (the one
the sources list uses) and a one-line description; another source type is added as one more menu item
(`AgentControl-LauncherUI-AddSourceMenu`).
The `.axaml` Views other than `MainWindow`
(`AboutWindow`, `ConfirmationWindow`, `MessageBoxWindow`, `SelectPackageWindow`,
`SettingsWindow`, `SourceEditorWindow`, `CredentialPromptWindow`) contain no logic beyond Avalonia's XAML-loading boilerplate,
`AutomationProperties.AutomationId` assignments that let FlaUI locate controls, and - for the dialogs that
return an answer or pick a folder - closing the window with the view model's answer, treating any other close
as a cancel, or opening the folder picker; all behavior
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
