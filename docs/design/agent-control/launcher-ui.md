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
`CardColorPalette`, `ThirdPartyDependencies`, `ColorPalettePanel`), the shared theme resources and styles
(`AppStyles.axaml`, included by `App.axaml`). The subsystem contains twelve units: the six
view-model units `MainWindowViewModel`, `RepoCardViewModel`, `SelectPackageWindowViewModel`,
`SettingsWindowViewModel`, `SourceEditorViewModel` and `CredentialPromptViewModel`, and six further
independently testable classes with behavior of their own, each with its own requirements, design, verification
document and review set: `RepoImageLocator` (finds and decodes a repo's untrusted picture; security-relevant),
`RepoGroupHeaderViewModel` (with `RepoGroupKind`; the header row of a repo-list section), `GroupNameViewModel` (the
validating view model of the group-name prompt), `DialogBehavior` (the keyboard behavior every dialog shares) and
`GlobalProgressViewModel` (with `GlobalProgressOperation`; the progress strip under the app bar that shows bulk work -
adding repositories, and refreshing the cards at start-up and after Settings are saved - with Cancel for an add) and
`RepoRefreshQueue` (the one queue every lazy read of a repo's state goes through - the cards' start-up reads, the
folder checks of an add, the cards' refresh after a settings save, the working-tree checks of the cards shown, and the
reads of Refresh, Pull and the card actions - so at most two run at once across all of them).

**Supporting types.** The remaining types are documented here, as supporting types of the subsystem, rather than as
units: the `RelayCommand`/`ViewModelBase`/`PackageSourceItemViewModel`-style MVVM plumbing and the `.axaml` Views are
declared glue with no logic of their own to verify apart from the units that bind them; `RepoCardStatus`,
`ConnectionTestResultKind` and `CardColorPalette` are small enumerations and static tables belonging to the card view
model; `BackgroundWorkRunner.cs` (`IBackgroundWorkRunner` and its application implementation
`ThreadPoolBackgroundWorkRunner`, a stateless `Task.Run` wrapper with one shared `Instance`) is the seam through which
`RepoRefreshQueue` runs the blocking file-system and git reads of `MainWindowViewModel` and `RepoCardViewModel` off
the UI thread; it has
no logic of its own, and the tests replace it with `GatedBackgroundWorkRunner` (a test helper that holds every piece
of work at a gate until released) to prove the callers stay free while the work runs;
`AsyncRelayCommand` and `ThirdPartyDependencies` have dedicated test files
(`AsyncRelayCommandTests.cs`, `ThirdPartyDependenciesTests.cs`) because each is independent of any Avalonia control;
and the pure display helpers - the value converters `FavoriteIconConverter`, `AgentToolKindDisplayNameConverter` and
`ThemePreferenceDisplayNameConverter` (each maps one value to its display text or glyph; the last two have their own
test files) and the `PathEllipsisText` control (a presentational control that only shortens text to the width it is
given, with its own `PathEllipsisTextTests.cs`) - are exercised through the subsystem requirements that name their
tests (`AgentControl-LauncherUI-UpToDateCue`, `AgentControl-LauncherUI-Appearance`,
`AgentControl-LauncherUI-FavoriteStar`) and are reviewed with the subsystem. The `ColorPalettePanel` control (the
content of the card and group color pop-ups, built in code like `PathEllipsisText`, with no `.axaml` file; it only
shows the palette and reports the color chosen) is likewise a supporting type, exercised through
`AgentControl-LauncherUI-GroupColor` and `AgentControl-LauncherUI-CardColor`, whose headless tests drive it.
`PathEllipsisText` and
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
- *Contract*: Reflects `EnqueueAddReposAsync`/`AddRepos`/`RemoveRepo` mutations, `FilterText` changes, grouping
  changes and repo
  order. The order is the user's own, never the launch time: with no repo in a group it is flat - favorites
  first, then the other repos in the order the user keeps them in (a new repo at the end, Move and drag reorder it).
  Once a repo is in a group it is sectioned - Favorites (a favorite only
  there), the user's groups in the user's own order (alphabetically until the user orders them), Ungrouped - with
  collapsible sections that a filter shows in
  full, each section showing its repos in that same relative order
  (`AgentControl-LauncherUI-RecentReposDisplay`, `AgentControl-LauncherUI-SearchFilter`,
  `AgentControl-LauncherUI-SortOrder`, `AgentControl-LauncherUI-StableRepoOrder`,
  `AgentControl-LauncherUI-RepoGroups`, `AgentControl-LauncherUI-GroupOrder`).
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
  (`GitIntegration`, `RepoSync`, `AgentToolLauncher`, `RepoConfig`) — Launch only after the agent files were
  verified against the pin (`AgentControl-LauncherUI-VerifyAgentFilesBeforeLaunch`) and not when the user cancelled it
  after a failed restore (`AgentControl-LauncherUI-RestoreFailureChoice`, `AgentControl-LauncherUI-Launch`), Upgrade
  only when a newer
  version exists at a configured source (`AgentControl-LauncherUI-Upgrade`) — and raises a view-model
  event (`LaunchRecorded`, `SelectPackageRequested`, `ReleaseNotesReady`,
  `AgentsMdTemplateOfferRequested`, `ContentChangeConfirmationRequested`, `CommittedFilesConversionRequested`,
  `AgentFilesChoiceRequested`, `ErrorOccurred`, `RemoveRequested`) reporting the outcome.
  An asynchronous command ignores a second request while it runs and can be cancelled through
  `CancelOperationCommand` (`AgentControl-LauncherUI-OneOperationPerRepo`,
  `AgentControl-LauncherUI-CancelOperation`, `AgentControl-LauncherUI-OperationProgress`). The card also
  exposes the chip properties (package, upgrade, branch, status) the view binds to
  (`AgentControl-LauncherUI-RepoCardChips`, `AgentControl-LauncherUI-StatusBadges`). `RefreshCommand` is bound
  both to the card menu's Refresh item and to the "Sign-in needed" chip button
  (`AgentControl-LauncherUI-SignInNeededChipSignsIn`); when the sign-in that check asks for is canceled, closed or
  rejected, the card keeps the same chip and tooltip rather than a failed check
  (`AgentControl-LauncherUI-SignInNotGivenKeepsChip`).
- *Constraints*: Never mutates state before a required confirmation (see `RemoveRequest`
  below); never replaces agent files that differ from the pin without the user choosing to update them, and never
  starts the agent tool after the user cancelled a launch whose needed agent files could not be installed.

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
  edited list of package sources, waits for its task without blocking the window (the command is unavailable until
  then), and raises `Saved` once it completed, letting
  `MainWindowViewModel.ApplySettingsAsync` persist the change in the background and invalidate the cached package
  data of every edited or removed source (`AgentControl-LauncherUI-Settings`,
  `AgentControl-LauncherUI-SettingsApplyResponsive`). Whenever the Settings window
  closes, saved or not, `MainWindow` calls `MainWindowViewModel.RecheckAfterSettingsClosed(settingsSaved)` with
  whether `Saved` was raised, which re-checks the cards that show a source problem or a failed upgrade check
  when nothing was saved and starts nothing after a save (`AgentControl-LauncherUI-SettingsClosedRechecksProblems`).
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

**Global progress strip** (`MainWindowViewModel.GlobalProgress`, a `GlobalProgressViewModel`): the strip directly under
the app bar that shows bulk work not tied to one card - adding repositories, and refreshing the cards at start-up and
after Settings are saved.

- *Type*: In-process .NET bindable properties and an `ICommand`, bound by `MainWindow.axaml` to a `Border`
  (AutomationId `GlobalProgressStrip`, visible only while `IsActive`), a 3-pixel `ProgressBar` (`GlobalProgressBar`,
  indeterminate or with `Value` of `Maximum`, its accessible name the status text), a `TextBlock`
  (`GlobalProgressText`) and a Cancel `Button` (`GlobalProgressCancelButton`, accessible name "Cancel adding
  repositories", visible only while `CanCancel`).
- *Role*: Provider.
- *Contract*: Shows the most recently begun running bulk operation that can still be canceled or, when none can, the
  most recently begun running one: "Adding repositories..." while the
  folders of an add are counted, "Adding {completed} of {total} repositories..." as each folder has been checked,
  "Saving..." while the add saves (no Cancel then), and "Refreshing repositories {completed} of {total}..." while the
  shared refresh queue reads the cards at start-up or after a settings save (no Cancel). A single card's Refresh,
  Pull or action shows on its card, not in the strip. Cancel stops the running add and every queued
  add before anything is saved or added, with no message afterwards; an add stays shown, with its Cancel, while the
  cards refresh after a settings save, and the refresh is shown once the add has ended or was canceled
  (`AgentControl-LauncherUI-GlobalProgress`, `AgentControl-LauncherUI-AddCancel`).
- *Constraints*: Fully collapsed when idle, so the idle window is unchanged; the strip, its text and its bar use
  the theme brushes `GlobalProgressStripBackground`, `GlobalProgressTextForeground`, `GlobalProgressForeground` and
  `GlobalProgressTrack`, defined for both themes and checked for contrast.

**MainWindowViewModel.AddSaveFailed** (with `SettingsSaveRetryRequest`): asks the view what to do when the settings
could not be saved at the end of a background add.

- *Type*: In-process .NET event whose argument carries the message and a `Task<bool>` answer.
- *Role*: Provider (the view answers).
- *Contract*: `MainWindow` shows the message - what failed, why (secrets redacted) and what Retry and Discard do - in
  the modal `ConfirmationWindow` with the buttons **Retry** and **Discard** and answers with
  `SettingsSaveRetryRequest.Respond`: Retry saves the add again, Discard gives it up and leaves the list as saved
  (`AgentControl-LauncherUI-AddSaveFailureChoice`). The first answer wins.
- *Constraints*: Discard is the focused default, and Escape, closing the dialog, a failure to show it, or no
  subscriber at all count as Discard, so nothing is retried without the user asking and the add never waits for
  ever.

### Design

`MainWindowViewModel` owns the `ObservableCollection<RepoCardViewModel>` backing the
recent-repos list; on construction it creates one `RepoCardViewModel` per `RecentRepo` in the
loaded `AppSettings`. It exposes `EnqueueAddReposAsync` (the background, queued add the window uses), `AddRepos` (the
same add, synchronous, and the single-folder `AddRepo`/`AddRepoToGroup`, each saved before it takes effect) and
`RemoveRepo` (applied at once and saved in the background, see *Background persistence* below), a `FilterText`
property driving `DisplayedRepoCards`/`DisplayedItems`, and
`ApplySettingsAsync`
(the Settings window's `onSave`), which persists the new settings in the background
while preserving the existing recent-repos list (and its order), the collapsed sections and the group colors and order,
and invalidates the shared
`PackageVersionCache` for each package source that was edited or removed.

Each `RepoCardViewModel` is independently responsible for one repo's status and actions: it reads
the pin via `RepoPinStore`, resolves which of the user's configured sources supplies the pinned package (asking
them through `PackageSourceRegistry`/`PackageVersionCache`, matching by the files identity the pin recorded - a pin
names no source - and showing an error state, never using a source whose files are not the recorded ones, when none
does: `AgentControl-LauncherUI-SourceAwareActions`), resolves
git status/branch via `GitClient`/`CommittedAgentFilesCache`, and drives
Launch/Pull/Upgrade/Select-Package/Favorite/Remove. The card presents its state as chips: a package
chip (robot icon, package name and version, the alias of the source that currently supplies the files as muted text
beside a small icon for the
source kind - a server has its own network-server icon - and a tooltip naming the source; hidden alias when no source
is known), an amber
upgrade chip reading "Upgrade to" and the version an upgrade would move to (also shown beside a red source problem),
a grey "No agent
package" chip when nothing is pinned, a branch chip, and color-coded status chips (uncommitted
changes, committed agent files, folder missing, and a red chip for a source problem with the actual problem in
its tooltip - "Sign-in needed" when the server needs a sign-in, otherwise "Package source problem"
(`SourceProblemChipText`)). The "Sign-in needed" chip (`SourceNeedsSignIn`) is a chip button bound to the
card's `RefreshCommand`: clicking it starts the same user-started check as the menu's Refresh, which may ask
for the sign-in, under the same busy guard, and its tooltip says to click it or use Refresh; every other source
problem keeps the tooltip-only red chip (`ShowSourceProblemChip`), and the two are never shown together. Like
the other chip buttons it shows hover and pressed tints (`ChipRedBackgroundHover`/`ChipRedBackgroundPressed`/
`ChipRedBorderHover` in both theme palettes) (`AgentControl-LauncherUI-SignInNeededChipSignsIn`); the
outcome of the last Launch/Pull/Upgrade action (`StatusMessage`) is shown
under the repo path; a repo whose folder is missing offers no upgrade; the Launch button is labeled with a
short name of the configured agent tool
(falling back to plain "Launch" for a long or unknown name) and Git Pull is an item of the card's "..." menu
(`AgentControl-LauncherUI-RepoCardChips`). A lightweight read of the card's *cheap facts* (folder, pin, branch,
cached committed-files badge and source problem - it never reads a package source) is kept separate from the
working-tree check (`git status`, which scans the whole tree), and both run through the shared refresh queue off the
UI thread (see *Background work and the global progress strip*); only the full `Refresh` reads both and then checks
the package sources. Removal never mutates state directly: `RemoveCommand` raises
`RemoveRequested` and `MainWindowViewModel` shows a `ConfirmationWindow`, removing the card only if
the user confirms (`AgentControl-LauncherUI-RemoveRepo`).

**How the units collaborate.** `MainWindowViewModel` is the owner and the hub. It creates one `RepoCardViewModel` per
`RecentRepo` (giving each the shared package-version cache, the credentials and a *commit* callback that is how the
card's own changes are saved, see below), subscribes to the card events that change the list (`FavoriteChanged`,
`GroupChanged`) and rebuilds `DisplayedItems` when they fire (it deliberately does not subscribe to `LaunchRecorded`: a
launch never moves a card); the window's code-behind subscribes to
the events that need a dialog.

- *`RepoCardViewModel` and `RepoImageLocator`*: the card is the only caller. Applying the card's cheap facts starts the
  picture refresh on a worker thread, which calls `RepoImageLocator.Find` for the repo's root and, when the returned
  `RepoImageFile` differs from the one already shown, decodes it (`RepoImageLocator.Decode` by default; the decoder is
  an injectable delegate so tests need no Avalonia platform). The `RepoImageFile` is the cache key, the card owns and
  disposes the `Bitmap` it publishes in `RepoImage`, and `Retire` clears it. The locator knows nothing of cards; any
  failure reaches the card as "no picture".
- *`MainWindowViewModel` and `RepoGroupHeaderViewModel`*: the main view-model creates, and keeps one instance per
  section (Favorites, each group, Ungrouped), and updates its `Count`, collapsed state, toggle availability, group
  color (`UpdateColor`) and move availability (`UpdateOrder`) in place
  so the header keeps keyboard focus. It subscribes to the header's five request events: `ToggleRequested` it handles
  itself (`ToggleGroupCollapsed`, saved before applied), and `MoveRequested` too (`MoveGroup`, which reports a failed
  save through `SettingsSaveFailed`); `RenameRequested`, `RemoveGroupRequested` and
  `AddRepoRequested` it forwards, with the group's name, as its own `GroupRenameRequested`, `GroupRemoveRequested` and
  `GroupAddRepoRequested` events, to which `MainWindow` responds with a prompt, a confirmation or the folder picker and
  then calls `RenameGroup`/`RemoveGroup`/`EnqueueAddReposAsync`. The header's **Group color...** item needs no
  request: the window
  opens the group palette and calls `MainWindowViewModel.SetGroupColor` with the color chosen. The header holds no
  settings and no reference to the main view-model.
- *Add repositories*: `MainWindow.AddRepoAsync(viewModel, groupName)` is the one add-repo flow, shared by the
  top-bar `AddRepoButton` (no group) and the group header menu's **Add repository...** (the header's group): it asks
  for folders through the native folder picker with several folders allowed (`AllowMultiple`; a replaceable seam,
  `RepoFolderPicker`, a `Func<Task<IReadOnlyList<string>>>` that tests answer with paths, or with an empty list for a
  cancel), does nothing at all when the picker is cancelled, calls `MainWindowViewModel.EnqueueAddReposAsync` with the
  paths and the group, and - unless the add was canceled - shows the text of
  `MainWindowViewModel.DescribeAddOutcomes`, if any, in a message box
  (`AgentControl-LauncherUI-AddSeveralRepos`, `-AddRepoSummary`). Every folder goes through the same checks (the path
  must be a folder, a repo already listed is not added twice, the folder must be the top
  folder of a Git repository - `GitClient.IsRepositoryRoot`, a `.git` folder or file directly in it - pin loaded), and
  the batch is saved once and then listed; given a group, the group is stored on the new entries before they are saved
  (`AgentControl-LauncherUI-AddRepoGitRootOnly`). For a repo that is already listed the add into a group instead moves
  the existing entry into the group, at the bottom of its section, in the same save, and the message says so
  (`AddRepoResult.MovedExisting`); the plain add keeps refusing it. A collapsed target group is expanded in the same
  save so the new cards are visible. The favorites
  and ungrouped headers have no "..." menu, so the item is offered on the user's own groups only
  (`AgentControl-LauncherUI-GroupAddRepository`). Folders dropped from the file manager use the same view-model calls
  (see *Adding repos by dropping folders*); how the add stays off the UI thread is described under *Background work
  and the global progress strip*.
- *`MainWindowViewModel` and `GlobalProgressViewModel`*: the main view-model creates one `GlobalProgressViewModel`
  (`GlobalProgress`) and is its only caller: `Begin` for a session of adds (with `CancelAddRepos` as the cancel action)
  and for each wave of counted reads of the shared refresh queue (no cancel action), reporting through the returned
  `GlobalProgressOperation` and disposing it when the work ends.
- *`MainWindowViewModel`, `RepoCardViewModel` and `RepoRefreshQueue`*: the main view-model creates one
  `RepoRefreshQueue` on the UI thread (so the queue posts its progress there), hands it to every card it creates,
  queues the folder checks of its adds on it, turns its progress into the strip's "Refreshing repositories ..." and
  disposes it when the window closes. A card queues its own cheap facts and working-tree checks on it
  (`RefreshRepoStateAsync`), raises their priority when it is shown (`PrioritizeRepoState`) and gives up its waiting
  reads when it is retired. The window binds the strip to it and its Cancel
  button to `CancelCommand`; the progress view model knows nothing of repos.
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
  executable, assembly, namespaces, settings folder and AutomationIds keep the name `AgentControl`. The UI tests that
  look a window up by title use the same strings.
- *Supporting types*: `CardColorPalette` is a static table built from `RecentRepo.CardColorNames` (the single list of
  valid names, owned by `Settings`); the card uses it to accept a color name and to find the tile's palette index, the
  group header to find its accent's index, and `ColorPalettePanel` draws one swatch per entry. `PathEllipsisText` is a
  control in the card's template
  that draws the card's `RepoPathParent` and `RepoPathLeaf` and collaborates with no other unit.
  `ThemePreferenceDisplayNameConverter` is used by the Settings window's theme picker to show a `ThemePreference`
  under a friendly name; the persisted value stays the enum member.

**Background persistence.** Every change the user makes in the main window is applied at once and saved in the
background, so the window never waits for the settings file, which another program (a virus scanner, an indexer, a
sync client) can hold for up to a second (`AgentControl-LauncherUI-ChangeAppliedWhenSaved`).
`MainWindowViewModel.CommitSettingsChange` applies the change to the live settings, runs
`AppSettings.RepairGroupSettings` (which drops the color and position of a group that no repo is in any more,
`AgentControl-LauncherUI-GroupSettingsKeptInStep`), and hands a snapshot of the live settings
(`AppSettings.CreateSnapshot`) to the `Settings` subsystem's `SettingsWriter`, which writes the newest snapshot on a
worker thread a moment after the last change, one write at a time; then the list is refreshed. A change made on a
card - `IsFavorite`, `MoveToGroup`, `SetCardColor`, and the launch timestamp - reaches this through the card's
`commitChange` callback (`MainWindowViewModel.CommitRepoChange`), which applies it and returns `true`. When a write
fails, the change stays in effect - the settings in memory, the cards and the list - and is written with the next
change or when the window closes (`AgentControl-LauncherUI-FailedSaveChangesNothing`); the writer reports the first
failure of a streak, and the main view-model raises `SettingsSaveFailed` with a message saying why and that the
changes stay in effect, which `MainWindow` shows in a message box it does not wait for - once per failure streak, not
for every click (`AgentControl-LauncherUI-FailedSaveReported`). The changes that must be on disk before they are
applied - an add, and the Settings window's values (`ApplySettingsAsync`) - are confirmed saves of the same writer,
made on a worker thread: they are applied only once written, and a failure changes nothing and reaches their caller -
`AddRepos` reports every folder that would have been added or moved as not saved, the background add asks the user
through `AddSaveFailed` whether to retry or discard (see *Background work and the global progress strip*), and the
Settings window stays open with the reason. A change made in the window while a confirmed save runs is applied at once
and written after it, together with it, so the file never loses either. When the window closes,
`MainWindowViewModel.Dispose` writes what is not written yet, on a worker thread, waiting at most five seconds; a
change made while an add was still saving at the close is written together with the add once the add's write
succeeded, so the close never takes the add out of the file again. When a
repo is removed, `RemoveRepo` applies and schedules the removal and then calls `RepoCardViewModel.Retire`: the card
cancels its running operation, ignores the results of upgrade checks and picture loads still in flight, clears all its
events (so nothing it still does can show an error box, release notes or a prompt), and disposes its picture;
`MainWindow` detaches its per-card handlers when a card leaves `RepoCards` and forgets its bookkeeping for it.

**Background work and the global progress strip.** Reading the cards at start-up, adding folders, refreshing the cards
after a settings save and checking the working tree of the cards read the disk and run git for every repo; done on the
UI thread for many repos they froze the window (`AgentControl-System-ResponsiveRepoListWork`). The rule is: blocking
reads run off the UI thread through the shared `RepoRefreshQueue` (owned by `MainWindowViewModel` and handed to every
`RepoCardViewModel`), which runs at most two of them at a time through `IBackgroundWorkRunner`
(`ThreadPoolBackgroundWorkRunner.Instance` in the application), they only read and return immutable records
(`RepoFolderFacts`, `RepoCheapFacts`, a working-tree answer), and every change to view-model state, the settings and the
list happens on the UI thread in the continuation of the `await` (which resumes on Avalonia's synchronization context).
The view models never touch the dispatcher and need no locking; the only shared mutable object a worker thread touches,
the per-card `CommittedAgentFilesCache`, is lock-guarded (`AgentControl-GitIntegration-QueriesFromAnyThread`).

- *One shared queue* (`AgentControl-LauncherUI-BoundedRepoReads`): every lazy repo read - the cards' startup reads, an
  add's folder checks, the cards' refresh after a settings save, the working-tree check of a card shown, a re-added
  repo's facts, the repo state of a Refresh (and of the "Sign-in needed" chip), of a Pull and after an untrack, and the
  read an action waits for - goes through `RepoRefreshQueue` instead of starting its own tasks, so
  the number of git processes is independent of the number of repos and callers. The limit is two: one read is at
  most one git process; two keep the work moving when one repo is slow (a network share, a huge tree, a git that hangs
  until its time limit), more would only compete for the same disk. A read already waiting for the same repo and kind
  is joined, not queued again; a user action goes before a card on screen, which goes before background work; a card
  shows that it is refreshing (`RepoCardViewModel.IsRefreshing`) while one of its reads waits or runs. The total
  process budget without a user action is the queue's two reads plus the upgrade checks' one shared limit of four
  source reads (each at most one git process for a Git source; see *Upgrade checks* below); a user action adds at most
  one process per busy card.
- *Start-up* (`AgentControl-LauncherUI-StartupCardsAtOnce`): the `MainWindowViewModel` constructor builds the cards from
  the settings alone - no disk access and no git - so the window appears at once; the pinned package the settings
  remember is shown until the pin file is read (even that read can take tens of seconds on an offline share). The
  constructor then queues every card's cheap facts at background priority, in display order (the cards shown, top first,
  then the collapsed and filtered-out ones), counted in the progress wave, so the strip shows "Refreshing repositories
  {completed} of {total}..." without Cancel. A card the window realizes moves its read ahead of the background ones
  (`Visible`), and its working-tree check follows; the list only realizes the rows it shows, so "visible first" comes
  from the realized rows and the display order without tracking the scroll position. The only work left before the
  window exists is loading the settings file (with a one-time order seeding save), creating the log folder and the
  credential profile check: one small local file or folder each, needed to build the window.
- *The card's refreshing state* (`AgentControl-LauncherUI-RefreshingCardState`): until a card's cheap facts were first
  applied (`RepoCardViewModel.HasRepoState`), the card knows nothing git-derived, so it does not show it as a fact: its
  status is `Refreshing` (grey accent, "Refreshing" for screen readers), its branch chip shows a dimmed `...`
  (`ShowBranchChip`, `BranchChipText`, `IsBranchUnknown` with the `Chip.unknown` style), the "No agent package" chip
  and the committed-files warning are hidden, and the working tree is "not checked yet". The italic "refreshing..."
  indicator (`RepoRefreshingText`, next to "checking for updates...") is shown while any of the card's reads waits or
  runs. A later re-read keeps the known values with the indicator next to them, so the accent does not flicker.
- *Actions wait for the card's state* (`AgentControl-LauncherUI-ActionWaitsForCardState`): Launch, Pull, Upgrade and
  Select Package (the apply) take the card's busy row first, then promote the card's waiting reads to the front of the
  queue and await them (`EnsureCurrentRepoStateAsync`; a card whose state was never read reads it now), and decide
  again on what was found: a missing folder, a working tree git now reports dirty (Pull) or a pin other than the one
  shown (Upgrade) stops the action with a status message. A card whose state is known and has nothing pending acts at
  once, with no git round trip of its own. This was chosen over re-reading on every action (an extra git call per
  click for state that is current) and over refusing the action until the state is known (the user would have to
  click again): the window is never blocked, the wait is bounded by the reads already running and git's time limit,
  and no action uses unknown or stale-pending data. The launch-time committed-files decision and the pre-launch sync
  therefore see the card's fresh state. Because the card is busy meanwhile, the background upgrade checks skip it.
- *Refresh and Pull through the queue* (`AgentControl-LauncherUI-RefreshThroughQueue`): a card's Refresh (the menu
  item and the "Sign-in needed" chip) reads its cheap facts and working tree through the queue at `UserAction`
  priority - ahead of background work, joined with a read of the card that already waits - and only then checks the
  package sources (allowing the sign-in prompt); a missing folder reads its cheap facts first, so a folder that came
  back is also checked for its working tree. The refresh after a Pull (and the working-tree check after a canceled
  one) and the re-read of git's answers after the card untracked agent folders go through the queue the same way,
  while the card is still busy. No git runs on the UI thread for any of them.
- *Upgrade checks*: every background upgrade check (start-up, added repos, after Settings, the periodic round) waits
  for the card's pending cheap-facts read before it takes a slot, because applying cheap facts makes a check in flight
  irrelevant (it was started against the old pin); the slots are one `SemaphoreSlim` of four shared by all of these
  checks, so overlapping checks do not multiply the source reads. The busy-card skip is unchanged: whether a card is
  busy is read right before its check starts, with no await in between; the periodic round and the re-check after
  Settings also skip a card whose state was never read.
- *Regression guard*: `BlockingCallGuard` is switched on for the headless test session's UI thread
  (`HeadlessTestApp`), so a git command (`GitClient`) made on the UI thread fails the test that made it; the
  synchronous `RefreshCheap`/`RefreshDirtyStatus` remain only for callers off the UI thread (tests), and the
  synchronous test-only adds (`AddRepo`, `AddRepoToGroup`, `AddRepos`) check only the folder and leave the card's
  state to the queue.
- *A git that hangs* (`AgentControl-LauncherUI-GitTimeoutShown`): every git command runs under a time limit
  (`GitClient`, 60 seconds for the local repository); a command past it is stopped and the card's status message says
  that git did not answer and was stopped, that the repo's Git state is unknown and that Refresh tries again, until a
  later read gets an answer. Closing the window disposes the queue, which stops the git that is still running.

- *The add pipeline* (`AgentControl-LauncherUI-AddReposResponsive`): `EnqueueAddReposAsync` validates the group name
  at once, then runs `AddReposAsync`: (1) **plan** on the UI thread - blank and repeated paths dropped, which folders
  are already listed noted, the git executable resolved; (2) **inspect** each folder through the shared queue (kind
  `FolderInspection`, priority `Visible`, the add's cancellation as the requester's; `InspectFolder`: exists, a file,
  the top folder of a Git repository and, for a Git root not yet listed,
  `RepoCardViewModel.ReadCheapFacts` - its pin, branch, `HEAD` and committed agent folders), the strip counting the
  folders checked; (3) **commit** on the UI thread (`PrepareAddCommit` then `ApplyAddCommit`): every folder is checked
  again against the list as it is now - a repo listed meanwhile is not added twice, a repo removed meanwhile is added
  again (its card's facts read in the background), a target group removed meanwhile
  is created again by the add, because the user asked for that group - and one deterministic settings change appends
  the new entries, moves listed repos into the group at the bottom of their section and opens a collapsed target
  group; it is saved once through the transactional change, and only then are the new cards created from the facts
  already read (no git call on the UI thread), put in the stored order and the list rebuilt once; the new cards'
  upgrade checks start in the background. The working tree of a new repo is not checked by the add; its card shows
  its Git status as not checked yet until the card is first shown (see below). The other list operations (filter,
  collapse, color, reorder, remove, rename) stay enabled during an add, because the commit is atomic and re-checks
  the live state.
- *Queue and Cancel* (`AgentControl-LauncherUI-AddQueued`, `AgentControl-LauncherUI-AddCancel`): each request is
  chained after the previous one (`PendingAddRepos`), so adds never run at the same time. Requests made while adds
  run join one session with one progress operation and one `CancellationTokenSource`; Cancel (the strip's button,
  `CancelAddRepos`) cancels the running add and every queued add of the session before they commit, and nothing is
  saved, added or reported. A request made after a cancel starts a new session. Once an add is saving, Cancel is
  hidden ("Saving..."), and the save completes. Closing the window cancels the adds that have not saved.
- *A failed save* (`AgentControl-LauncherUI-AddSaveFailureChoice`): the commit raises `AddSaveFailed` with a
  `SettingsSaveRetryRequest` and waits for its answer; Retry prepares the commit again against the list as it is then
  and saves again; Discard (or no subscriber, a closed window, Escape) reports the folders that would have been added
  or moved as not saved; Retry saves again and asks again each time that save fails. Because the add is applied only
  once saved, nothing has to be undone: memory, the cards and the list are as they were before the add, including the
  group, place and collapsed state of repos the add would have moved.
- *Settings refresh* (`AgentControl-LauncherUI-SettingsApplyResponsive`): `ApplySettingsAsync` saves the new settings
  with a confirmed save of the settings writer on a worker thread (the Settings window's Save button is unavailable
  meanwhile and the window stays usable) and adopts them once saved, then cancels the upgrade check and periodic round
  that were running and starts
  `RefreshCardsAfterSettingsAsync` as `PendingRefresh`: each card's cheap facts through the shared queue at background
  priority, the cards shown first (`RefreshRepoStateAsync` with `reportsProgress`), and, for a card whose folder came
  back or every present card when the git executable changed, its working tree, with "Refreshing repositories
  {completed} of {total}..." in the strip (the queue's progress wave) and no Cancel; then one list rebuild and the
  upgrade check against the new sources. A newer settings save supersedes an
  older refresh, which then neither rebuilds nor starts the upgrade check.
- *Working-tree checks* (`AgentControl-LauncherUI-GitStatusInBackground`): the first time a card's row is attached
  to the visual tree (priority `Visible`, and the card's other waiting reads are promoted), after a Refresh and after
  a Pull (also a canceled one), the card runs `git status` through the shared queue (`RefreshRepoStateAsync`); until it
  answers the card keeps its previous state, and a card never
  checked shows its Git status as not checked yet, never as having uncommitted changes.
- *Package work* (`AgentControl-LauncherUI-PackageWorkNeverBlocks`): a card's actions never wait for the disk, a process
  or a package hash on the UI thread. The sources the cards and the Select Package dialog ask come from
  `PackageSourceRegistry.Create`, whose providers run on the thread pool and hand over packages whose identity is
  already computed; the card computes the identity on a worker itself for a source that does not. The pin reads and
  writes, the `.gitignore` and release notes of an apply, the managed-folder check before a launch, the AGENTS.md
  check and answer, and the shell detection and start of the agent tool run through the card's background-work runner
  (see the `RepoCardViewModel` design). The source editor runs its connection test, its "Forget saved sign-in" and its
  credential-helper question on workers and asks that question only once the typed address has stayed unchanged for a
  short delay (see the `SourceEditorViewModel` design). The headless tests guard the UI thread, so a pin read, a hash,
  an extraction or a git start that slips back onto it fails them.
- *The strip*: `GlobalProgressViewModel` (see its design) shows one operation at a time - the most recently begun
  running one that can still be canceled, otherwise the most recently begun running one - so an add started while the
  cards refresh is shown, and the refresh again when the add ends; and a refresh that starts during an add (after a
  settings save) does not take the add's Cancel away: the refresh is shown once the add has ended or was canceled.

`SelectPackageWindowViewModel` backs the package-selection dialog opened from a card's **Select Package...** (a repo
with no pin) or **Change package...** (a repo with a pin). It shows three columns - source, package, version. For Change
package it is given the source that supplies the repo's files, the package and the version and, once that source has
been read, selects the package and its version (keeping the newest version when the pinned one is no longer listed, and
selecting nothing when the source failed, does not offer the package or is not configured), so choosing the version the
repo already uses again is one click; it never overrides a choice the user made while the source was loading
(`AgentControl-LauncherUI-ChangePackage`). Selecting a source loads that source's package names from
`PackageVersionCache` in the background (the dialog first invalidates every source, so it shows what the sources offer
now, and reuses what it fetched while it stays open), with a progress indicator and cancellation; a source that cannot
be read shows its error inline, with a **Retry** button that reads only that source (or, when only a package's versions
failed, only those) again, and never blocks the other sources (`AgentControl-LauncherUI-ResponsiveOperations`,
`AgentControl-LauncherUI-SourceFailureIsolation`, `AgentControl-LauncherUI-RetryFailedRead`). A source whose folder is
missing is explained in words, naming the folder; until a package is selected the version column says "Select a package
to see its versions." (`ShowVersionsHint`); each source row has a UI Automation name made of its alias, kind and status
(`AutomationName`). Selecting a package lists its versions in descending order (defaulting to latest), and confirming
reports the full package reference - source alias, name and version - from which the card applies the package and
records only its name, version and files identity (the source it was selected from is remembered as the card's supplier
in the user's own settings, not in the pin). The three-column layout was chosen over a grouped single list because the
same package name can exist in several sources and the user must see which source they pick from.
`SettingsWindowViewModel` seeds its editable properties from the current `AppSettings` on construction and exposes
`IsCustomAgentToolSelected` to drive the settings window's custom-command entry field visibility. It lists the
configured package sources as `PackageSourceItemViewModel` rows (alias, kind icon from `PackageSourceIcons`, shortened
location, and a folder-exists status computed on a background thread; a server source and a Git source on a remote
address show the neutral status "Online source" with a cloud icon - no warning styling - because they are never
contacted merely to draw the list (the tooltip says when they are contacted and points to Edit > Test connection), while
a Git source on a local path is a folder like any other; a NuGet source that requires signed packages also shows a small
"Signed only" badge (`SourceSignedOnlyBadge`, a shield icon in the neutral `Chip` style) whose tooltip says signed
packages are required and, when a publisher is pinned, that only that publisher is accepted, naming it by the start and
end of its certificate fingerprint only, as the repo card's shield does) offers one **Add** button whose menu lists the
source types (zip-package folder, NuGet-package folder, NuGet-package server, Git repository), with Edit and Remove
(which act on the selected row) beside it on the list's header line, and also exposes the **Theme** choice (system
default, light or dark) saved with the other settings, and asks the window to open a `SourceEditorViewModel` dialog for
adding and editing; removal asks for confirmation and warns that repos that get their agent package from the source will
use another configured source that delivers the identical files, or show a source problem if there is none.
`SourceEditorViewModel` validates the alias (required, unique ignoring case, fixed once the source exists), the location
(absolute folder path, no embedded credentials; for a server an `https` address ending in `index.json`, or `http` only
after an explicit "Allow insecure HTTP" tick, whose warning is shown while it is on) and, for a NuGet source, a
non-empty list of package identifiers, reusing the `PackageSourceDefinition` validation of `AgentPackageManagement`; its
Save command stays disabled while anything is invalid. For a server it also offers **Test connection**, which lists the
versions of the first package ID with progress and Cancel, and states the result in words (*Connection OK* with the
version count and newest; *Sign-in failed*; *Access denied*; *Sign-in canceled*; *Package not found*; *Timed out*;
*Server unreachable*; *Connection failed*), and shows example addresses for JFrog Artifactory and Azure Artifacts
(`AgentControl-LauncherUI-TestConnection`, `AgentControl-LauncherUI-ServerSourceEditor`,
`AgentControl-LauncherUI-InsecureHttpChoice`). For a Git repository the editor is the same window with its own sections
(see *Git repository sources* below). For a NuGet source it also offers the **Package signatures** section: a "Require
signed packages" checkbox (off by default; disabled, with its tooltip shown even though it is disabled, where
`SignatureVerificationSupport` says signatures cannot be verified) with help text that explains the limit of requiring
signatures without a pinned publisher, and a "Trusted signer certificate fingerprint (SHA-256)" field that is enabled
only while signatures are required, accepts the fingerprint as pasted and explains how to find it. A server's editor
also has **Forget saved sign-in**: the view shows a `ConfirmationWindow` that names the server, and only on "Yes" calls
the view model, which reports the outcome in words below the button; the button is disabled, with a tooltip saying why,
when no credential helper can be reached from the window. `SettingsWindowViewModel` passes the application's
`PackageCredentialContext` to the editor for that purpose (`AgentControl-LauncherUI-SignatureOptions`,
`AgentControl-LauncherUI-ForgetSavedSignIn`).

`AsyncRelayCommand` is the asynchronous counterpart of `RelayCommand`: it runs a task-returning
operation with a cancellation token, ignores a new request while one is running, exposes
`IsRunning` and `Cancel`, and reports an unhandled failure to a required error callback instead of
letting it escape. `RepoCardViewModel` uses it so that reading package sources, extracting a
package or pulling never runs on the UI thread, and shows a busy message with a Cancel button
while one runs. Upgrade availability is determined by a separate background check per card
(`RefreshUpgradeStatusAsync`, where a newer check supersedes an older one), which
`MainWindowViewModel` starts a few cards at a time after construction, after a repo is added
and after settings are applied, so that one slow or unreachable source holds up only the cards' own checks, never the
window, and never hides what the other sources offer (`AgentControl-LauncherUI-ResponsiveOperations`,
`AgentControl-LauncherUI-SourceFailureIsolation`,
`AgentControl-LauncherUI-StaleResultsDiscarded`). The application is left open all day while packages are published,
so `MainWindowViewModel` repeats the same check for every card every five minutes while the window is open (a timer on
a `TimeProvider` whose ticks are posted to the UI thread; it skips busy, missing and unpinned cards, never overlaps
rounds, reads each source once per round - an unreachable source too, whose failure is remembered for the round only -
and stops when the window closes and the view model is disposed;
`AgentControl-LauncherUI-PeriodicUpgradeRecheck`), and opening the Select Package dialog reads the sources afresh
rather than serving a listing cached earlier (`AgentControl-LauncherUI-SelectPackageReadsSourcesFresh`). That
background check never opens the
sign-in dialog (`RefreshUpgradeStatusAsync` defaults to `allowPrompt: false`): for a server it uses the
credentials already typed this session or saved with the user's Git credential helper (read without any dialog)
and otherwise sets the card's source problem to say a sign-in is
needed and to click the card's "Sign-in needed" chip or use the card's menu > Refresh, either of which is a check
the user started and so may ask
(`AgentControl-LauncherUI-BackgroundChecksDoNotPrompt`, `AgentControl-LauncherUI-SignInNeededChipSignsIn`). When
the user then cancels or closes the sign-in dialog that check opened, or the server rejects the sign-in typed, the
source still needs a sign-in, so the card shows the same "Sign-in needed" chip with the same tooltip - not "Update
check failed" - and the user can click it again at once; the check asks at most once, and nothing asks again by
itself because every automatic check never prompts. Every other failure of that check is still shown as a failed
check (`AgentControl-LauncherUI-SignInNotGivenKeepsChip`). A
sign-in made with the Settings window's Test connection is kept in the session's credential store the cards share,
so when the Settings window closes, `MainWindow` calls `MainWindowViewModel.RecheckAfterSettingsClosed`, passing
whether `SettingsWindowViewModel.Saved` was raised: after a save it starts nothing (`ApplySettingsAsync` has already
started the check of every card), otherwise it re-checks in the background - the same never-prompting check,
alongside any check that is running - only the cards that are not busy, not missing, pinned, and show a source
problem or a failed upgrade check, so the sign-in (or a connection that came back) shows on them at once
(`AgentControl-LauncherUI-SettingsClosedRechecksProblems`).

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
shown for Remove confirmation; a second constructor takes the labels of the two buttons for questions whose answers
are not literally Yes and No, keeping the AutomationIds, the focus on the declining button and Escape as a decline)
to offer a package's optional root-level `AGENTS.md` template
when the repo has none of its own: `MainWindow` subscribes to
`AgentsMdTemplateOfferRequested`, shows `ConfirmationWindow` with explanatory text, and calls
back into `AcceptAgentsMdTemplateAsync`/`DeclineAgentsMdTemplateAsync` (which write off the UI thread) depending on
the user's answer
(`AgentControl-LauncherUI-AgentsMdTemplateOffer`). This mirrors the non-modal
`ReleaseNotesViewer` pattern's event-raise/subscribe structure but intentionally uses a
*modal* dialog instead, since this prompt requires an explicit yes/no decision before
proceeding, unlike release notes which are purely informational. The two are sequenced: the view
model raises the offer before `ReleaseNotesReady`, `MainWindow` remembers the open question per card and
`ShowReleaseNotesAsync` waits for it to be answered before opening the release-notes window and activating
it, so the question never covers the notes and the notes window - which has no owner - is not lost behind the
modal question; the notes are not opened at all when the main window has closed meanwhile.

A package version republished with different files (the same version, other content) - or, for a Git source, a tag
that was moved to other files - is handled the same way: when **Change package...** (or
**Select Package...**)
of the version the repo is pinned to would replace its agent files with changed content,
`RepoCardViewModel` changes nothing and raises `ContentChangeConfirmationRequested` with the selection and a
plain-language question; `MainWindow` shows it in the same modal `ConfirmationWindow` (the safe answer, No, is the
default) and, only if the user answers Yes, applies the selection again with `acceptContentChange: true`, which
replaces the files and records the new identity. An Upgrade or the restore or update of agent files before launch
never asks: it refuses and reports (the Launch case through the "Launch anyway / Cancel" question below), and the
message names the card menu's **Change
package...** as the way to accept the change - it no longer suggests deleting the repo's pin file
(`AgentControl-LauncherUI-ContentChangeRefusal`, `AgentControl-LauncherUI-ChangePackage`).

**Verifying the agent files before launch.** Launch no longer takes the existence of the four managed folders as proof
that the pinned files are there (a repo with the folders present but half of the files missing was launched that way
indefinitely). `RepoCardViewModel` decides from the files themselves, in this order (the full table is in the
*RepoCardViewModel* design): committed agent files or no pin skip the check; a repo whose managed folders hold no file
at all gets the pinned package installed silently; for a pin that records a files identity the files in the folders
are hashed with the very algorithm that produced that identity and compared, with no package source read, so an
in-sync launch asks nothing and downloads nothing; a pin recorded without an identity keeps the older folder check
(all four folders present: launch; one missing: silent install). When the files differ - a file missing, changed or
added, or not readable because of a link - the card raises `AgentFilesChoiceRequested` with an
`AgentFilesChoiceRequest`, and `MainWindow` shows it in the same modal `ConfirmationWindow`, with the request's own
button labels: **Update from package** replaces the four folders with the pinned package's files, **Keep current
files** (the default, and what Escape answers) launches with the files as they are. The question is asked on every
launch while the difference persists and nothing is persisted. When agent files that were needed cannot be installed
(no source, an unreachable or refusing source, a republished package, a failed copy) the card raises the same event
with the reason and the labels **Launch anyway** and **Cancel** (the default and Escape): only Launch anyway starts the
agent tool; Cancel starts nothing and records no launch. This is the one case in which agent-package state stands in
the way of a launch, and the user decides it (`AgentControl-LauncherUI-VerifyAgentFilesBeforeLaunch`,
`AgentControl-LauncherUI-RestoreFailureChoice`; the decision is recorded in architecture.md). `MainWindow` always
answers the request - a dialog that fails to show answers with the safe default - so the card's launch never hangs.

**Converting committed agent files.** A repo can have its managed agent folders (`.github/agents`,
`.github/standards`, `.github/templates`, `.github/skills`) tracked by git. `.gitignore` has no effect on tracked files,
so applying a package over them would leave git showing them modified or deleted and the new files invisible, with a
commit of everything recording a broken mix. Select Package, Change package... and Upgrade therefore convert such a
repo, with the user's consent: `RepoCardViewModel` asks git at that moment which of the four folders are tracked (not
the cached "Committed agent files" chip), and when some are, raises `CommittedFilesConversionRequested` with a
`CommittedFilesConversionRequest` - before anything is downloaded - naming the repo, the tracked folders and the
package, saying that AgentControl will stop tracking the folders in git (the files stay on disk until the package
replaces them), add them to `.gitignore` and apply the package, and that the result is to be committed afterwards.
`MainWindow` shows the question in a modal `ConfirmationWindow` (the safe answer, No, is the default) and answers the
request in place; the card's package operation waits for the answer while the card stays busy, so its other actions
stay disabled and the operation stays serialized. Declining changes nothing at all. Agreeing lets the operation go on:
only after the package has been acquired and accepted, and immediately before it replaces the files, git stops
tracking exactly the tracked folders (`GitClient.UntrackAgentFolders`, index only); a failure of that stops the apply
before any file is replaced. Afterwards the card's committed-files chip clears, the "Uncommitted changes" chip shows,
and the status message tells the user to commit the change. Launch is unchanged: it still skips the sync for such a
repo and never converts it (`AgentControl-LauncherUI-ConvertCommittedAgentFiles`).

**Change package.** A repo that already has a pin is offered **Change package...** in the card menu's *Agent files*
group (`ChangePackageMenuItem`, shown while `HasPinnedPackage`; the group is shown whenever a package is pinned,
`HasAgentFileActions`). It is the same `SelectPackageCommand` as **Select Package...**, which is now executable for
any present, idle repo; `MainWindow` opens `SelectPackageWindow` with a view model that is given the source that
supplies the repo's files (when known), the pinned package and the version, so the dialog opens on the current choice,
and applies the confirmed selection through
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
(`AgentControl-LauncherUI-GitSourcePresentation`). On a repo card the package chip shows the Git icon and the alias of
the source that supplies the files; its tooltip adds the tag (the source's tag pattern applied to the pinned
version). There is no separate chip for a moved tag: files that differ are caught by the content identity check (see
*Portable pins* and the content-change paragraph above). A git older than the supported version
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
again whenever `MainWindowViewModel.ApplySettingsAsync` has saved new settings (through an injected
`Action<ThemePreference>`, so the view model stays free of the UI framework), so every open window changes
at once without a restart (`AgentControl-LauncherUI-Appearance`). Every color the application's own XAML uses
lives in `AppStyles.axaml` as a resource in a `Light` and a `Dark` theme dictionary (surfaces, borders,
shadows, the status accent colors, chip fills and text, button states, the avatar palette, the group accent palette
(`GroupAccent0` ... `GroupAccent9`), the drag-and-drop
indicator `RepoDropIndicator` and header highlight `RepoDropTargetBackground`, the About banner (`AboutBanner*`), the
global progress strip (`GlobalProgressForeground`, `GlobalProgressTrack`, `GlobalProgressStripBackground`,
`GlobalProgressTextForeground`),
validation,
caution and success text, the page background) consumed with `DynamicResource`; no template or setter holds a
hard-coded color. Text colors were chosen for at least 4.5:1 contrast against the fill and surface they are
drawn on, status accents and icons for 3:1, and `ThemeResourceTests` checks both dictionaries define the same
keys and meet those ratios in every control state it covers. Defining every color for both themes is the design
rule that makes the "legible in both themes" requirement (`AgentControl-LauncherUI-Appearance`) hold: no window can
be left with a color that exists in one theme only. The same file defines the shared styles: the repo card
(surface, soft shadow,
hover lift with 140 ms transitions, selection and keyboard-focus ring), the status accent bar, the avatar
tile, the chips (one height, padding and font, tinted fill with a matching foreground, a flat button wrapper
for clickable chips, whose amber, grey and red chips tint on hover, and amber and red also when pressed), the action
buttons (primary
accent with hover, pressed and disabled states, and a
tinted secondary), the ghost icon buttons without a border (favorite star, "more actions"), the group headers of the
card menu, the drag-and-drop feedback (see *Reordering repos*) and the empty state. Per-control color setters are
avoided on purpose.

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
none applies) and *Organize* (**Move to group**, then **Reorder**, a submenu holding **Move to top**, **Move up**,
**Move down** and **Move to bottom**, and **Card color...**) and **Remove from list**. The upgrade
chip is a button that runs the same `UpgradeCommand` as the menu's Upgrade item
(`AgentControl-RepoCardViewModel-UpgradeFromChip`).

**Repo groups and card colors (user-specific).** Both are personal organizing choices stored only in the user's
own settings file (on the repo's `RecentRepo`, and the collapsed section names, the group colors and the group order in
`AppSettings`), never in the
repository or its `.agentcontrol.json` (`AgentControl-System-RepoGroups`, `AgentControl-System-CardColor`,
`AgentControl-System-GroupColor`, `AgentControl-LauncherUI-RepoGroupsSettingsOnly`).
*Sections.* The list is a single `ListBox` over `MainWindowViewModel.DisplayedItems` with two row templates: a
repo card, and a section header - a fixed 8-pixel slot for the group's color accent, one flat button (chevron, icon,
name, count chip) that collapses or expands the
section, plus, for the user's own groups, a "..." menu (`GroupHeaderMenuButton`, tooltip "Add a repository to this
group, or move, color, rename or remove the group") with **Add repository...** (`GroupAddRepoMenuItem`), a separator,
**Move up** (`MoveGroupUpMenuItem`), **Move down** (`MoveGroupDownMenuItem`), **Group color...**
(`GroupColorMenuItem`), a separator, **Rename group...** and **Remove group**. The header
button's accessible name says the section, its count and its state (`Work, 2 repos, collapsed`); the group's color does
not change it. The header template is wrapped in a `Panel` with two drop indicator borders
(`GroupDropIndicatorAbove`, `GroupDropIndicatorBelow`) for dragging groups. A decision
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
*Card color.* **Card color...** opens a small palette pop-up (an attached `Flyout` on the "..." button, whose content is
a `ColorPalettePanel` with AutomationId `CardColorPalette`) with one
round swatch per `CardColorPalette` entry - the same ten theme-aware avatar colors the automatic color is drawn
from - plus **Automatic**. Each swatch has an AutomationId (`CardColorSwatch_<Name>`), its name as tooltip and
accessible name,
and the chosen one shows a check mark and a ring, so the choice is not carried by color alone. The color goes only to
the avatar tile (palette style classes `avatar0` ... `avatar9`, driven by bindings on the card's `AvatarStyleClass`);
the status accent bar and chips are unaffected. A repo that shows its own picture keeps the tile's light backing
so a logo drawn for a white page stays legible, and the chosen color - the card's own or its group's
(`RepoCardViewModel.IsTileColorChosen`) - is drawn as a ring around the picture. The
letters on every palette color use whichever of white and near-black reads better on that fill in each theme,
at least 4.5:1 (`ThemeResourceTests` checks every entry in both themes).
*Group color.* **Group color...** in a user group's header menu opens the same palette for the group (a
`ColorPalettePanel` with AutomationId `GroupColorPalette` in a `Flyout` attached to `GroupHeaderMenuButton`, opened by
the window's `GroupColorMenuItem_Click`, which posts the opening to the dispatcher like the card's), with **None**
instead of
**Automatic** (`GroupColorSwatch_<Name>`, `GroupColorHeadingText`, `GroupColorNoneButton`); its heading names the
group's color (`Group color: Teal`, or `Group color: None`) and the chosen swatch is marked. The window's
`GroupColorPalette_ColorChosen` calls `MainWindowViewModel.SetGroupColor`, which saves the choice first
(`AgentControl-LauncherUI-GroupColor`). A colored group's header shows a 4 by 20 pixel rounded bar
(`GroupColorAccent`) in its fixed-width slot, so headers stay aligned whether or not they are colored; the bar's
tooltip and accessible name name the color (`Group color: Teal`), so it is never conveyed by color alone, and
*Favorites* and *Ungrouped* are never colored (they have no menu). The bar is drawn with its own brushes
`GroupAccent0` ... `GroupAccent9` (style classes `groupAccent0` ... `groupAccent9`, from the header's
`ColorStyleClass`) rather than the avatar fills, because a header accent is a graphic on the page and must reach 3:1
on `PageBackground` and on the drop tint `RepoDropTargetBackground` over the page in both themes: the dark theme uses
lighter tones, the light theme the avatar fills except Amber, which is darker there (`ThemeResourceTests`,
`AgentControl-LauncherUI-GroupColorLegible`). The tiles of the group's repos that have no card color of their own take
the group's color, a card's own color wins, a favorite keeps its group's color (its chip says which group), and the
card palette's heading says `Card color: Automatic (group color Teal)` when a tile follows its group
(`AgentControl-LauncherUI-GroupColorOnCards`). A group's color and its position in the group order follow a rename,
are forgotten when the group is removed or its last repo leaves it, and are kept when settings are applied
(`AgentControl-LauncherUI-GroupSettingsKeptInStep`).
*The palette control.* `ColorPalettePanel` (built in code, no `.axaml`) has the styled properties `IdPrefix` (default
`CardColor`), `Heading`, `SelectedColorName`, `ClearText` (default `Automatic`), `ClearTooltip` and `ClearIcon`, and
the event `ColorChosen` (`ColorChosenEventArgs.ColorName`, `null` for the clear button). It builds a heading
`TextBlock` named `{IdPrefix}HeadingText`, one swatch `Button` per `CardColorPalette.All` entry (classes `swatch` and
`avatar{index}`, tag, accessible name and tooltip the color's name, AutomationId `{IdPrefix}Swatch_{Name}`, the
`selected` class on the chosen one) and a clear `Button` (AutomationId and name `{IdPrefix}{ClearText}Button`, with a
check mark while no color is chosen); the parts are rebuilt when `IdPrefix`, `ClearText`, `ClearIcon` or
`ClearTooltip` change. The card's palette therefore keeps every identifier it had, and one control guarantees that the
two palettes look and behave the same. The window finds the palette of an item with `FindControlOfItem(item, name)`.

**Reordering repos (user-controlled order).** The order of the repo cards belongs to the user and nothing else
changes it: the list is kept in `AppSettings.RecentRepos` order (see the `Settings` design), a new repo goes to the
end, and launching, refreshing, pulling, upgrading or marking a favorite never moves a card
(`AgentControl-LauncherUI-SortOrder`, `AgentControl-LauncherUI-StableRepoOrder`; system level
`AgentControl-System-RepoOrder`). Favorites stay first, and each section - or, with no groups, each of the two
implicit tiers (favorites, the rest) - shows its repos in the same relative order. The user changes the order in two
ways (`AgentControl-System-RepoReorder`), and both end in the same two view-model operations, so the business rules
live in `MainWindowViewModel` and the window only translates input and shows feedback:

- *Move menu items.* The *Organize* group of a card's "..." menu has a **Reorder** submenu (AutomationId
  `ReorderMenuItem`, a swap-vertical icon) holding **Move to top**, **Move up**, **Move down** and
  **Move to bottom** (AutomationIds `MoveToTopMenuItem`, `MoveUpMenuItem`, `MoveDownMenuItem`,
  `MoveToBottomMenuItem`), which are keyboard-reachable and so give a way to reorder that needs no drag. They move the
  card within its own section (`MainWindowViewModel.MoveCard`). As with the *Move to group* submenu, what they may do
  depends on state that is only known when the menu opens, so `MainWindow`'s `CardMenu_Opening` handler (through
  `UpdateMoveMenuItems`) asks `MainWindowViewModel.GetMoveDisabledReason` for each item, disabling it and giving it
  that reason as its tooltip (shown even while disabled) - at the start or end of the section, and while the filter
  has text - and `MainWindowViewModel.GetReorderDisabledReason` for the **Reorder** submenu itself, which is disabled,
  with the same filter explanation as its tooltip, while the filter has text and, with its own explanation, when none
  of the four moves applies (the only repo of its section) (`AgentControl-LauncherUI-RepoOrderMenu`).
- *Drag and drop.* **Drag source**: the window observes the pointer on the `RepoListBox` on the way down (also for
  events a row already handled). A press with the left button on a card is remembered as a possible drag, unless it is
  on a `Button`, `TextBox`, `MenuItem` or `ScrollBar` in the card (the star, the chips, the menu keep their own
  behavior), on a section header (a user group's header starts a group drag instead, see *Ordering groups*), or while
  reordering is off; the drag starts once the pointer has moved 6
  device-independent pixels in either direction with the button held, which keeps a plain or slightly unsteady click a
  click. The dragged data is one item of the application data format `DemaConsulting.AgentControl.RepoCard` carrying the
  card's repo path, and the dragged card's row gets the `dragging` style class (dimmed) until the drag ends however it
  ends. The operating system's drag is started through the `DragStarter` seam, whose default is
  `DragDrop.DoDragDropAsync`; as with the folder picker seam (`RepoFolderPicker`), a headless test supplies its own
  starter that records the data and then raises the drop events on the list. **Drop target**: the list is a drop target
  (`DragDrop.AllowDrop`) with drag-enter, drag-over, drag-leave and drop handlers. The row under the pointer is found
  from the realized rows' layout bounds (`GetRowAt`, not a renderer hit test, so it is right straight after an
  auto-scroll moved the rows): over a card, the upper half of the row (`IsUpperHalf`) means "before that card" and the
  lower half "after
  it", giving a `RepoDropTarget.ForCard` with the card's section (`GetSectionOf`); over a section header,
  `RepoDropTarget.ForHeader` (no anchor: the end of that section). Drag-over asks `MainWindowViewModel.CanDrop` and
  answers `Move` or `None` as the drag effect; drop calls `MainWindowViewModel.DropCard`. A drag that does not carry
  this data format, or a repo that is not listed, is ignored.
- *What a drop does* (`AgentControl-LauncherUI-RepoDragReorder`, `-RepoDragBetweenSections`,
  `-RepoDropOnHeader`): within a
  section it moves the card to the drop position. Into another section it first changes the card's section - the group
  for a group or Ungrouped section, the favorite flag for Favorites, exactly as the menu and the star would, so the
  card's chip and the header counts stay consistent - and then puts it at the drop position, so one drop can mean two
  saves (a failure of the second is reported and leaves the card in its new section at its old relative place). On a
  header the card goes to the end of the section. A collapsed group stays collapsed (the header's count changes, which
  is the visible result; the user's collapsed state is not touched). In the flat list (no repo is in a group) a drag
  only reorders within a tier: a drop on the other tier is refused, since without headers nothing would explain the
  favorite change.
- *Feedback* (`AgentControl-LauncherUI-RepoDropFeedback`): the card template carries two thin, non-hit-testable
  `Border`s (`DropIndicatorAbove`, `DropIndicatorBelow`) that take no layout space; while the pointer is over an
  accepted place the window puts the style class `dropAbove`, `dropBelow` or `dropHeader` on that row's `ListBoxItem`
  (and removes it again when the place changes, the drag leaves the list, the drop happens or the drag ends), and
  `AppStyles.axaml` makes the matching indicator line visible (`dropAbove`, `dropBelow`) or tints the header
  (`dropHeader`, `RepoDropTargetBackground`). The line uses `RepoDropIndicator`; both resources are defined for the
  light and the dark theme and are checked for contrast by `ThemeResourceTests`. A refused place (the drag effect
  `None`) shows no line, and neither does the dragged card's own row.
- *Auto-scroll while dragging* (`AgentControl-LauncherUI-DragAutoScroll`): a drag of a card, a group header or folders
  held near the top or bottom edge of the list, over the app bar above it (the `AppBar` border has
  `DragDrop.AllowDrop` with drag-enter/over handlers that refuse the drop, effect `None`, and a drag-leave handler) or
  below it (the `RepoListArea` margins) keeps the list scrolling, even while the pointer does not move. Every
  drag-over of the list, the list area and the app bar calls `UpdateAutoScroll` with the drag's data and the pointer
  position relative to the window: it remembers both and starts a `DispatcherTimer` (40 ms interval, 25 ticks a
  second) when the pointer is in a scroll zone, never restarting a running timer (so frequent drag-over events do not
  starve its ticks), or stops it when the pointer left the zones. Each tick (`AutoScrollTick`) recomputes the step for
  the remembered position, scrolls the list's `ScrollViewer` by it within its extent (stopping at the top or bottom),
  lays the list out again and redraws the drop feedback for whatever row is now under the pointer
  (`UpdateDragFeedback`, the same feedback a drag-over would show). The step is the pure function
  `ComputeAutoScrollStep(pointerY, viewportHeight)`, with the pointer's height relative to the viewport: the zone is
  `AutoScrollEdge` = 32 DIP inside each edge (half the viewport when it is shorter than two zones, so the nearer edge
  wins) and extends beyond the edge; the depth into the zone is measured from its inner boundary, and the step grows
  linearly from `AutoScrollMinStep` = 4 DIP per tick at the zone's boundary to the cap `AutoScrollMaxStep` = 36 DIP per
  tick, reached at a depth of `AutoScrollEdge` + `AutoScrollOvershoot` (32 + 96 = 128 DIP) - from 100 to 900 DIP a
  second; negative scrolls up, 0 (the middle of the list, a non-positive or unknown height) does not scroll, and a
  step or an offset change below `MinimumScrollDistance` (0.5 DIP) stops the timer. Clearing the feedback is split:
  `HideDropFeedback` removes the line or tint only (used inside the drag-over paths, where the drag goes on), while
  `ClearDropFeedback` also stops the auto-scroll (drag-leave, drop, the end of a card or group drag). A drag-leave
  stops the auto-scroll only when the pointer really left the element (`IsInside` is false), not when it moved from
  one row or child control to the next; leaving the window raises a drag-leave on the last element and stops it.
  `IsAutoScrolling` exposes whether the timer runs, for tests. Whether a `DispatcherTimer` ticks during the operating
  system's own drag loop on Windows is confirmed by a UI run, not by the headless tests, which call `AutoScrollTick`
  directly.
- *No reordering while filtering* (`AgentControl-LauncherUI-ReorderOffWhileFiltering`): while the filter box has text
  only some repos are shown, so their positions cannot be changed. `MainWindowViewModel.IsReorderEnabled` is then
  `false`: no drag starts, the **Reorder** submenu and its four Move items are disabled with the tooltip
  `ReorderDisabledWhileFilteringMessage`, and `MoveCard`, `CanDrop` and `DropCard` refuse as well. Filtering never
  changes the stored order.
- *Ordering groups* (`AgentControl-System-GroupReorder`, `AgentControl-LauncherUI-GroupOrder`): *Favorites* stays
  first and *Ungrouped* last; between them the groups are listed in the user's order (`AppSettings.GroupOrder`), and the
  groups without a stored position - every group until the user first orders them, and a new group - follow
  alphabetically. Two ways change it, both ending in `MainWindowViewModel`: the header menu's **Move up** and **Move
  down** (`MoveGroupUpMenuItem`, `MoveGroupDownMenuItem`, bound to the header's `MoveUpCommand`/`MoveDownCommand`,
  disabled with the reason as tooltip - shown even while disabled - at the first or last group and while the filter has
  text, `AgentControl-LauncherUI-GroupMoveMenu`) call `MoveGroup`, which swaps the group with its neighbor; and dragging
  a header (`AgentControl-LauncherUI-GroupDragReorder`). **Group drag source**: a left-button press anywhere on a user
  group's header (also on its toggle) while `IsReorderEnabled` is a possible drag, except on `GroupHeaderMenuButton`,
  its menu items, a text box or a scroll bar; past the same 6-pixel threshold `StartGroupDragAsync` releases the pointer
  capture the header's toggle button took (`press.Pointer.Capture(null)`), so the button loses its pressed state and
  the release at the end of the drag does not collapse or expand the section, puts the `dragging` class on the header
  row and starts the drag (`DragStarter`, else `DragDrop.DoDragDropAsync`, effect `Move`) with one item of the
  application data format `DemaConsulting.AgentControl.RepoGroup` (`RepoGroupDataFormat`) carrying the group's name.
  **Group drop target**: only the header of another user group accepts it, the upper half meaning before that group
  and the lower half after it (a `GroupDropTarget`), shown with the same `dropAbove` / `dropBelow` line through the
  header's own indicators; `MainWindowViewModel.CanDropGroup` decides and `DropGroup` does it. Over the *Favorites* or
  *Ungrouped* header, a card or empty space the effect is `None` and no line shows. A collapsed group stays collapsed.
  No group drag starts while the filter has text, and the Move items are then disabled with
  `GroupReorderDisabledWhileFilteringMessage`. The whole order of the groups as shown is stored on every change, so the
  next start shows the same order (`AgentControl-LauncherUI-GroupOrderRemembered`).
- *Telling drags apart*: `RepoList_DragOver` and `RepoList_Drop` dispatch by the data of the drag, always in this
  order - the card format first (the unchanged card paths `CardDragOver`/`CardDrop`), then the group format
  (`GroupDragOver`/`GroupDrop`), then `DataFormat.File` (folders from the file manager, see *Adding repos by dropping
  folders*) - so a drag is never taken for another kind. `ShowDropFeedback` takes the feedback class to show, so the
  card, group and folder feedback share one implementation; the card feedback is unchanged.
- *Collection moves.* A reorder moves cards inside `MainWindowViewModel.RepoCards` (`Move` notifications). The window's
  `OnRepoCardsChanged` ignores a `Move`, because the cards stay attached and the per-card state the window keeps
  (pending prompts, shown dialogs) must survive the reorder.

The order is stored in the user's own settings only, never in a repository or its `.agentcontrol.json`, survives a
restart, and for a settings file written before this existed is seeded once from what the user used to see (favorites
first, then most recently launched), so nothing jumps on upgrade (`AgentControl-LauncherUI-RepoOrderRemembered`;
system level `AgentControl-System-RepoOrderRemembered`, subsystem `AgentControl-Settings-RepoOrder`).

**Adding repos by dropping folders.** Folders dragged from the file manager onto the main window are added as repos
(`AgentControl-System-AddSeveralRepos`, `AgentControl-LauncherUI-AddRepoDropFolders`). The list area is a `Grid`
(`RepoListArea`, `DragDrop.AllowDrop`, transparent background so it is hit-testable everywhere) holding the list, the
empty-state panel and an overlay `Border` (`ExternalDropOverlay`, not hit-testable), hidden by default and shown while
the grid has the `externalDrop` class: a 2-pixel `RepoDropIndicator` outline over a `RepoDropTargetBackground` fill
with a corner radius of 8 (no new brushes). Where a drop goes:

- over the header of one of the user's groups, or over a card listed in such a group's section, the folders join that
  group, and that group's header row (when it is realized) gets the `dropHeader` tint;
- over *Favorites* (a favorite is a per-repo mark, not a place to add to - refusing would confuse in the flat list,
  where favorites have no header, and starring silently would surprise), *Ungrouped*, the flat list, empty list space,
  the empty-state panel or the margins, the folders are added ungrouped and the overlay outlines the list area.

The drag effect is `e.DragEffects & DragDropEffects.Copy` (nothing on disk moves), and the feedback is removed on
drag-leave and on drop. Dropping folders is allowed while the filter has text, because adding is not reordering. The
list's handlers answer every drag over the list; the `RepoListArea` handlers answer only what the list did not handle
(files are added ungrouped there; any other data is refused with `None`). The platform raises a drag-leave and a
drag-enter whenever the element under the pointer changes, so the handlers treat drag-enter like drag-over. The paths
come from `IDataTransfer.TryGetFiles()` and `IStorageItem.TryGetLocalPath()` (falling back to `Path.LocalPath` of a
`file` URI); items without a local path are left out. The drop is answered at once (Copy) and the add is posted to the
UI thread with `Dispatcher.UIThread.Post`, which calls `MainWindowViewModel.EnqueueAddReposAsync` (queued after any
add still running, checked in the background with progress in the strip; a dropped file is reported as not a Git
repository, and every folder is checked like a picked one, `AgentControl-LauncherUI-AddRepoGitRootOnly`) and then
shows the message of `DescribeAddOutcomes`, if any (nothing after a cancel), so neither the add nor a modal dialog runs
inside the operating system's drag-and-drop loop
(`AgentControl-LauncherUI-AddRepoSummary`). A drag of a card or a group header is never taken for folders, and folders
are never taken for a card move (see *Telling drags apart*).

**Settings window package-source actions.** One **Add** button opens a menu with the source types that can be added
(zip-package folder, NuGet-package folder, NuGet-package server, Git repository), each with its type's icon (the one the
sources list uses) and a one-line description; another source type is added as one more menu item
(`AgentControl-LauncherUI-AddSourceMenu`). The `.axaml` Views other than `MainWindow` (`AboutWindow`,
`ConfirmationWindow`, `MessageBoxWindow`, `SelectPackageWindow`, `SettingsWindow`, `SourceEditorWindow`,
`CredentialPromptWindow`) contain no logic beyond Avalonia's XAML-loading boilerplate,
`AutomationProperties.AutomationId` assignments that let FlaUI locate controls, and - for the dialogs that return an
answer or pick a folder - closing the window with the view model's answer, treating any other close as a cancel, or
opening the folder picker; all behavior lives in the view models above. `MainWindow`'s code-behind is the one exception:
it owns dialog-orchestration, view-layer plumbing that requires a live Avalonia `Window` — subscribing to each
`RepoCardViewModel` event (`RemoveRequested`, `SelectPackageRequested`, `ReleaseNotesReady`,
`AgentsMdTemplateOfferRequested`, `ContentChangeConfirmationRequested`, `CommittedFilesConversionRequested`,
`AgentFilesChoiceRequested`, `ErrorOccurred`) and deciding which dialog/window to show and how to route the user's
response back into the view model (e.g. awaiting `AcceptAgentsMdTemplateAsync`/`DeclineAgentsMdTemplateAsync` from the
`ConfirmationWindow` shown for the AGENTS.md template offer, or answering `MainWindowViewModel.AddSaveFailed` from the
Retry/Discard `ConfirmationWindow`). `AboutWindow` (opened via a toolbar command with no
dedicated view model, as a modal dialog over the main window) is 600 wide, sizes its height to its content up to a
`MaxHeight` of 640 and cannot be resized. Its root `Grid` has three rows. Row 0 is a banner across the top
(`AboutBanner`, exposed to assistive technology as one content element named "AgentControl: A Desktop Launcher for
Distributing Proprietary Agent Packages") composed in XAML: a band with two decorative `Ellipse` glows filled
with the theme `RadialGradientBrush`es `AboutBannerGlowPrimary` and `AboutBannerGlowSecondary`, the application logo
(reused from `Assets/AppIcon.ico`, the same icon already used for the main window/taskbar) on the 56 by 56
`LogoBackingBackground` tile, the name (`AboutBannerTitleText`, "AgentControl", `AboutBannerForeground`) and the project
tagline (`AboutBannerTaglineText`, from `AboutWindow.BannerTagline`, `AboutBannerTaglineForeground`), and a 3-pixel
gradient line along its bottom (`AboutBannerAccent`, a `LinearGradientBrush`). Every `AboutBanner*` brush is defined in
both theme dictionaries, the title and tagline are checked for 4.5:1 on the band and on the band under each glow's
strongest stop (`ThemeResourceTests`), and no new binary image asset or package was added
(`AgentControl-LauncherUI-AboutBanner`). Row 1 is the one scrolling region (`AboutContentScroller`, a `ScrollViewer`
with a `MaxHeight` of 440) holding a short mission tagline, a labeled "Version" row with the running version
(`Program.Version` without its `+` build
metadata, via `AboutWindow.DisplayVersion`; the full build version is the row's tooltip), the copyright, the license
summary, the "Third-Party Dependencies" heading and the list of this application's direct runtime third-party
dependencies and their SPDX license identifiers (`AboutDependenciesList`, no longer in a scroll region of its own;
package versions are intentionally omitted so dependency updates do not require edits here), sourced from a static
`ThirdPartyDependencies` list rather than parsed at runtime from the project file (`AgentControl-LauncherUI-About`).
Row 2 is a footer with a `SurfaceDivider` line above the `AboutCloseButton`; the banner and Close stay in view however
long the details grow (`AgentControl-LauncherUI-AboutScrollableDetails`).
Adding a repo (`AgentControl-LauncherUI-AddRepo`) is likewise driven directly by `MainWindowViewModel` rather than a
dedicated unit, since its logic (path-existence, duplicate-path and Git-repository-root checks, the last delegated to
`GitClient`) is simple enough not to warrant a
separate class.

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
Theme picker, a labeled row) and a Tools section (git executable, agent tool, shell, and the "Keep the shell open
after the agent tool exits" checkbox directly after the shell preference, with a tooltip saying what it does)
(`AgentControl-LauncherUI-Appearance`, `AgentControl-LauncherUI-KeepShellOpenOption`). The source editor's location
box wraps a long folder or feed address
over several lines (up to a maximum height, then it scrolls), has a tooltip with the whole value
(`SourceEditorViewModel.LocationTooltip`, absent while the box is empty), and the editor is 640 px wide.

`MainWindow` explicitly tracks every non-modal child window it opens (the `ReleaseNotesViewer`)
in an `_openChildWindows` list via a `ShowTrackedChildWindow(Window)` helper,
which also subscribes to each window's `Closed` event to remove it from the list, and force-closes
any still-open entries from a `MainWindow_Closing` handler before the main window
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
current tag pattern (`AgentControl-LauncherUI-GitSourcePresentation`).

**Portable pins.** A repo's pin names no source - aliases and kinds of source are each user's own configuration, and a
teammate may use any of them for the same files - so `RepoCardViewModel` resolves the pin purely from the user's own
Settings: it asks the configured sources (Settings order) which of them offer the pinned package and version, and a
pin that recorded a `files-sha256:` identity gets the first whose acquired files have exactly that identity (the
source that last supplied the card is tried first, and is accepted as it is by the background check). The supplier is
remembered per user in `settings.json` (`RecentRepo.PinnedSource`), never in the pin. The package chip then shows that
source's alias and kind icon, and its tooltip names it ("Agent files: pkg ver from source 'alias' (kind).", plus the
tag for a Git source); the chip is hidden when no source is known. When no configured source offers the version, or
sources offer it but none delivers the recorded files, the red "Package source problem" chip shows with a message
saying so. A newer version a source offers is still offered as an upgrade then (the upgrade chip and the problem
chip coexist, the accent bar is red), and Upgrade resolves across all configured sources, the newest version winning
and a tie keeping Settings order (`AgentControl-LauncherUI-PinnedSourceSupplierShown`, `-UpgradeAcrossSources`).

**Moved tags and Change package.** A Git tag that is moved, or a package that is republished, so that the same version
delivers other files has another files identity, whichever source delivers it: the restore before launch and Upgrade
refuse it (nothing is applied), and Change package asks for confirmation before the changed files replace the repo's (a
moved tag that delivers identical files is irrelevant, and nothing is shown for it). There is no separate warning chip.
The card's Refresh command, which the user starts, reads every configured source (NuGet, zip folder and Git) again
instead of reusing a cached listing, so that a newly published version or pushed tag shows up at once, while the
background check may use a listing less than five minutes old (`AgentControl-LauncherUI-RefreshRelistsSources`). The way
to accept changed files is the card menu's "Change package...", which opens the Select Package dialog on the source that
supplies the repo's files (when known), its package and version; choosing the pinned version again asks for confirmation
before the changed content replaces the files (`AgentControl-LauncherUI-ChangePackage`,
`AgentControl-LauncherUI-ContentChangeConfirmation`). The Select Package dialog never replaces a choice the user made
while the sources were loading with the repo's own source and package.

### Design Constraints

**Never block the UI thread** (`AgentControl-System-ResponsiveOperations`, `AgentControl-System-ResponsiveRepoListWork`,
`AgentControl-LauncherUI-PackageWorkNeverBlocks`). Once the main window exists, the UI thread does no disk, process or
network work of its own. It only changes view-model state, the settings in memory and the list, in the continuations of
work that ran elsewhere. A slow disk, an offline network share, a hung git or a slow package server can therefore make
a card wait, but never the window. The rule is met as follows:

- *Repo state*: every read of a repo's folder, pin, branch, committed agent folders and working tree goes through the
  one shared `RepoRefreshQueue` (`AgentControl-LauncherUI-BoundedRepoReads`). See *Background work and the global
  progress strip* and the queue's own design.
  - **Limit: two reads at a time** across all callers. One read is at most one git process or one burst of
    file-system metadata reads. Two slots, not one, so that one slow repo (a network share, a huge working tree, a git
    that hangs until its time limit) cannot stall the whole wave, or a user action that was moved up behind it. Two,
    not more, because the reads compete for the same disk and the antivirus scan at each process start: more parallel
    `git status` runs gain little and make the computer sluggish, the opposite of the goal.
  - **Priorities**: `UserAction` (Refresh, the "Sign-in needed" chip, Pull, the re-read after an untrack, and the
    read an action waits for) before `Visible` (a card the list realizes, an add's folder checks) before `Background`
    (start-up and the refresh after Settings, in display order). First in, first out within a priority. A read
    already waiting for the same repo and kind is joined, not queued again; `Promote` raises a waiting read's
    priority.
  - **Shutdown**: closing the window disposes the queue, which cancels the waiting reads and stops the running git.
- *Git*: every `GitClient` command runs under a time limit (60 seconds for the local repository, 10 minutes for a
  pull, then 10 seconds for the stopped process tree to exit). A command past its limit is stopped with its process
  tree, and the card's status message says so (`AgentControl-LauncherUI-GitTimeoutShown`).
- *Network upgrade checks* keep their own limiter: one `SemaphoreSlim` of four slots that every check shares
  (start-up, added repos, after Settings, the periodic round), so overlapping checks do not multiply. A Git source
  runs at most one git at a time per check. A check waits for its card's pending cheap-facts read before it takes a
  slot.
- *Process budget*: without a user action, at most the queue's 2 reads plus the 4 upgrade-check slots, so at most 6
  processes, typically 2. A user action adds at most one process at a time per busy card, the settings writer one
  write, and the dialogs' own work (listing a source, Test connection, the sign-in helper check) is paced by the user.
- *No periodic local re-check*: there is no timer that re-reads local git state. The periodic round (every five
  minutes, the package-version cache lifetime) runs the network upgrade checks only, through the shared limiter.
- *Settings*: every save goes through the `Settings` subsystem's `SettingsWriter`
  (`AgentControl-System-ListChangesKept`; see *Background persistence* and the writer's own design). A gesture's change
  is applied at once and scheduled: the writer keeps only the newest snapshot (`AppSettings.CreateSnapshot`, a deep copy
  taken on the UI thread) and writes it on a worker thread 300 ms after the last change, one write at a time. An add and
  the Settings window's Save are confirmed writes (`SaveNowAsync`), applied only once written. A failed background write
  is reported once per failure streak through a message box the window does not wait for, and the change is written
  again with the next change and at the close. `MainWindowViewModel.Dispose` flushes what is not written yet, waiting at
  most five seconds.
- *Packages and sources*: every built-in provider that `PackageSourceRegistry.Create` builds is wrapped in
  `BackgroundPackageSourceProvider`, which runs each call on the thread pool and computes a delivered package's
  content identity there. `GitRemoteClient` and `GitCredentialClient` start git off the caller's thread. The card's
  pin reads and writes, the extraction, the `.gitignore`, the release notes, the AGENTS.md offer and its answer, and
  the launch (shell detection and the process start) run through `IBackgroundWorkRunner`. The source editor runs Test
  connection and Forget saved sign-in in `Task.Run`, and checks a feed address for a Git credential helper 400 ms
  after the typing stops, off the UI thread. A release-notes link is opened by a shell start on a worker thread.
- *Exceptions*, each justified:
  - Before the window exists: `SettingsStore.Load` (plus a one-time order-seeding save of an older file), creating
    the log folder and the credential profile probe. Each is one small local file or folder, needed to build the
    window.
  - At the close: the bounded settings flush (at most five seconds, after the window has closed).
  - Serilog's synchronous file sink, for the few lines a user action logs on the UI thread.
  - `Path.GetFullPath` in path normalization, which is lexical (no I/O).
  - The synchronous `RepoCardViewModel.RefreshCheap`/`RefreshDirtyStatus` and `MainWindowViewModel.AddRepo`,
    `AddRepoToGroup` and `AddRepos` remain for tests and callers off the UI thread; no production code calls them on
    the UI thread.
- *Regression guard* (`Quality-UiThreadRegressionGuard`): `BlockingCallGuard` is a no-op in the application. The
  headless test session marks its UI thread as guarded, so a guarded call made there fails the test that made it. The
  guard points are:
  - `GitClient` (every git command, and the `.git/HEAD` read of the current branch)
  - `SettingsStore.Save`
  - `RepoPinStore.Load` and `Save`
  - `PackageContentIdentity` (the package hash and the installed-files hash)
  - `PackageContentExtractor.Extract`
  - `GitRemoteClient` (its process start)
  - `GitCredentialClient` (its process start)
  - the release-notes link's shell start
