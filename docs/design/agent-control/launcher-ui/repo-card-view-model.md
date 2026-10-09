### RepoCardViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`RepoCardViewModel` backs a single card in the main window's recent-repos list: it tracks the
repo's pin, git status, and upgrade availability, and drives Launch/Pull/Upgrade/Select-
Package/Favorite/Remove actions for that repo. All business logic (upgrade detection, launch
orchestration, pull-eligibility checks) is implemented here or delegated to `GitClient`,
`PackageSource`, `PackageZipExtractor`, `RepoPinStore`, and `AgentToolLauncher` rather than in
any view code-behind, so this class is fully unit-testable without an Avalonia window.

#### Data Model

**RepoPath**: `string` (derived from the shared `RecentRepo`) — Fixed for the card's lifetime.

**RepoName**: `string` (derived) — The repo's leaf folder name, used as the card's title
(`AgentControl-RepoCardViewModel-DisplayInfo`).

**IsMissing**: `bool` — `true` when the repo path no longer exists on disk; when `true`, every
other check/action short-circuits to its "unavailable" default rather than attempting a git/
filesystem operation against a nonexistent path (`AgentControl-RepoCardViewModel-MissingRepo`).

**IsFavorite**, **LastLaunchedUtc**, **PinnedPackageName**, **PinnedPackageVersion**: mirror
the corresponding fields on the shared `RecentRepo`/pin file
(`AgentControl-RepoCardViewModel-Favorite`, `AgentControl-RepoCardViewModel-DisplayInfo`).

**CurrentBranch**, **HasCommittedAgentFiles**: `string?`/`bool` — Resolved via `GitClient`,
with the committed-files result consulting the per-card `CommittedAgentFilesCache`
(`AgentControl-RepoCardViewModel-CommittedFilesBadge`).

**IsUpgradeAvailable**, **LatestAvailableVersion**: `bool`/`string?` — Resolved via the shared
`PackageVersionCache` (`AgentControl-RepoCardViewModel-UpgradeBadge`).

**CanPull**: `bool` — Reflects working-tree cleanliness, refreshed lazily
(`AgentControl-RepoCardViewModel-PullGating`).

**GitStatusUnavailable**: `bool` — Set when the most recent working-tree status check could
not be completed (e.g. not a git repository, git could not be started), as opposed to a
successful check that found uncommitted changes. Both disable `CanPull`, but are distinguished
so the "Dirty working tree" badge/tooltip aren't shown for a status that simply couldn't be
determined (`AgentControl-RepoCardViewModel-PullDisabledExplanation`).

**GitStatusChecked**: `bool` — Set once `RefreshGitStatus` has completed at least once for
this card (successfully or not). Defaults to `false`, so a card constructed but not yet
refreshed (`RefreshDirtyStatus` is lazy/deferred by design) is never misreported as having a
confirmed dirty working tree before its first status check actually runs
(`AgentControl-RepoCardViewModel-PullDisabledExplanation`).

**IsWorkingTreeDirty**: `bool` (derived) — `GitStatusChecked && !CanPull && !IsMissing &&
!GitStatusUnavailable`; drives the card's "Dirty working tree" badge. Deliberately excludes
`IsMissing` repos, which already get their own dedicated "Missing" badge instead,
`GitStatusUnavailable` repos, since a failed status check is not the same as a confirmed dirty
working tree, and any repo for which `GitStatusChecked` is still `false`
(`AgentControl-RepoCardViewModel-PullDisabledExplanation`).

**PullTooltip**: `string` (derived) — The `PullCommand` button's tooltip text, explaining why
Pull is currently disabled (missing repo, not yet checked, an undeterminable git status, or
dirty working tree) instead of leaving the button to silently disappear or disable with no
explanation; when Pull is enabled, returns the original neutral "Pull the latest commits for
this repo" text (`AgentControl-RepoCardViewModel-PullDisabledExplanation`). The Pull button
itself always stays visible in the view (never hidden via `IsVisible`), consistent with the
"stay visible, disable, explain" pattern already used for Launch/Upgrade/Select-Package when
the repo is missing. `MainWindow.axaml` sets `ToolTip.ShowOnDisabled="True"` on the Pull
button so this explanatory text remains reachable while the button is disabled via
`PullCommand`'s `CanExecute` - Avalonia suppresses tooltips on disabled controls by default,
which would otherwise hide the explanation precisely when it is most needed.

**IsPackageSelectionNeeded**: `bool` (derived) — `PinnedPackageName is null`.

**_logger**: `ILogger<RepoCardViewModel>` (private, via `AppLogging.Factory.CreateLogger<RepoCardViewModel>()`)
— Used to log the real work this view model performs (launch, pull, upgrade/apply-package,
AGENTS.md accept/decline), mirroring the logging convention already established by `GitClient`
and `AgentToolLauncher`. Every call site is guarded by `_logger.IsEnabled(...)`.

**LaunchCommand, PullCommand, UpgradeCommand, SelectPackageCommand, RefreshCommand,
FavoriteToggleCommand, RemoveCommand**: `RelayCommand` — see Interfaces in
`docs/design/agent-control/launcher-ui.md`.

#### Key Methods

**RefreshCheap**: Runs every cheap per-repo check (missing-repo, pin, branch,
committed-files badge via `HEAD`-hash cache, upgrade status). Deliberately excludes the
working-tree dirty check, which is not cacheable and is deferred/lazy per architecture.md's
repo-fact caching strategy, so this method is safe to call eagerly for every recent repo at
app launch (`AgentControl-RepoCardViewModel-CommittedFilesBadge`). When the repo is found to be
missing, also invalidates `GitStatusChecked`/`GitStatusUnavailable` (not just `CanPull`), since
a cached Git-status result from before the repo disappeared is no longer trustworthy once it
reappears; this method itself never re-checks Git status on a missing-to-present transition
(that would duplicate the check already performed by `RefreshCommand`/`Refresh`'s subsequent
`RefreshDirtyStatus` call), so a caller that invokes `RefreshCheap` alone for an
already-realized card (see `MainWindowViewModel.ApplySettings`) must call `RefreshDirtyStatus`
itself afterward to get an immediate fresh check instead of leaving the card in a "not yet
checked" state until its next manual refresh
(`AgentControl-RepoCardViewModel-PullDisabledExplanation`).

**RefreshDirtyStatus**: Re-checks working-tree cleanliness via `GitClient`, updating `CanPull`,
`GitStatusUnavailable`, and `GitStatusChecked`. Not called by `RefreshCheap` or the
constructor — computed lazily instead, on demand via `RefreshCommand` or once when a card
first becomes visible.

**Refresh**: Convenience entry point equivalent to `RefreshCheap` followed by
`RefreshDirtyStatus`, for callers (e.g. adding a brand-new repo) wanting an immediate full
refresh.

**Launch**: Launches the configured agentic CLI tool in the repo's working directory.

- *Parameters*: None (bound to `LaunchCommand`).
- *Returns*: `void`.
- *Postconditions*: On success, `StatusMessage` is set and `LaunchRecorded` is raised
  (`AgentControl-RepoCardViewModel-Launch`); on failure, `ErrorOccurred` is raised.

Calls `EnsureAgentFilesSyncedBeforeLaunch` purely for its best-effort sync side effect, then
always proceeds to resolve the shell/agent command and spawn the process regardless of that
call's outcome — the agent-package sync state (missing pin, missing managed folders, failed
re-extraction) never gates the launch.

**EnsureAgentFilesSyncedBeforeLaunch** (internal): Best-effort attempt to sync the repo's four
managed agent folders with the pinned package version before `Launch` spawns the process. This
is an informational side action only — it never blocks `Launch`.

- *Parameters*: None.
- *Returns*: `bool` — informational only; `true` if no sync action was needed/attempted or a
  sync succeeded, `false` if a sync attempt was made and failed. Either way `Launch` proceeds.
- *Postconditions*: If `HasCommittedAgentFiles` is `true`, the managed folders are never
  touched (no delete, no extract) even if a pin exists and folders are missing; a non-blocking
  `StatusMessage` notes that sync was skipped. Otherwise, if `PinnedPackageName` is `null`, this
  is treated as an acceptable state (not an error) and a non-blocking `StatusMessage` notes that
  launch is proceeding without managed agent files. If `PackageZipExtractor.AllManagedFoldersExist`
  is already `true`, returns `true` immediately with no extra I/O — and critically, never
  calls `MaybeOfferAgentsMdTemplate` in that case, since the offer must only ever fire when a
  sync genuinely happens. Otherwise resolves the *currently pinned* package at the configured
  source (never the latest) and re-extracts it directly via `PackageZipExtractor.Extract`,
  without displaying release notes but still calling `MaybeOfferAgentsMdTemplate`; if this
  re-extraction attempt fails for any reason, `ErrorOccurred` is raised as a non-blocking
  warning (`AgentControl-RepoCardViewModel-EnsureSyncedBeforeLaunch`).
- Marked `internal` rather than `private` so it can be unit-tested directly, separated from
  `Launch`'s process-spawning side effect, mirroring `AgentToolLauncher.BuildProcessStartInfo`'s
  own precedent.

**Pull**: Runs `git pull` in the repo, then refreshes working-tree status
(`AgentControl-RepoCardViewModel-Pull`). Reports success/failure via `StatusMessage`, never
throwing for a failed pull.

**Upgrade**: Finds the latest package at the configured source for the pinned package name,
then applies it via `ApplyPackageAndShowReleaseNotes`
(`AgentControl-RepoCardViewModel-Upgrade`).

**ApplySelectedPackage**: Resolves the exact user-selected package name/version at the
configured source, then applies it via `ApplyPackageAndShowReleaseNotes`.

- *Parameters*: `string packageName`, `string version`.
- *Postconditions*: On success, the pin and release notes are updated exactly as `Upgrade`
  (`AgentControl-RepoCardViewModel-ApplySelectedPackage`); on a version no longer available at
  the source, raises `ErrorOccurred` without mutating the existing pin.

**ApplyPackageAndShowReleaseNotes** (private): Shared extract-and-pin sequence reused by
`Upgrade` and `ApplySelectedPackage`: extracts the package (blind-delete-and-replace),
proactively ensures the repo's `.gitignore` covers the four managed agent folders (via
`EnsureGitIgnoreCoversManagedFolders`, its own non-blocking step — see Error Handling),
rewrites the pin file (preserving any pre-existing `AgentsMdTemplateDeclined` value — read via
its own try/catch that tolerates a corrupt/unreadable existing pin file by defaulting to
`false` rather than aborting the apply; see Error Handling), refreshes pin/upgrade-status
properties, raises `ReleaseNotesReady` with the new package's release notes, and finally calls
`MaybeOfferAgentsMdTemplate`.

**MaybeOfferAgentsMdTemplate** (private): Decides whether to offer the package's optional
root-level `AGENTS.md` template, called immediately after every successful extraction in both
`ApplyPackageAndShowReleaseNotes` and `EnsureAgentFilesSyncedBeforeLaunch`'s re-extraction
branch — never as an independent, standing per-launch poll.

- *Parameters*: `string packageFilePath` — the zip just successfully extracted.
- *Returns*: `void`.
- *Postconditions*: Does nothing if the repo already has a root-level `AGENTS.md` file, or if
  the repo's pin records a prior decline (`AgentsMdTemplateDeclined == true`), or if the
  package's zip has no root-level `AGENTS.md` entry. Otherwise reads the template via
  `PackageZipExtractor.ReadAgentsMdTemplate` and raises `AgentsMdTemplateOfferRequested` with
  its content (`AgentControl-RepoCardViewModel-AgentsMdTemplateOffer`).
- Wrapped in its own non-blocking try/catch, mirroring `EnsureGitIgnoreCoversManagedFolders`:
  a failure here (e.g. a transient read error) never aborts the pin write, release-notes
  display, or launch that precede/follow it.

**AcceptAgentsMdTemplate**: Writes the user-accepted `AGENTS.md` template to the repo root,
called by the view layer after the user answers "Yes" to the `AgentsMdTemplateOfferRequested`
prompt.

- *Parameters*: `string templateContent`.
- *Returns*: `void`.
- *Postconditions*: Writes `templateContent` verbatim to `AGENTS.md` at the repo root via a
  create-new (never-overwrite) file write, never into any of the four managed folders
  (`AgentControl-RepoCardViewModel-AcceptAgentsMdTemplate`). Race-safe against the gap between
  `MaybeOfferAgentsMdTemplate`'s existence check and this write: opening the file with
  `FileMode.CreateNew` is isolated into its own try/catch, separate from the subsequent write,
  so that only a failure to *create* the file (because it already exists) is ever reported as
  the non-destructive "AGENTS.md already exists" outcome; a failure during the write/flush step
  that follows a successful create is always reported via `ErrorOccurred` as a genuine error,
  never misclassified as a pre-existing file. On success or the non-destructive outcome, logs
  at `Information`; on a genuine failure, logs at `Error`.

**DeclineAgentsMdTemplate**: Persists the user's decision not to adopt the template, called by
the view layer after the user answers "No".

- *Parameters*: None.
- *Returns*: `void`.
- *Postconditions*: Loads the repo's existing pin (or constructs a minimal one from the
  card's current `PinnedPackageName`/`PinnedPackageVersion` if none exists yet), sets
  `AgentsMdTemplateDeclined = true`, and saves it via `RepoPinStore.Save`
  (`AgentControl-RepoCardViewModel-DeclineAgentsMdTemplate`). Never writes an `AGENTS.md` file.
  This flag is never reset back to `false` by any code path in this class, per the feature's
  explicit scope — on failure, raises `ErrorOccurred` rather than throwing.

**EnsureGitIgnoreCoversManagedFolders** (private): Best-effort, non-blocking wrapper around
`GitIgnoreEnsurer.Ensure` called immediately after every successful extraction in
`ApplyPackageAndShowReleaseNotes`, before the pin-file write. Deliberately wrapped in its own
try/catch, separate from `ApplyPackageAndShowReleaseNotes`'s outer catch, so a `.gitignore` I/O
failure can never abort the pin write or release-notes display that follow it — mirroring the
non-blocking idiom already established by `EnsureAgentFilesSyncedBeforeLaunch`.

**SelectPackage** (private): Validates a package source is configured, then raises
`SelectPackageRequested` for the view layer to show the dialog
(`AgentControl-RepoCardViewModel-SelectPackage`).

#### Error Handling

`Launch` catches `InvalidOperationException`/`ArgumentException` from
`AgentToolLauncher.BuildProcessStartInfo`/`Launch` and raises `ErrorOccurred`.
`EnsureAgentFilesSyncedBeforeLaunch` catches `InvalidOperationException` (extraction failure)
and `DirectoryNotFoundException` (unreachable source), raising `ErrorOccurred` as a
non-blocking warning for each rather than propagating or blocking the launch. `Pull` catches
`InvalidOperationException` from `GitClient.Pull` and reports it via `ErrorOccurred`.
`RefreshGitStatus`/`RefreshBranchAndCommittedFiles` catch `InvalidOperationException` from
`GitClient` and degrade to "unknown"/`false` rather than propagating, since these run as part
of routine, frequent UI refreshes. `ApplyPackageAndShowReleaseNotes` catches
`InvalidOperationException` and `DirectoryNotFoundException`, raising `ErrorOccurred` without
applying a partial pin update; its read of a prior pin's `AgentsMdTemplateDeclined` value is
guarded by its own inner try/catch that treats an `InvalidOperationException` (corrupt/unreadable
existing pin file) as "no persisted decline" (defaults to `false`) rather than letting it
propagate to the outer catch and abort the apply after extraction has already succeeded; the
subsequent `RepoPinStore.Save` call's own failure handling is unchanged. `EnsureGitIgnoreCoversManagedFolders` catches
`InvalidOperationException` from `GitIgnoreEnsurer.Ensure` separately, in its own nested
try/catch, raising `ErrorOccurred` as a non-blocking warning without ever propagating — a
`.gitignore` I/O failure never aborts the pin write or release-notes display. `MaybeOfferAgentsMdTemplate`
catches `InvalidOperationException`/`ArgumentException`/`NotSupportedException` (covering both
`PackageZipExtractor.ReadAgentsMdTemplate` and `PathHelpers.SafePathCombine` failure modes) in
its own non-blocking try/catch, logging nothing further and never propagating.
`AcceptAgentsMdTemplate` writes via `FileMode.CreateNew` in a step isolated from the
subsequent write/flush; only that creation step's `IOException` is guarded by a check that the
target file now exists (another user/process created it after the offer was raised but before
this call ran) to report a non-destructive, informational `StatusMessage` rather than an
error. A failure during the write/flush step that follows a successful create is always a
genuine error (e.g. disk full) and is never misreported as "already exists", since it is
handled by a separate, later catch. Any `IOException`/`UnauthorizedAccessException`/
`ArgumentException`/`NotSupportedException` from either step (other than the guarded
create-time "already exists" case) raises `ErrorOccurred` rather than throwing (it never calls
`RepoPinStore`, so `InvalidOperationException` cannot occur on this path).
`DeclineAgentsMdTemplate` catches those same four exception types plus
`InvalidOperationException` from `RepoPinStore.Load`/`Save` and likewise raises
`ErrorOccurred` rather than throwing. The constructor
throws `ArgumentNullException` for a null
`recentRepo`, `getSettings`, or `packageVersionCache`. `ApplySelectedPackage` throws
`ArgumentNullException` for a null `packageName` or `version`.

#### Dependencies

- **RepoPinStore** (`RepoConfig` subsystem) — reads/writes the repo's pin.
- **PackageSource**, **PackageVersionCache** (`AgentPackageManagement` subsystem) — package
  discovery and upgrade-availability checks.
- **PackageZipExtractor** (`RepoSync` subsystem) — extracts a package into the repo, and
  reads an optional root-level `AGENTS.md` template via `ReadAgentsMdTemplate`.
- **GitIgnoreEnsurer** (`RepoSync` subsystem) — proactively ensures the repo's `.gitignore`
  covers the four managed agent folders after a successful extraction.
- **PathHelpers** (`Utilities` subsystem) — used by `MaybeOfferAgentsMdTemplate` and
  `AcceptAgentsMdTemplate` to safely combine the repo root with the fixed `AGENTS.md`
  filename before any read or write.
- **GitClient**, **CommittedAgentFilesCache** (`GitIntegration` subsystem) — git status,
  branch, and committed-files queries.
- **ShellDetector**, **AgentToolLauncher** (`AgentToolLauncher` subsystem) — shell detection
  and process launch.
- **AppLogging**, **ILogger** (`Logging` subsystem) — `_logger` logs the real business
  operations this view model performs (launch, pull, upgrade/apply-package, AGENTS.md
  accept/decline), mirroring the convention already established by `GitClient` and
  `AgentToolLauncher`.
- **SelectPackageWindowViewModel** (`LauncherUI` subsystem) — constructed by the view layer
  in response to `SelectPackageRequested`; its `Confirmed` event is wired to
  `ApplySelectedPackage`.

#### Callers

- **MainWindowViewModel** — constructs one `RepoCardViewModel` per `RecentRepo` and subscribes
  to `FavoriteChanged`/`LaunchRecorded` to persist settings and re-sort the displayed list.
- **MainWindow** (code-behind) — subscribes to `RemoveRequested`
  (`AgentControl-RepoCardViewModel-RemoveRequest`), `SelectPackageRequested`,
  `ReleaseNotesReady`, `AgentsMdTemplateOfferRequested`, and `ErrorOccurred` to drive
  dialogs/windows, calling `AcceptAgentsMdTemplate`/`DeclineAgentsMdTemplate` from the
  `ConfirmationWindow` shown for the AGENTS.md template offer.
