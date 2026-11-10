## LauncherUI

![LauncherUI Structure](LauncherUIView.svg)

### Overview

The `LauncherUI` subsystem is the Avalonia-based user interface a developer interacts with
directly. It spans the recent-repos main window, per-repo cards, the package-selection
window, the settings window, the package-source editor window and the sign-in dialog
(`MainWindowViewModel`, `RepoCardViewModel`, `SelectPackageWindowViewModel`, `SettingsWindowViewModel`,
`SourceEditorViewModel` (one class in two files: `SourceEditorViewModel.cs` and, for a Git repository source,
`SourceEditorViewModel.Git.cs`), `CredentialPromptViewModel`), plus their `.axaml` Views (`MainWindow`,
`AboutWindow`, `ConfirmationWindow`, `MessageBoxWindow`, `SelectPackageWindow`, `SettingsWindow`,
`SourceEditorWindow`, `CredentialPromptWindow`, `GroupNameWindow`) and shared MVVM support types (`RelayCommand`,
`AsyncRelayCommand`, `ViewModelBase`, `PackageSourceIcons`, `PackageSourceItemViewModel`,
`SelectPackageSourceViewModel`, `AvaloniaCredentialPrompt`, `ConnectionTestResultKind`, `RepoCardStatus`,
`CardColorPalette`, `ThirdPartyDependencies`), the shared theme resources and styles
(`AppStyles.axaml`, included by `App.axaml`). The subsystem contains ten units: the six
view-model units `MainWindowViewModel`, `RepoCardViewModel`, `SelectPackageWindowViewModel`,
`SettingsWindowViewModel`, `SourceEditorViewModel` and `CredentialPromptViewModel`, and four further
independently testable classes with behavior of their own, each with its own requirements, design, verification
document and review set: `RepoImageLocator` (finds and decodes a repo's untrusted picture; security-relevant),
`RepoGroupHeaderViewModel` (with `RepoGroupKind`; the header row of a repo-list section), `GroupNameViewModel` (the
validating view model of the group-name prompt) and `DialogBehavior` (the keyboard behavior every dialog shares).

**Supporting types.** The remaining types are documented here, as supporting types of the subsystem, rather than as
units: the `RelayCommand`/`ViewModelBase`/`PackageSourceItemViewModel`-style MVVM plumbing and the `.axaml` Views are
declared glue with no logic of their own to verify apart from the units that bind them; `RepoCardStatus`,
`ConnectionTestResultKind` and `CardColorPalette` are small enumerations and static tables belonging to the card view
model; `AsyncRelayCommand` and `ThirdPartyDependencies` have dedicated test files
(`AsyncRelayCommandTests.cs`, `ThirdPartyDependenciesTests.cs`) because each is independent of any Avalonia control;
and the pure display helpers - the value converters `FavoriteIconConverter`, `AgentToolKindDisplayNameConverter` and
`ThemePreferenceDisplayNameConverter` (each maps one value to its display text or glyph; the last two have their own
test files) and the `PathEllipsisText` control (a presentational control that only shortens text to the width it is
given, with its own `PathEllipsisTextTests.cs`) - are exercised through the subsystem requirements that name their
tests (`AgentControl-LauncherUI-UpToDateCue`, `AgentControl-LauncherUI-Appearance`,
`AgentControl-LauncherUI-FavoriteStar`) and are reviewed with the subsystem. `PathEllipsisText` and
`ThemePreferenceDisplayNameConverter` are thus deliberately documented supporting types, not units: the SysML2
model points their design, verification and requirements at this subsystem's documents (the convention for units
without a dedicated companion document) and the subsystem requirements link their tests. `RepoGroupName` is
different: it is a unit of the `Settings` subsystem with its own requirements, design
(`docs/design/agent-control/settings/repo-group-name.md`), verification and review set, which this subsystem only
uses. The subsystem depends on the
`AgentPackageManagement` subsystem (sources, providers, failure causes) and on the `PackageCredentials`
subsystem (`PackageCredentialContext`, and the `ICredentialPrompt` that `AvaloniaCredentialPrompt` implements) and
on `SignatureVerificationSupport` (whether the signature requirement can be offered). The repo card's package chip
shows a small shield (`RequiresSignature`, `SignatureTooltip` of `RepoCardViewModel`) when the pinned NuGet source
requires signed packages, with an automation identifier `PackageSignatureIcon`
(`AgentControl-LauncherUI-SignatureIndicator`).

### Interfaces

**MainWindowViewModel.DisplayedItems**: The filtered, sectioned collection of rows shown in the main
window: the repo cards interleaved with the `RepoGroupHeaderViewModel` that starts each section.
(`MainWindowViewModel.DisplayedRepoCards` is the same cards without the headers and without the cards of
collapsed sections.)

- *Type*: In-process .NET observable collection (data-bound to `MainWindow`'s `ListBox`, which picks the
  header or the card template by row type).
- *Role*: Provider.
- *Contract*: Reflects `AddRepo`/`RemoveRepo` mutations, `FilterText` changes, grouping changes and sort
  order. With no repo in a group it is flat: favorites first, then most-recently-launched descending
  with never-launched last. Once a repo is in a group it is sectioned - Favorites (a favorite only
  there), the user's groups alphabetically, Ungrouped - with collapsible sections that a filter shows in
  full (`AgentControl-LauncherUI-RecentReposDisplay`, `AgentControl-LauncherUI-SearchFilter`,
  `AgentControl-LauncherUI-SortOrder`, `AgentControl-LauncherUI-RepoGroups`).
- *Constraints*: Recomputed synchronously on the UI thread whenever a dependency changes, updating the
  collection in place so unchanged rows keep their controls.

**RepoCardViewModel's action commands**: `LaunchCommand`, `PullCommand`,
`SelectPackageCommand`, `UpgradeCommand`, `RemoveCommand`,
`NewGroupCommand`, `RemoveFromGroupCommand`. (The favorite star has no command: its checked state is bound
two-way to `RepoCardViewModel.IsFavorite`, the only owner of that state, `AgentControl-LauncherUI-FavoriteStar`.
Because favoriting moves the card to or from the Favorites section, which re-creates its controls, `MainWindow` keeps
the focus: it records which card's star last took the focus (and by what navigation method, keyboard or pointer;
forgotten when the focus goes to any other control or the window is deactivated), and when that card's
`FavoriteChanged` is raised - after the view model has regrouped the list - it posts, at `Loaded` priority, a
request that focuses the new star of the same repo the same way (once more at `Background` priority if the row is
not created yet). A star that did not have the focus is never given it, and a card that is no longer shown (its
new section is collapsed) has no star to focus.)

- *Type*: In-process .NET `ICommand` (`AsyncRelayCommand` for the operations that read a
  package source or the file system - Launch, Pull, Upgrade, Refresh - and `RelayCommand` for the
  rest), bound to `Button`/`MenuItem` controls.
- *Role*: Provider.
- *Contract*: Each command's `CanExecute` reflects the card's current state (e.g.
  `PullCommand` is only executable when the working tree is clean,
  `AgentControl-LauncherUI-Pull`; `SelectPackageCommand` for any repo that is present and not busy - "Select
  Package..." without a pin, "Change package..." with one, `AgentControl-LauncherUI-ChangePackage`); `Execute`
  delegates to the relevant subsystem
  (`GitIntegration`, `RepoSync`, `AgentToolLauncher`, `RepoConfig`) — Launch only once agent
  files are confirmed synced (`AgentControl-LauncherUI-Launch`), Upgrade only when a newer
  version exists at the source (`AgentControl-LauncherUI-Upgrade`) — and raises a view-model
  event (`LaunchRecorded`, `SelectPackageRequested`, `ReleaseNotesReady`,
  `AgentsMdTemplateOfferRequested`, `ContentChangeConfirmationRequested`, `ErrorOccurred`, `RemoveRequested`)
  reporting the outcome.
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
definition, and tests a server's or a Git repository's connection.

- *Type*: In-process .NET `ICommand` (`TestConnectionCommand` is an `AsyncRelayCommand`).
- *Role*: Provider.
- *Contract*: `SaveCommand` is executable only while the alias, location and (for a NuGet source) package
  identifiers are valid - for a server, an `https` address ending in `index.json`, or `http` only while
  "Allow insecure HTTP" is ticked; for a Git repository, an `https`, SSH or local-path address with no credential,
  a tag pattern, an optional folder inside the repository and a package name; it raises `Saved` with the resulting
  source definition for the settings
  view model to add to or replace in its list (`AgentControl-LauncherUI-PackageSourceValidation`,
  `AgentControl-LauncherUI-AddPackageSource`, `AgentControl-LauncherUI-EditPackageSource`,
  `AgentControl-LauncherUI-GitSourceEditor`).
  `TestConnectionCommand` lists the versions of the first package ID (for a Git repository, the repository's tags,
  shown as a preview of which ones match the tag pattern), with progress and a Cancel action, and
  reports a plain-language result; it may open the sign-in dialog (`AgentControl-LauncherUI-TestConnection`).
- *Constraints*: The alias of an existing source cannot be changed. The test saves nothing.

**ICredentialPrompt.PromptAsync** (implemented by `AvaloniaCredentialPrompt`): Asks the user to sign in.

- *Type*: In-process .NET interface implementation (asynchronous), shown as a modal dialog.
- *Role*: Provider (to the `PackageCredentials` subsystem).
- *Contract*: Returns the answer the user gave in the sign-in dialog - the credential they typed and whether
  they ticked "Remember on this computer" - or no answer when the user
  cancels or the dialog is closed; canceling the supplied token closes the dialog
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
loaded `AppSettings`. It exposes `AddRepo`/`RemoveRepo` (each saved through `SettingsStore` before it takes effect, see
*Transactional persistence* below), a `FilterText` property driving `DisplayedRepoCards`/`DisplayedItems`, and
`ApplySettings`
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
under the repo path; a repo whose folder is missing offers no upgrade; the Launch button is labeled with a
short name of the configured agent tool
(falling back to plain "Launch" for a long or unknown name) and Git Pull is an item of the card's "..." menu
(`AgentControl-LauncherUI-RepoCardChips`). A lightweight `RefreshCheap` path (branch
resolution, cached committed-files badge, pin and source problem - it never reads a package
source) is kept separate from the full `Refresh` path (which also
checks working-tree cleanliness) so the main window's frequent UI refresh does not eagerly invoke a
git subprocess for every card. Removal never mutates state directly: `RemoveCommand` raises
`RemoveRequested` and `MainWindowViewModel` shows a `ConfirmationWindow`, removing the card only if
the user confirms (`AgentControl-LauncherUI-RemoveRepo`).

**How the units collaborate.** `MainWindowViewModel` is the owner and the hub. It creates one `RepoCardViewModel` per
`RecentRepo` (giving each the shared package-version cache, the credentials and a *commit* callback that is how the
card's own changes are saved, see below), subscribes to the card events that change the list (`FavoriteChanged`,
`GroupChanged`, `LaunchRecorded`) and rebuilds `DisplayedItems` when they fire; the window's code-behind subscribes to
the events that need a dialog.

- *`RepoCardViewModel` and `RepoImageLocator`*: the card is the only caller. `RefreshCheap` starts the picture
  refresh on a worker thread, which calls `RepoImageLocator.Find` for the repo's root and, when the returned
  `RepoImageFile` differs from the one already shown, decodes it (`RepoImageLocator.Decode` by default; the decoder is
  an injectable delegate so tests need no Avalonia platform). The `RepoImageFile` is the cache key, the card owns and
  disposes the `Bitmap` it publishes in `RepoImage`, and `Retire` clears it. The locator knows nothing of cards;
  any failure reaches the card as "no picture".
- *`MainWindowViewModel` and `RepoGroupHeaderViewModel`*: the main view-model creates, and keeps one instance per
  section (Favorites, each group, Ungrouped), and updates its `Count`, collapsed state and toggle availability in place
  so the header keeps keyboard focus. It subscribes to the header's four request events: `ToggleRequested` it handles
  itself (`ToggleGroupCollapsed`, saved before applied); `RenameRequested`, `RemoveGroupRequested` and
  `AddRepoRequested` it forwards, with the group's name, as its own `GroupRenameRequested`, `GroupRemoveRequested` and
  `GroupAddRepoRequested` events, to which `MainWindow` responds with a prompt, a confirmation or the folder picker and
  then calls `RenameGroup`/`RemoveGroup`/`AddRepoToGroup`. The header holds no settings
  and no reference to the main view-model.
- *Add repository to a group*: `MainWindow.AddRepoAsync(viewModel, groupName)` is the one add-repo flow, shared by the
  top-bar `AddRepoButton` (no group) and the group header menu's **Add repository...** (the header's group): it asks
  for a folder through the native folder picker (a replaceable seam, `RepoFolderPicker`, so tests answer with a path
  or a cancel), does nothing at all when the picker is cancelled, and calls `MainWindowViewModel.AddRepo` or
  `AddRepoToGroup`. Both go through the same private add (`AddRepoCore`: folder must exist, a repo already listed is
  not added twice, pin loaded, saved once, then listed), which, given a group, stores the group on the new entry before
  it is saved. For a repo that is already listed `AddRepoToGroup` instead moves the existing entry with
  `RepoCardViewModel.MoveToGroup` and the window says so in a message box (`AddRepoResult.MovedExisting`); the plain
  add keeps refusing it. A collapsed target group is expanded so the new card is visible. The favorites and ungrouped
  headers have no "..." menu, so the item is offered on the user's own groups only
  (`AgentControl-LauncherUI-GroupAddRepository`).
- *`GroupNameViewModel` and `GroupNameWindow`*: neither the card nor the main view-model creates the prompt; the
  window's code-behind does, when `RepoCardViewModel` raises `NewGroupRequested` (with the card's existing group
  names) or the header's rename is forwarded (with the group's name as the name being renamed), and shows
  `GroupNameWindow` modally. `GroupNameViewModel` validates through `RepoGroupName` (the `Settings` unit that also
  validates the card's `MoveToGroup` and repairs stored names, so the prompt, the card and the settings file agree on
  what a group name is) and raises `Completed` exactly once - with the accepted name, or `null` for Cancel - which
  closes the window; the code-behind then calls `RepoCardViewModel.MoveToGroup` or `MainWindowViewModel.RenameGroup`.
- *`DialogBehavior`*: a stateless helper that every dialog window's code-behind calls once from its constructor
  (`DialogBehavior.Apply(window, initial-focus control, cancel action)`): `SettingsWindow`, `SourceEditorWindow`,
  `CredentialPromptWindow`, `SelectPackageWindow`, `ConfirmationWindow`, `MessageBoxWindow`, `AboutWindow` and
  `GroupNameWindow`. It knows no view-model; a window that has a view-model passes its `Cancel` as the cancel
  action. The release-notes window is the only window that does not apply it.
- *Resizable `SettingsWindow` and `SourceEditorWindow`*: both are `CanResize` with a `MinWidth` of 500 and a
  `MinHeight` of 360 and no maximum width, and keep their default opening size (800 and 640 wide, `SizeToContent`
  `Height`, centered on the owner); when the user resizes them Avalonia ends the automatic height, so nothing fights
  the user's size, and the size is not remembered. Their fields (`TextBox`, `ComboBox`, the location and address
  fields, the source list) stretch with the window width, and the source list rows show more of the location
  (end-ellipsis trimming where it does not fit). Each form sits in a `ScrollViewer` (`SettingsScrollViewer`,
  `SourceEditorScrollViewer`) above a fixed row holding the error message and the Save button (Settings) or Save and
  Cancel (editor), so a window made shorter than its content scrolls the form and never clips the buttons; the editor
  still limits its own height to the screen when it opens (`AgentControl-LauncherUI-ResizableSourceDialogs`).
- *Window titles*: the windows show the product name with a space - "Agent Control" (main window and the message and
  confirmation boxes), "Agent Control Settings" and "About Agent Control". This is display text only: the product,
  executable, assembly, namespaces, settings folder and AutomationIds keep the name `AgentControl`. The Puppet and the
  UI tests that look a window up by title use the same strings.
- *Supporting types*: `CardColorPalette` is a static table built from `RecentRepo.CardColorNames` (the single list of
  valid names, owned by `Settings`); the card uses it to accept a color name and to find the tile's palette index, and
  the main window's palette pop-up draws one swatch per entry. `PathEllipsisText` is a control in the card's template
  that draws the card's `RepoPathParent` and `RepoPathLeaf` and collaborates with no other unit.
  `ThemePreferenceDisplayNameConverter` is used by the Settings window's theme picker to show a `ThemePreference`
  under a friendly name; the persisted value stays the enum member.

**Transactional persistence.** Every change the user makes in the main window is saved before it takes effect, so
memory, the list and the settings file never disagree. `MainWindowViewModel.CommitSettingsChange` tries the change on
a candidate copy of the settings (`CreatePersistedCopy`, with the repos cloned by `RecentRepo.Clone` and the collapsed
list copied), saves the candidate with `SettingsStore.Save`, and only when that succeeded applies the same change to
the live settings; then the list is refreshed (candidate copy, save, commit, refresh). A change made on a card -
`IsFavorite`, `MoveToGroup`, `SetCardColor`, and the launch timestamp - reaches this through the card's
`commitChange` callback (`MainWindowViewModel.CommitRepoChange`), which returns whether the change was saved: when it
was not, the card property keeps its value (the two-way bound star is told to read it again),
`MoveToGroup`/`SetCardColor` return `false`, and the main view-model raises `SettingsSaveFailed` with the message
for the user, which `MainWindow` shows in a message box. A collapse toggle from a header is reported the same way.
`AddRepo`, `RemoveRepo`, `ToggleGroupCollapsed`, `RenameGroup` and `RemoveGroup` instead throw
`InvalidOperationException` when the save fails, having changed nothing, and the window's code-behind shows it in a
message box. A failed save therefore leaves the live settings, the cards, the displayed list and the file as they
were, and a later unrelated save can never write the failed change. `ApplySettings` follows the same rule for the
Settings window's values: it saves them and adopts them only after that. When a repo is removed, `RemoveRepo` saves
the removal and then calls `RepoCardViewModel.Retire`: the card cancels its running operation, ignores the results of
upgrade checks and picture loads still in flight, clears all its events (so nothing it still does can show an error
box, release notes or a prompt), and disposes its picture; `MainWindow` detaches its per-card handlers when a card
leaves `RepoCards` and forgets its bookkeeping for it.

`SelectPackageWindowViewModel` backs the package-selection dialog opened from a card's **Select Package...**
(a repo with no pin) or **Change package...** (a repo with a pin). It shows three columns - source, package, version.
For Change package it is given the pin's source, package and version and, once that source has been read, selects the
package and its version (keeping the newest version when the pinned one is no longer listed, and selecting nothing
when the source failed, does not offer the package or is not configured), so choosing the version the repo already
uses again is one click; it never overrides a choice the user made while the source was loading
(`AgentControl-LauncherUI-ChangePackage`). Selecting a source loads that
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
location, and a folder-exists status computed on a background thread; a server source and a Git source on a remote
address show the neutral status "Online source" with a cloud icon - no warning styling - because they are never
contacted merely to draw the list (the tooltip says when they are contacted and points to Edit > Test connection),
while a Git source on a local path is a folder like any other; a NuGet source that requires signed packages also shows
a small "Signed only" badge (`SourceSignedOnlyBadge`, a shield icon in the neutral `Chip` style) whose tooltip says
signed packages are required and, when a publisher is pinned, that only that publisher is accepted, naming it by the
start and end of its certificate fingerprint only, as the repo card's shield does) offers one **Add** button whose
menu lists the
source types (zip-package folder, NuGet-package folder, NuGet-package server, Git repository), with Edit and Remove
(which act
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
`AgentControl-LauncherUI-InsecureHttpChoice`). For a Git repository the editor is the same window with its own
sections (see *Git repository sources* below). For a NuGet source it also
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
on the UI thread, so each question is marshaled with `Dispatcher.UIThread.InvokeAsync` and the caller awaits
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
Escape and closing the window all answer "no credential". Canceling the question's token (the operation was
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
proceeding, unlike release notes which are purely informational. The two are sequenced: the view
model raises the offer before `ReleaseNotesReady`, `MainWindow` remembers the open question per card and
`ShowReleaseNotesAsync` waits for it to be answered before opening the release-notes window and activating
it, so the question never covers the notes and the notes window - which has no owner - is not lost behind the
modal question; the notes are not opened at all when the main window has closed meanwhile.

A package version republished with different content (the same version, other bytes) - or, for a Git source, a tag
that now points to another commit - is handled the same way: when **Change package...** (or **Select Package...**)
of the version the repo is pinned to would replace its agent files with changed content,
`RepoCardViewModel` changes nothing and raises `ContentChangeConfirmationRequested` with the selection and a
plain-language question; `MainWindow` shows it in the same modal `ConfirmationWindow` (the safe answer, No, is the
default) and, only if the user answers Yes, applies the selection again with `acceptContentChange: true`, which
replaces the files and records the new identity. An Upgrade or the restore of missing agent files before launch
never asks: it refuses and reports through `ErrorOccurred`, and the message names the card menu's **Change
package...** as the way to accept the change - it no longer suggests deleting the repo's pin file
(`AgentControl-LauncherUI-ContentChangeRefusal`, `AgentControl-LauncherUI-ChangePackage`).

**Change package.** A repo that already has a pin is offered **Change package...** in the card menu's *Agent files*
group (`ChangePackageMenuItem`, shown while `HasPinnedPackage`; the group is shown whenever a package is pinned,
`HasAgentFileActions`). It is the same `SelectPackageCommand` as **Select Package...**, which is now executable for
any present, idle repo; `MainWindow` opens `SelectPackageWindow` with a view model that is given the pin's source
alias, package and version, so the dialog opens on the current choice, and applies the confirmed selection through
`ApplySelectedPackageAsync` like any other. Choosing the current version again is the user's explicit way to accept
changed content (the confirmation above); choosing another version or package pins it as usual. Requesting the
dialog never touches the pin.

**Git repository sources.** A Git source is added from the Add menu's fourth item (`AddGitSourceButton`, the Git
icon, "Packages are tags of a Git repository"). Its editor (`SourceEditorViewModel`, with the Git part in
`SourceEditorViewModel.Git.cs`) is the same window as for the other kinds, with titled sections: *Source* (alias,
immutable once the source is saved; the repository URL with examples for https, `git@host:org/repo.git` and a local
path, and a note that a credential is never accepted in it), *Package* (the package name, the alias when left empty),
*Versions* (the tag pattern, starting as `v{version}`, with live validation and help text with examples such as
`release/V{version}` and `ver{version}` and the note that the fixed parts are case-sensitive), *Location in repo*
(an optional folder, backslashes saved as slashes), *Sign-in* (a note that AgentControl uses the user's own Git
credential helper or SSH keys, never stores credentials, and that checks it runs in the background never prompt) and
the *Connection test*. The test lists the repository's tags without cloning (through `IGitTagPreview` of
`GitSourceProvider`, created by `MainWindowViewModel.CreateConnectionTestProvider` as an interactive provider that
enforces the minimum git version), with progress and Cancel, and shows a preview: "N of M tags match the pattern", the
matching tags (version and tag name, newest first, at most ten); when none matches, *No matching tags* with a few
tag names the repository does have (only names the provider found safe to show) and a hint about the prefix and
capitalization. The headlines are *Connection OK*, *No matching tags*, *Sign-in needed*, *Repository not found*,
*Repository unreachable*, *Timed out*, *Git is too old* and *Connection failed*; a result that was running when an
input changed is never shown. Save refuses a Git source whose settings the application would refuse, and
`SettingsWindowViewModel.Save` re-checks every Git source (so a hand-edited settings file is stopped with a message)
(`AgentControl-LauncherUI-GitSourceEditor`, `AgentControl-LauncherUI-GitSignInGuidance`). Everywhere a source is shown
it carries the Git icon and the name "Git repository" (`PackageSourceIcons`); `PackageSourceItemViewModel.IsRemote`
makes a remote Git source an "Online source" like a server and a Git source on a local path show its folder status
(`AgentControl-LauncherUI-GitSourcePresentation`). On a repo card the package chip shows the Git icon and the alias;
its tooltip adds the tag (the source's tag pattern applied to the pinned version) and the first seven characters of
the commit the pin recorded as its `git-commit:` identity. When the background check finds that the tag of the pinned
version now points to a different commit (`UpgradeCheck.Pinned.Revision` against that identity) the card shows the
amber **Tag moved** chip (`TagMovedChip`, `RepoCardStatus.TagMoved`, which outranks "upgrade available" and hides the
up-to-date cue); the chip's tooltip names the tag and both commits, its click only opens **Change package...**, and
nothing is ever applied by it (`AgentControl-LauncherUI-TagMovedWarning`). A git older than the supported version
makes the editor's test say *Git is too old* and the background check set the card's source problem to the same
explanation (without a sign-in hint) (`AgentControl-LauncherUI-GitTooOld`).

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
keys and meet those ratios in every control state it covers. Defining every color for both themes is the design
rule that makes the "legible in both themes" requirement (`AgentControl-LauncherUI-Appearance`) hold: no window can
be left with a color that exists in one theme only. The same file defines the shared styles: the repo card
(surface, soft shadow,
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
shown even while it is disabled, and **Refresh**), *Agent files* (**Select Package...** when no package is pinned,
**Change package...** when one is, and **Upgrade to x.y.z** when a newer version exists; the group is hidden when
none applies) and *Organize* (**Move to group** and
**Card color...**) and **Remove from list**. The upgrade
chip is a button that runs the same `UpgradeCommand` as the menu's Upgrade item
(`AgentControl-RepoCardViewModel-UpgradeFromChip`).

**Repo groups and card colors (user-specific).** Both are personal organizing choices stored only in the user's
own settings file (on the repo's `RecentRepo`, and the collapsed section names in `AppSettings`), never in the
repository or its `.agentcontrol.json` (`AgentControl-System-RepoGroups`, `AgentControl-System-CardColor`).
*Sections.* The list is a single `ListBox` over `MainWindowViewModel.DisplayedItems` with two row templates: a
repo card, and a section header - one flat button (chevron, icon, name, count chip) that collapses or expands the
section, plus, for the user's own groups, a "..." menu with **Add repository...** (first, then a separator) and
**Rename group...** and **Remove group**. The header
button's accessible name says the section, its count and its state (`Work, 2 repos, collapsed`). A decision
made here: a favorite repo is listed **only** under *Favorites* (not a second time under its group), so every
repo appears once and the counts add up; its card shows its group as a chip so the user can still see where it
belongs, and it returns to its group when it stops being a favorite. When no repo is in a group the list has no
headers at all and looks as it did before groups existed. While the filter box has text every section is shown
expanded and sections without a match disappear, so a match can never be hidden by a collapsed group; the
headers' toggles are disabled then, with a tooltip that says why.
*Move to group.* The submenu depends on the groups that exist when the menu opens, which a XAML template cannot
express as one list mixing groups and commands, so `MainWindow`'s code-behind fills it in the flyout's `Opening`
event (a check mark on the repo's own group, a separator, **New group...**, and **Remove from group** for a
grouped repo); the choices run `RepoCardViewModel.MoveToGroup` and its commands. Group names are shown in
text blocks, so an underscore is not taken as an access key. **New group...** and **Rename group...** show the
small `GroupNameWindow`, whose `GroupNameViewModel` validates while the user types (blank, longer than 40
characters, and the built-in names *Favorites* and *Ungrouped* are refused; a duplicate is refused when renaming and
joins the existing group, with a note, when creating); **Remove group** asks for a confirmation that says
nothing on disk or in any repository changes.
*Card color.* **Card color...** opens a small palette pop-up (an attached `Flyout` on the "..." button) with one
round swatch per `CardColorPalette` entry - the same ten theme-aware avatar colors the automatic color is drawn
from - plus **Automatic**. Each swatch has an AutomationId (`CardColorSwatch_<Name>`), its name as tooltip and
accessible name,
and the chosen one shows a check mark and a ring, so the choice is not carried by color alone. The color goes only to
the avatar tile (palette style classes `avatar0` ... `avatar9`, driven by bindings on the card's `AvatarStyleClass`);
the status accent bar and chips are unaffected. A repo that shows its own picture keeps the tile's light backing
so a logo drawn for a white page stays legible, and the chosen color is drawn as a ring around the picture. The
letters on every palette color use whichever of white and near-black reads better on that fill in each theme,
at least 4.5:1 (`ThemeResourceTests` checks every entry in both themes).

**Settings window package-source actions.** One **Add** button opens a menu with the source types that can be
added (zip-package folder, NuGet-package folder, NuGet-package server, Git repository), each with its type's icon
(the one
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
`ReleaseNotesReady`, `AgentsMdTemplateOfferRequested`, `ContentChangeConfirmationRequested`, `ErrorOccurred`) and
deciding which
dialog/window to show and how to route the user's response back into the view model (e.g.
calling `AcceptAgentsMdTemplate`/`DeclineAgentsMdTemplate` from the `ConfirmationWindow` shown
for the AGENTS.md template offer). `AboutWindow` (opened via a
toolbar command with no dedicated view model, as a modal dialog over the main window) shows the application
logo (reused from
`Assets/AppIcon.ico`, the same icon already used for the main window/taskbar - no new binary
image asset was added), a short mission tagline, a labeled "Version" row with the running version
(`Program.Version` without its `+` build metadata, via `AboutWindow.DisplayVersion`; the full build version is
the row's tooltip) and the copyright, and a scrollable list of this application's direct runtime third-party
dependencies and their SPDX license identifiers (package versions are intentionally omitted so
dependency updates do not require edits here), sourced from a static
`ThirdPartyDependencies` list rather than parsed at runtime from the project file
(`AgentControl-LauncherUI-About`). Adding a repo
(`AgentControl-LauncherUI-AddRepo`) is likewise driven directly by `MainWindowViewModel`
rather than a dedicated unit, since its logic (path-existence and duplicate-path validation)
is simple enough not to warrant a separate class.

**Dialog keyboard behavior.** Every dialog (Settings, the source editors, the sign-in prompt, the package
picker, the confirmations, the message boxes, the group-name prompt and About) is shown with
`ShowDialog(owner)`, so the window behind is disabled by the operating system, and applies the shared
`DialogBehavior.Apply` from its code-behind (`AgentControl-LauncherUI-DialogKeyboard`; the behavior itself is the
`DialogBehavior` unit's, see its design): Tab navigation on the
window is `Cycle`, so Tab from the last control wraps to the first and Shift+Tab back, never leaving the
dialog; the first sensible control takes the focus when the dialog opens (Settings: Add; the confirmation: the
safe answer No; the message box and About: their button; the editors and prompts: their first field), taken
with the Tab navigation method so the keyboard focus ring shows while the keyboard is in use (the ring is the
`:focus-visible` style of each control; with the mouse in use Avalonia shows none, as in every Windows
application); and Escape - handled on the way back up from the focused control, so an open drop-down keeps its
own Escape - cancels any dialog that has a way to cancel (the confirmation answers No, the message box, About
and the package picker close, the prompts and the source editor cancel). Settings has no Escape: its edits are
kept in memory until Save and would be dropped silently. Focus returns to the control that had it in the main
window when the dialog closes, because that window's focus is not changed while it is disabled. The release-notes
window is the only non-modal window left.

**Main window app bar.** The top of the main window is one compact bar (`AppBar`, inside the normal OS title bar -
the window does not extend into it): the application logo (`AppLogo`, reused `Assets/AppIcon.ico` on the same white
backing as in the About window, with the accessible name and tooltip "AgentControl" - the name and tagline are in
the window title and the About window, not repeated here), the filter box (`FilterTextBox`, the star-width column,
so it is what shrinks at narrow widths) and the `AddRepoButton`, `OpenSettingsButton` and `OpenAboutButton` icon
buttons, which are auto-width and never clip. It is drawn on the `AppBarBackground` theme brush (a little off the
page color in the dark theme, white in the light one) with a one-pixel `SurfaceDivider` line below it, instead of
a branded header and a blue rule above a separate toolbar. The keyboard focus order is the visual order (the logo is
not a stop): filter, Add, Settings, About (`AgentControl-LauncherUI-AppBar`).

**Card presentation at narrow widths.** The repo name is one `TextBlock` with an end ellipsis and the whole name
as its tooltip, in a star-width column beside the auto-width Launch and "..." buttons, so it never runs under
them. The path is a `PathEllipsisText`, a small `Decorator` that draws one `TextBlock` from
`RepoPathParent` and `RepoPathLeaf` (the folder name with its separator). It measures the real text, so the result
follows the font and the width the layout gives it: a path that fits is drawn whole; otherwise the folder part
gives way first (an end ellipsis, down to 35 % of the width) and the folder name is shown whole while it fits. A
folder name that is still too long gets a *middle* ellipsis that keeps its start and its end (binary search over
the number of kept characters, never splitting a surrogate pair), so a narrow card reads
`C:\work\cl...\05-very-long-na...-alpha` and similar repos stay apart. The control carries the whole path as its
tooltip and accessible name, and the text it draws is hidden from automation so the path is not read twice. The
package chip's up-to-date check mark
(`UpToDateCue`) and its tooltip live inside the existing chip (`AgentControl-LauncherUI-UpToDateCue`).
The card color palette's heading names the chosen color (`Card color: Teal`), in addition to each swatch's
tooltip, accessible name and check mark. The icon-only toolbar buttons and the card's "..." button have
accessible names matching their tooltips.

**Settings sections and the source editor.** Settings lists Package sources, then an Appearance section (the
Theme picker, a labeled row) and a Tools section (git executable, agent tool, shell)
(`AgentControl-LauncherUI-Appearance`). The source editor's location box wraps a long folder or feed address
over several lines (up to a maximum height, then it scrolls), has a tooltip with the whole value
(`SourceEditorViewModel.LocationTooltip`, absent while the box is empty), and the editor is 640 px wide.

`MainWindow` explicitly tracks every non-modal child window it opens (the `ReleaseNotesViewer`)
in an `_openChildWindows` list via a `ShowTrackedChildWindow(Window)` helper,
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

**Git package sources.** The card builds its package source registry with Git options
(`RepoCardViewModel.CreateSourceRegistry`):
the configured or overridden git executable, a cache folder inside the isolated configuration directory when one was
given, and `Interactive` equal to the `allowPrompt` switch that already decides whether a NuGet server may open the
sign-in dialog. The same switch therefore keeps Git quiet for the background upgrade check and the restore
before launch -
a missing sign-in is reported as a source authentication failure, which the card shows as "sign-in needed" - and lets
Git
show its own credential UI for work the user started (`AgentControl-LauncherUI-GitSourcePrompts`,
`AgentControl-System-GitSignIn`).

**Git source in the Settings editor and on the card.** The Settings window's one Add menu offers "Git repository" as
its fourth item (`AgentControl-LauncherUI-AddSourceMenu`). It opens the `SourceEditorViewModel` in Git mode: the
repository address, the package name (the alias when left empty), the tag pattern (default `v{version}`) and an optional
folder inside the repository, each explained by examples and validated while the user types with the same rules the
settings save applies, a sign-in note, and a connection test that lists the repository's tags and previews which of
them match the pattern (`AgentControl-LauncherUI-GitSourceEditor`, `AgentControl-LauncherUI-GitSignInGuidance`). The
test's provider is the owner's (`MainWindowViewModel.CreateConnectionTestProvider`): the configured git, interactive,
refusing a git that is too old. A Git source is listed with the Git icon and the kind name "Git repository": "Online
source" when its address is remote, the folder status when it is a local path; the Select Package list and the repo
card use the same icon, and the card's package chip tooltip names the tag the pinned version has under the source's
current tag pattern and the first seven characters of the commit the files came from
(`AgentControl-LauncherUI-GitSourcePresentation`).

**Tag moved and Change package.** A Git pin records the commit of the content it was set up from as a `git-commit:`
identity, not the tag. When a check finds that the tag of the pinned version now points to another commit, the card
shows an amber "Tag moved" chip, explains what happened and applies nothing; because a tag can move at any time, the
card's Refresh command, which the user starts, reads the Git source again instead of reusing the session's cached
listing, while the background check may use the cache, and a warning is cleared again whenever a later check fails or
the repo's folder is missing, since the state is then unknown (`AgentControl-LauncherUI-TagMovedWarning`). Editing a
source's tag pattern re-maps a pinned version to another tag, which is reported the same way when that tag names a
different commit. The way to accept the change is the card menu's "Change package...", which opens the Select Package
dialog on the repo's own source, package and version; choosing the pinned version again asks for confirmation before
the changed content replaces the files (`AgentControl-LauncherUI-ChangePackage`,
`AgentControl-LauncherUI-ContentChangeConfirmation`). The Select Package dialog never replaces a choice the
user made while the sources were loading with the repo's own source and package.
