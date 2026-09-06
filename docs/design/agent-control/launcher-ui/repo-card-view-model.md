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

**IsPackageSelectionNeeded**: `bool` (derived) — `PinnedPackageName is null`.

**LaunchCommand, PullCommand, UpgradeCommand, SelectPackageCommand, RefreshCommand,
FavoriteToggleCommand, RemoveCommand**: `RelayCommand` — see Interfaces in
`docs/design/agent-control/launcher-ui.md`.

#### Key Methods

**RefreshCheap**: Runs every cheap per-repo check (missing-repo, pin, branch,
committed-files badge via `HEAD`-hash cache, upgrade status). Deliberately excludes the
working-tree dirty check, which is not cacheable and is deferred/lazy per architecture.md's
repo-fact caching strategy, so this method is safe to call eagerly for every recent repo at
app launch (`AgentControl-RepoCardViewModel-CommittedFilesBadge`).

**RefreshDirtyStatus**: Re-checks working-tree cleanliness via `GitClient`, updating `CanPull`.
Not called by `RefreshCheap` or the constructor — computed lazily instead, on demand via
`RefreshCommand` or once when a card first becomes visible.

**Refresh**: Convenience entry point equivalent to `RefreshCheap` followed by
`RefreshDirtyStatus`, for callers (e.g. adding a brand-new repo) wanting an immediate full
refresh.

**Launch**: Launches the configured agentic CLI tool in the repo's working directory.

- *Parameters*: None (bound to `LaunchCommand`).
- *Returns*: `void`.
- *Postconditions*: On success, `StatusMessage` is set and `LaunchRecorded` is raised
  (`AgentControl-RepoCardViewModel-Launch`); on failure, `ErrorOccurred` is raised.

First calls `EnsureAgentFilesSyncedBeforeLaunch` and aborts entirely — without resolving the
shell/agent command or spawning any process — if that check reports the launch should not
proceed.

**EnsureAgentFilesSyncedBeforeLaunch** (internal): Ensures the repo's four managed agent
folders match the pinned package version before `Launch` proceeds.

- *Parameters*: None.
- *Returns*: `bool` — `true` if the launch may proceed.
- *Postconditions*: If `PinnedPackageName` is `null`, raises `ErrorOccurred` directing the
  user to "Select Package..." first and returns `false`. If
  `PackageZipExtractor.AllManagedFoldersExist` is already `true`, returns `true` immediately
  with no extra I/O. Otherwise resolves the *currently pinned* package at the configured
  source (never the latest) and re-extracts it directly via `PackageZipExtractor.Extract`,
  without displaying release notes (`AgentControl-RepoCardViewModel-EnsureSyncedBeforeLaunch`).
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
rewrites the pin file, refreshes pin/upgrade-status properties, and raises `ReleaseNotesReady`
with the new package's release notes.

**SelectPackage** (private): Validates a package source is configured, then raises
`SelectPackageRequested` for the view layer to show the dialog
(`AgentControl-RepoCardViewModel-SelectPackage`).

#### Error Handling

`Launch` catches `InvalidOperationException`/`ArgumentException` from
`AgentToolLauncher.BuildProcessStartInfo`/`Launch` and raises `ErrorOccurred`.
`EnsureAgentFilesSyncedBeforeLaunch` catches `InvalidOperationException` (extraction failure)
and `DirectoryNotFoundException` (unreachable source), raising `ErrorOccurred` for each rather
than propagating. `Pull` catches `InvalidOperationException` from `GitClient.Pull` and reports
it via `ErrorOccurred`. `RefreshGitStatus`/`RefreshBranchAndCommittedFiles` catch
`InvalidOperationException` from `GitClient` and degrade to "unknown"/`false` rather than
propagating, since these run as part of routine, frequent UI refreshes.
`ApplyPackageAndShowReleaseNotes` catches `InvalidOperationException` and
`DirectoryNotFoundException`, raising `ErrorOccurred` without applying a partial pin update.
The constructor throws `ArgumentNullException` for a null `recentRepo`, `getSettings`, or
`packageVersionCache`. `ApplySelectedPackage` throws `ArgumentNullException` for a null
`packageName` or `version`.

#### Dependencies

- **RepoPinStore** (`RepoConfig` subsystem) — reads/writes the repo's pin.
- **PackageSource**, **PackageVersionCache** (`AgentPackageManagement` subsystem) — package
  discovery and upgrade-availability checks.
- **PackageZipExtractor** (`RepoSync` subsystem) — extracts a package into the repo.
- **GitClient**, **CommittedAgentFilesCache** (`GitIntegration` subsystem) — git status,
  branch, and committed-files queries.
- **ShellDetector**, **AgentToolLauncher** (`AgentToolLauncher` subsystem) — shell detection
  and process launch.
- **SelectPackageWindowViewModel** (`LauncherUI` subsystem) — constructed by the view layer
  in response to `SelectPackageRequested`; its `Confirmed` event is wired to
  `ApplySelectedPackage`.

#### Callers

- **MainWindowViewModel** — constructs one `RepoCardViewModel` per `RecentRepo` and subscribes
  to `FavoriteChanged`/`LaunchRecorded` to persist settings and re-sort the displayed list.
- **MainWindow** (code-behind) — subscribes to `RemoveRequested`
  (`AgentControl-RepoCardViewModel-RemoveRequest`), `SelectPackageRequested`,
  `ReleaseNotesReady`, and `ErrorOccurred` to drive dialogs/windows.
