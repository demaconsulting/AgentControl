### RepoCardViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`RepoCardViewModel` backs a single card in the main window's recent-repos list: it tracks the
repo's pin, git status, and upgrade availability, and drives Launch/Pull/Upgrade/Select-
Package/Favorite/Remove actions for that repo. All business logic (upgrade detection, launch
orchestration, pull-eligibility checks) is implemented here or delegated to `GitClient`,
`PackageSourceRegistry`, `PackageContentExtractor`, `RepoPinStore`, and `AgentToolLauncher`
rather than in any view code-behind, so this class is fully unit-testable without an Avalonia
window, and every change to the user's own settings entry (favorite, group, card color, launch time) is made
through the owner (see *Changes go through the owner* below). Every package-source operation is bound to the
source named in the repo's pin, which is the *preferred* source: another configured source takes its place only when
it is verified to deliver the files the pin recorded (see *Portable pins* below;
`AgentControl-RepoCardViewModel-SourceAwareness`), and the card shows whether the source that supplies the package
requires signed
packages (`AgentControl-RepoCardViewModel-SignatureIndicator`).

#### Data Model

**RepoPath**: `string` (derived from the shared `RecentRepo`) — Fixed for the card's lifetime.

**RepoName**: `string` (derived) — The repo's leaf folder name, used as the card's title
(`AgentControl-RepoCardViewModel-DisplayInfo`).

**IsMissing**: `bool` — `true` when the repo path no longer exists on disk; when `true`, every
other check/action short-circuits to its "unavailable" default rather than attempting a git/
filesystem operation against a nonexistent path (`AgentControl-RepoCardViewModel-MissingRepo`). The refresh
command is deliberately *not* disabled for a missing repo: it is the only in-app way to re-check once the folder
(a remounted drive, a restored clone) is back, so **RefreshMenuText** reads "Check again" and **RefreshTooltip**
"Check whether the folder is available again" while `IsMissing`, and "Refresh" / "Re-check this repo's branch,
chips, and upgrade status" otherwise; the other commands stay disabled
(`AgentControl-RepoCardViewModel-MissingRepoRecheck`). A refresh that finds the folder again clears `IsMissing`
and the actions become available again (`AgentControl-RepoCardViewModel-MissingRepoCleared`).

**IsFavorite**, **LastLaunchedUtc**, **PinnedPackageName**, **PinnedPackageVersion**,
**PinnedSource**: mirror the corresponding fields on the shared `RecentRepo`/pin file
(`AgentControl-RepoCardViewModel-Favorite`, `AgentControl-RepoCardViewModel-DisplayInfo`).
`IsFavorite` is the *only* owner of the favorite state: the card's star is a `ToggleButton` whose `IsChecked`
binds to it two-way and the card has no favorite command. (An earlier design also bound a command that toggled
the property, so one click flipped the state twice - the toggle itself, then the command - and a repo could
neither be favorited nor unfavorited.) Setting it changes the shared record and raises `FavoriteChanged` exactly
once per change, but only after the owner saved the change (see *Changes go through the owner* below); the
owning `MainWindowViewModel` re-sections the list.
`PinnedSource` is the alias of the source the pin names (a pin written before sources existed
reads as `Default`): the preferred source, which stays as recorded even when another source supplies the files. The
card also keeps, read from the pin by `RefreshPin`, the pin's `ContentIdentity` (what another source's files are
verified against), its `SourceRevision` (the commit a Git pin's files came from) and `_supplyingSource` - the alias of
the configured source that was verified to deliver the identical files when the pinned source cannot be used, never
written to the pin.

**Changes go through the owner** (`AgentControl-RepoCardViewModel-ChangeSavedBeforeApplied`): the constructor takes
an optional `commitChange` callback, `Func<RecentRepo, Action<RecentRepo>, bool>`. The card never assigns to its
`RecentRepo` directly: the private `CommitChange` hands the owner the entry and the assignment to make, and the
owner (`MainWindowViewModel.CommitRepoChange`) makes it on a candidate copy of the settings, saves the candidate,
and only then applies it to the live entry, returning `true`; a failed save returns `false` after telling the user.
The card then raises its change event and updates its derived properties only when the callback returned `true`. When it
returned `false` nothing changed: `IsFavorite` keeps its value (and raises `PropertyChanged` for it so the two-way
bound star shows the real state again), `MoveToGroup` and `SetCardColor` return `false`, and a launch is simply not
stamped (`RecordLaunched` raises no `LaunchRecorded`). Without a callback (a card built outside the main window, as
in tests) the change is applied to the entry directly. The assignments must be plain assignments of fixed values,
because the owner runs them twice (candidate, then live entry).

**SourceProblem** / **HasSourceProblem**: `string?`/`bool` — the card's error state for a pin whose
package cannot be obtained: its source alias is not among the configured sources and no other configured source
delivers the identical files (or the pin recorded no identity to verify one against), or a source configuration that is
itself invalid (for example a duplicated alias; "The package sources are not configured correctly: ..."), which is a
problem and never "up to date" (`AgentControl-RepoCardViewModel-InvalidSourceConfigurationIsProblem`). The text for a
pin that nothing can supply says that the pinned source is not configured and that no other configured source delivers the
identical files - or, for a pin that recorded no identity of its files, that no other source can be matched to it -
and what to do about it (`AgentControl-RepoCardViewModel-UnresolvedPinExplained`). When set, no
upgrade is offered and no source whose files are not verified is ever used
(`AgentControl-RepoCardViewModel-SourceAwareness`); the main window shows it as a warning badge
on the card, with the reason as its tooltip. Set by the cheap refresh and by the upgrade check; cleared when
the pin or settings no longer have the problem. It is *not* set when another source supplies the files: the chip then
names that source (see *Portable pins* below).

**CurrentBranch**, **HasCommittedAgentFiles**: `string?`/`bool` — Resolved via `GitClient`,
with the committed-files result consulting the per-card `CommittedAgentFilesCache`
(`AgentControl-RepoCardViewModel-CommittedFilesBadge`). The badge is advisory for Launch (which skips the
sync and changes nothing); Select Package, Change package and Upgrade offer to convert such a repo instead (see
*Converting committed agent files* under `ApplyPackageAndShowReleaseNotesAsync`).

**IsUpgradeAvailable**, **LatestAvailableVersion**: `bool`/`string?` — Resolved via the shared
`PackageVersionCache` at the pin's source (`AgentControl-RepoCardViewModel-UpgradeBadge`).
**IsCheckingUpgrade** and **IsUpgradeStatusUnknown** ("checking" and "check failed") belong to the upgrade check
that is current: a private generation counter, `_upgradeCheckGeneration`, is incremented by every check that starts and
by `InvalidateUpgradeCheck`, and a check publishes its answer, or its failure, only while its own number is still the
current one (`AgentControl-RepoCardViewModel-StaleUpgradeCheckDiscarded`).

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

**PullTooltip**: `string` (derived) — The tooltip text of the Git Pull action, explaining why
Pull is currently disabled (missing repo, not yet checked, an undeterminable git status, or
dirty working tree) instead of leaving the action to silently disable with no
explanation; when Pull is enabled, returns the original neutral "Pull the latest commits for
this repo" text (`AgentControl-RepoCardViewModel-PullDisabledExplanation`). The Git Pull item of the card's
"..." menu is always present (never hidden via `IsVisible`), consistent with the
"stay visible, disable, explain" pattern already used for Launch/Upgrade/Select-Package when
the repo is missing. `MainWindow.axaml` sets `ToolTip.ShowOnDisabled="True"` on the item
so this explanatory text remains reachable while the item is disabled via
`PullCommand`'s `CanExecute` - Avalonia suppresses tooltips on disabled controls by default,
which would otherwise hide the explanation precisely when it is most needed. The item is the first entry of the
menu's "Repository" group and keeps its icon and the `PullButton` AutomationId.

**IsPackageSelectionNeeded**: `bool` (derived) — `PinnedPackageName is null`.

**Status** / **StatusText** / **StatusTooltip** / **LastActionFailed** (`AgentControl-RepoCardViewModel-StatusAccent`,
`-StatusWords`, `-StatusProblemCleared`):
`Status` is the card's overall `RepoCardStatus`, which the view shows as the colored accent bar on the card's
left edge. A problem outranks everything - the folder is missing (`IsMissing`), the pinned package's source has a
problem or needs a sign-in (`HasSourceProblem`), or the last action failed (`LastActionFailed`) - then
`TagMoved` (`IsPinnedTagMoved`: the tag of the pinned Git version now points to another commit, so the card never
says "up to date" or merely "upgrade available" for content that is no longer what the repo has), then
`UpgradeAvailable` (`IsUpgradeAvailable`), then `NoPackage` (`IsPackageSelectionNeeded`), then `Checking` (a
package is pinned but the upgrade check is still running, so the card never claims "up to date" early), then
`Unknown` (`IsUpgradeStatusUnknown`: the last upgrade check failed because the package source could not be read,
so nothing established that the card is current; kept until a later check succeeds), and otherwise `UpToDate`.
**ShowUpToDateCue** (`HasPinnedPackage && Status == UpToDate`,
`AgentControl-RepoCardViewModel-UpToDateCue`) tells the card to draw a small check mark - with the tooltip and
accessible name "Agent files are up to date" - inside its package chip, so a healthy card says so in words
without an extra chip or row; it is hidden for every other status so it never contradicts the rest of the card.
**RepoPathParent** and **RepoPathLeaf** (`AgentControl-RepoCardViewModel-RepoPathKeepsFolderName`) split `RepoPath` at
its last separator (the leaf keeps its separator, a
trailing separator is ignored, and a path without a separator is all leaf): the card's `PathEllipsisText` shortens the
parent with an end ellipsis first and the leaf with a middle ellipsis only when it still does not fit, which keeps
the start and the end of the folder name visible.
Green, amber, red and grey are only the view's rendering of that value: `StatusText` says
it in words ("Up to date", "Upgrade available", "Needs attention", "No agent package", "Checking for updates",
"Update check failed", "Tag moved")
and is the bar's accessible name, and `StatusTooltip` adds the reason (for a problem, the missing folder, the
source problem or "the last action failed"). `LastActionFailed` is set whenever the card raises `ErrorOccurred`
(the constructor subscribes to its own event, so no call site needs bookkeeping) and when a Git Pull ends with
git's refusal, and is cleared when the next operation starts (`RunOperationAsync`). **HasAgentFileActions**
(`IsPackageSelectionNeeded || IsUpgradeAvailable || HasPinnedPackage`) lets the card menu hide its "Agent files"
group when it has nothing to offer: "Select Package...", "Change package..." or "Upgrade to ...".

**Avatar tile** (`AgentControl-RepoCardViewModel-AvatarTile`, `-AvatarFolderGlyph`, `-AvatarColorStable`):
**AvatarInitials** are the capital first letters
of the first two words of `RepoName` (words are runs of letters and digits, so `alpha-service` gives `AS`), or
one letter for a one-word name, and **HasAvatarInitials** is `false` when the name has none (the view then shows
a folder glyph). **AvatarColorIndex** (and **AvatarStyleClass**, `avatar0` ... `avatar9`) picks one of
`AvatarPaletteSize` palette entries: the color the user chose (**CardColorName**) when there is one, otherwise a
32-bit FNV-1a hash of the repo path compared without regard to case or
a trailing separator; `string.GetHashCode` is deliberately not used because it is randomized per process, which
would change a repo's color on every launch. The view model carries no color: the style class selects theme-aware
`AvatarBackgroundN`/`AvatarForegroundN` resources (letter color chosen for contrast) from `AppStyles.axaml`.

**Group and card color** (user-specific; stored only on the repo's `RecentRepo` in the per-user settings, never in
the repository or its pin file). **GroupName** reads the stored name through `RepoGroupName.Sanitize` (a blank,
overlong or reserved stored value yields a valid name or none), **HasGroup** is `GroupName is not null`, and
**ShowGroupChip** (`IsFavorite && HasGroup`, `AgentControl-RepoCardViewModel-GroupChipForFavorites`) tells the card to
show its group as a chip - needed only for a favorite,
because a favorite is listed under Favorites instead of under its group. **MoveToGroup(name)** moves the repo into a
group or, with `null`/blank, out of every group: the name is cleaned up, refused (returns `false`, nothing changes)
when `RepoGroupName.Validate` fails (too long, or *Favorites*/*Ungrouped*), and given the spelling of an existing
group that matches ignoring letter case (`GetExistingGroupNames()`, from the groups of all repos in the live
settings, `AgentControl-RepoCardViewModel-GroupNameRules`, `-GroupList`); a real change is made through the owner
(see *Changes go through the owner*) and, once saved, raises `GroupChanged` for `MainWindowViewModel` to re-section
(`AgentControl-RepoCardViewModel-MoveToGroup`). **NewGroupCommand** raises `NewGroupRequested` (the view shows the
name prompt and calls `MoveToGroup`; `-NewGroupPrompt`); **RemoveFromGroupCommand** (available while `HasGroup`) calls
`MoveToGroup(null)`. **CardColorName** is the palette color's name read through `CardColorPalette.Find` - a missing,
blank or unknown stored name gives `null`, the automatic color (**HasCustomCardColor**, **IsCardColorAutomatic**,
`-CardColorStoredFallback`) -
and **SetCardColor(name)** stores a palette name (letter case ignored; refused with `false` when not in the palette,
`-CardColorUnknownRefused`)
or `null` for Automatic, through the owner, then raises `CardColorChanged` and changes
`AvatarColorIndex`/`AvatarStyleClass` (`-CardColor`, `-CardColorRecorded`). Only the tile
changes: **Status**, the accent bar and every chip are independent of the color
(`AgentControl-RepoCardViewModel-CardColorKeepsStatus`). `CardColorPalette` is a small static helper of this unit: ten named
entries (Blue, Violet, Magenta, Green, Amber, Teal, Orange, Indigo, Red, Lime) whose position is the index of the
theme-aware `AvatarBackgroundN`/`AvatarForegroundN` resources, so a chosen color is the same palette the automatic
colors come from. Where the repo shows its own picture the tile keeps its light backing (so a logo drawn for a
white page stays legible) and a chosen color is drawn as a 3 px ring around the picture (the `customColor`
class); with no picture the color fills the tile and the letters use the entry's foreground resource.

**Repo picture** (`AgentControl-RepoCardViewModel-RepoPicture`): **RepoImage** (`Bitmap?`), **HasRepoImage**,
**ShowAvatarInitials** and **ShowAvatarFolderGlyph** tell the tile what to show. `RefreshCheap` starts
`RefreshRepoImageAsync` (never for a missing folder, which just clears the picture,
`AgentControl-RepoCardViewModel-RepoPictureFallback`); on a worker thread it asks
`RepoImageLocator` for the repo's picture, and decodes it with `RepoImageLocator.Decode` unless it is the same
file (path, last-write time and size) as the one already decoded - a failed decode is remembered the same way
(`-RepoPictureNotDecodedAgain`) - and
then publishes the bitmap on the caller's context, so the UI thread never touches the disk or the decoder and
repeated refreshes cost one probe of the repo root. A newer refresh supersedes an older one (a generation counter,
`_imageGeneration`, taken when
the refresh starts and also advanced by a missing folder and by `Retire`), and any
exception from the locator, the decoder or an injected decoder - of whatever type - means "no picture", so the
background task never faults (`-RepoPictureFailureIsolated`). Only the latest refresh may clear the picture on such a
failure: an older refresh
failing after a newer one published leaves the newer picture alone. Which file is acceptable, and how it is checked
(the repo root, links, PNG header
limits, handle verification), is the `RepoImageLocator` unit's design; this view model relies on it to return
only a bounded, scaled bitmap and treats its result as untrusted-derived display data. The view letterboxes the
picture (`Stretch="Uniform"`) on a
light backing inside the same tile.

**Bitmap ownership** (`AgentControl-RepoCardViewModel-RepoPictureReleased`): the card owns the bitmap it publishes in
`RepoImage`. When the property is replaced or cleared, the superseded bitmap is disposed right after the change was
announced (`PropertyChanged` for `RepoImage`, `HasRepoImage` and the tile properties), by which time the bound tile no
longer references it. A bitmap that a refresh decoded but that a newer refresh overtook is never published, so the
refresh that decoded it disposes it. `Retire` clears the property, which disposes the bitmap that was shown.
Consumers must not keep the value beyond the moment they read it.

**Source access and asynchronous operations.** The card builds a `PackageSourceRegistry` from the
current settings' effective sources for each operation (`CreateSourceRegistry`) and awaits the
asynchronous providers, so reading a source, extracting a package and pulling never run on the UI
thread. `LaunchCommand`, `PullCommand`, `UpgradeCommand` and `RefreshCommand` are
`AsyncRelayCommand` instances: a second request while one runs is ignored
(`AgentControl-RepoCardViewModel-OneOperationAtATime`), and while one runs
**IsBusy** is set, **BusyMessage** says what is happening (`AgentControl-RepoCardViewModel-OperationBusyState`)
and **CanCancelOperation** lets
`CancelOperationCommand` cancel it (`AgentControl-RepoCardViewModel-CancelOperation`); package reads and
applies do not block the caller (`AgentControl-RepoCardViewModel-ResponsiveOperations`).
Canceling is not an error and leaves the repo's pin unchanged. NuGet sources keep their private
caches under a `nuget` folder inside the startup configuration-directory override when one is
supplied (so automated tests never touch the user profile), otherwise in the per-user default.
`CreateSourceRegistry(allowPrompt)` also hands NuGet server sources the application's
`PackageCredentialContext`: with `allowPrompt` true (a read the user started) the sign-in dialog may
open, with false (background work) the registry gets `WithoutPrompt()`, a context that shares the
session credentials and the user's saved sign-in (read without any dialog) but can never ask.

**Chip properties**: the view binds the card's chips to derived properties
(`AgentControl-RepoCardViewModel-DisplayInfo`, `AgentControl-RepoCardViewModel-UpgradeBadge`):

- *Package chip* — `HasPackageChip`, `PackageChipText` (package name and version),
  `PackageChipTooltip` (names the package, version, source alias and source kind of the source that supplies the
  files - the pinned one, or the one that stands in for it - and, when another source supplies them, adds the
  `SourceSubstitutionNote`: *Pinned source 'X' is not configured; the identical files come from 'Y'.* or *...does
  not offer {package} {version}; ...*; for a Git source it adds ", tag {tag} @ {commit}" - the tag is
  `PinnedTagName`, the source's tag pattern applied to the pinned version, and the commit is
  `PinnedCommitShort`, the first seven characters of the `git-commit:` source revision the pin recorded - and only
  ", tag {tag}" when the pin recorded no commit; a pin to any other kind of source names neither,
  `AgentControl-RepoCardViewModel-GitSourceAwareness`, `-GitSourceTooltip`), the source kind's icon from
  `PackageSourceIcons` (a server
  source has its own network-server icon, distinct from the zip-folder and NuGet-folder icons, and a Git source
  the Git logo), and
  `PinnedSourceLabel`, the alias of the source that supplies the files shown as muted text beside the icon so the
  source is recognizable at a glance without hovering (the pinned source's alias - a pin written before sources
  existed reports the legacy `Default` alias - or, when another source stands in for it, that source's alias;
  `AgentControl-RepoCardViewModel-ShowSupplyingSource`). Hidden for a missing repo or when nothing is pinned.
- *Signature shield* — `RequiresSignature` is `true` only when the source that supplies the package is a **NuGet**
  source whose
  definition requires signed packages (not for a zip source, a source that is not configured, or a source without
  the requirement), and `SignatureTooltip` says "Signature required (any trusted signer)" or, when the source pins a
  publisher, "Signature required (signer pinned: A3AF7AF1…7641)" naming only the start and end of the fingerprint
  (`NuGetSourceProvider.SummarizeFingerprint`). The main window shows a small shield icon inside the package chip
  while `RequiresSignature` holds, and exposes it to assistive technology under the same text, so the requirement is
  not conveyed by an icon alone. Both properties are derived from the pinned source's definition, so they are
  raised again whenever the card refreshes after the source changed
  (`AgentControl-RepoCardViewModel-SignatureIndicator`).
- *No-package chip* — `HasNoPackageChip` and its tooltip, shown instead of the package chip for a
  present repo with no pin; clicking it opens Select Package.
- *Upgrade chip* — `IsUpgradeAvailable`, `UpgradeChipText` (the version an upgrade would move to)
  and `UpgradeTooltip` ("Upgrade the agent files to x.y.z (pinned: a.b.c). Same as the "..." menu >
  Upgrade.", `AgentControl-RepoCardViewModel-UpgradeChipTooltip`); the chip is a button bound to `UpgradeCommand`, the
  same command as the menu's Upgrade item, so it has the same guards (only while an upgrade is available and no
  operation is running, `-UpgradeChipAvailability`) and the same
  confirmations (`AgentControl-RepoCardViewModel-UpgradeFromChip`); `IsCheckingUpgrade` shows that the
  background check is still running.
- *Branch chip* — the branch name with a tooltip.
- *Tag moved chip* — `IsPinnedTagMoved` and `PinnedTagMovedTooltip` (the tag, the source, the recorded and the new
  commit - seven characters each - what AgentControl did not do, and that the way to accept it is the menu's "Change
  package..." and choosing the version again). The amber chip is a button bound to `SelectPackageCommand`, so a click
  only opens the Change package dialog; it applies nothing
  (`AgentControl-RepoCardViewModel-TagMovedWarning`, `-TagMovedAppliesNothing`).
- *Status chips* — uncommitted changes, committed agent files, folder missing and package source
  problem (whose tooltip is the actual problem), each color-coded amber or red
  (`AgentControl-LauncherUI-StatusBadges`).
- *Launch label* — `LaunchButtonText` is "Launch" followed by the short display name of the
  configured agent tool, or plain "Launch" when the name is unknown or longer than ten characters
  (`AgentControl-RepoCardViewModel-LaunchLabel`).

**_logger**: `ILogger<RepoCardViewModel>` (private, via `AppLogging.Factory.CreateLogger<RepoCardViewModel>()`)
— Used to log the real work this view model performs (launch, pull, upgrade/apply-package,
AGENTS.md accept/decline), mirroring the logging convention already established by `GitClient`
and `AgentToolLauncher`. Every call site is guarded by `_logger.IsEnabled(...)`.

**LaunchCommand, PullCommand, UpgradeCommand, RefreshCommand**: `AsyncRelayCommand`;
**SelectPackageCommand, RemoveCommand, CancelOperationCommand, NewGroupCommand,
RemoveFromGroupCommand**:
`RelayCommand` — see Interfaces in `docs/design/agent-control/launcher-ui.md`.

#### Key Methods

**RefreshCheap**: Runs every cheap per-repo check (missing-repo, pin, the source problem of a pin
naming an unconfigured source, branch, committed-files badge via `HEAD`-hash cache). It is
synchronous and never reads a package source, so it is safe to call eagerly for every recent repo
at app launch; upgrade availability is determined separately by `RefreshUpgradeStatusAsync`.
A retired card returns at once. Otherwise it first calls `InvalidateUpgradeCheck`, which advances
`_upgradeCheckGeneration` so an upgrade check still in flight never publishes its answer over what this refresh
finds, and resets "checking" and "check failed"
(`AgentControl-RepoCardViewModel-StaleUpgradeCheckDiscarded`, `-UpgradeStateResetOnRefresh`); the caller starts a
new check when one is wanted. When the pin it reads (name, version or source) differs from the one before, it also
drops `IsUpgradeAvailable` and `LatestAvailableVersion`, which described the old pin, and when the repo has become
missing it clears the upgrade state, so a late answer cannot revive it.
Deliberately excludes the
working-tree dirty check, which is not cacheable and is deferred/lazy per architecture.md's
repo-fact caching strategy (`AgentControl-RepoCardViewModel-CommittedFilesBadge`). When the repo is found to be
missing, also invalidates `GitStatusChecked`/`GitStatusUnavailable` (not just `CanPull`), since
a cached Git-status result from before the repo disappeared is no longer trustworthy once it
reappears; this method itself never re-checks Git status on a missing-to-present transition
(that would duplicate the check already performed by `RefreshCommand`/`Refresh`'s subsequent
`RefreshDirtyStatus` call), so a caller that invokes `RefreshCheap` alone for an
already-realized card (see `MainWindowViewModel.ApplySettings`) must call `RefreshDirtyStatus`
itself afterward to get an immediate fresh check instead of leaving the card in a "not yet
checked" state until its next manual refresh
(`AgentControl-RepoCardViewModel-PullDisabledExplanation`).

**Retire** (internal): Stops the card for good because its repo was removed from the recent-repos list; called by
`MainWindowViewModel.RemoveRepo` once the removal was saved (and for a card prepared by `AddRepo` whose save failed).
It marks the card retired, advances `_upgradeCheckGeneration` and `_imageGeneration` so results still on their way are
dropped, clears "checking", cancels the running operation (`CancelOperation`; the operation ends cancelled, which is
never reported as an error), sets every event of the card (`ErrorOccurred`, `ReleaseNotesReady`,
`AgentsMdTemplateOfferRequested`, `FavoriteChanged`, `GroupChanged`, `CardColorChanged`, `LaunchRecorded`,
`NewGroupRequested`, `RemoveRequested`, `SelectPackageRequested`, `ContentChangeConfirmationRequested`,
`CommittedFilesConversionRequested`) to `null`
so that nothing it still does can show an
error box, release notes or a prompt, and clears the picture, which disposes it
(`AgentControl-RepoCardViewModel-RetiredCardReportsNothing`, `-RetireCancelsRunningWork`). A retired card ignores
`RefreshCheap` and `RefreshUpgradeStatusAsync` (`-RetiredCardChecksNothing`), is never reused, and `Retire` is
idempotent; adding the repo again creates a new card.

**RefreshDirtyStatus**: Re-checks working-tree cleanliness via `GitClient`, updating `CanPull`,
`GitStatusUnavailable`, and `GitStatusChecked`. Not called by `RefreshCheap` or the
constructor — computed lazily instead, on demand via `RefreshCommand` or once when a card
first becomes visible.

**Refresh** / **RefreshAsync**: Convenience entry point equivalent to `RefreshCheap` followed by
`RefreshDirtyStatus` and the upgrade check, for callers (e.g. adding a brand-new repo) wanting an
immediate full refresh; `RefreshCommand` runs it asynchronously.

**RefreshUpgradeStatusAsync**: Checks, in the background, whether the pin's source offers a
newer version than the pinned one, using the shared cache - and, when the pin's source is not available, which other
source supplies the pinned files and what the configured sources offer (see *Portable pins*). A retired card returns
at once. Each check takes a new
generation number when it starts, and a check started later supersedes one
still waiting (`AgentControl-RepoCardViewModel-SupersededUpgradeCheck`), so a result for settings that have
since changed is discarded; a source configuration that is invalid is shown as a problem
(`-InvalidSourceConfigurationIsProblem`) and cancellation leaves
the previous state in place (`AgentControl-RepoCardViewModel-CancelOperation`). Its
`allowPrompt` parameter defaults to `false`, which is what the background check at start-up, after a
repo is added and after settings change uses: it reads a server through the remembered session
credentials and **never opens the sign-in dialog**. A server that then answers that it needs a sign-in
(`PackageSourceFailureKind.AuthenticationFailed`) sets `SourceProblem` to the provider's secret-free message
followed by *Use the card's "..." menu > Refresh to sign in*, so the card says what to do instead of
interrupting the user. The card's **Refresh** command runs the same check with `allowPrompt` true, and the
Select Package and Upgrade actions the user chooses use a source registry that may open the dialog
(`AgentControl-RepoCardViewModel-BackgroundChecksNeverPrompt`). The check result carries the listing's entry for the
pinned version (`UpgradeCheck.Pinned`). For a Git source whose pin recorded a `git-commit:` source revision, a listing
that
shows the pinned version at a different commit (`FindMovedTagCommit`, comparing the pin's `SourceRevision` with
`PackageContentIdentity.FromGitCommit(listing revision)`) sets
the moved-tag state through `SetMovedTag`, which notifies `IsPinnedTagMoved`, the tooltip and the status; nothing is
read, extracted or written because of it, and the state is cleared whenever the pin changes (`RefreshPin`), the
listing no longer differs, a check fails (the tag state is then unknown, so an earlier warning must not stay up as if
it had been confirmed) or the repo's folder is missing (`AgentControl-RepoCardViewModel-TagMovedWarning`,
`-TagMovedClearedByNewPin`, `-TagMovedClearedWhenUnknown`). Only a well-formed `git-commit:` revision (40 or 64 hex
digits) is compared: a pin without a revision, a pin whose revision is of another kind or malformed, a listing without
a revision, a source that is not a Git source (the check is made only against the pinned Git source, never against a
stand-in) and a tag that still points to the recorded commit are never reported as
moved (`-TagMovedNeedsEvidence`). A pin an older version wrote - whose commit was recorded as its identity - is read
with that commit as its source revision, so its moved tag is still seen. The tag of the pinned version is not stored in
the pin: it is derived from the
source's tag pattern as configured now (`PinnedTagName`). Editing the pattern therefore maps the pinned version to
another tag, and when that tag names a different commit than the pin recorded it is reported as a moved tag; the user
accepts it, or not, with "Change package..." (`-TagMovedFollowsTagPattern`).

The session's `PackageVersionCache` answers the background check, which is cheap, from the listing it already read.
A moved tag is only visible in a fresh listing, so the optional `relistGitSource` parameter (true for the
**Refresh** command and for the status check that follows an applied package) discards the cached entries of the pin's
source - `PackageVersionCache.Invalidate(sourceAlias)`, which also stops a read already in flight from putting an
older answer back - before the check, but only when the pinned source is a Git source; the versions of other kinds
of source are immutable by convention and keep using the cache (`-TagMovedFreshListing`). A `GitRemoteException` of
kind `GitTooOld` found by the check
becomes a source problem with the exception's secret-free message and no sign-in hint
(`AgentControl-RepoCardViewModel-GitTooOldProblem`, `-GitTooOldNoUpgrade`).

**Launch**: Launches the configured agentic CLI tool in the repo's working directory.

- *Parameters*: None (bound to `LaunchCommand`).
- *Returns*: `void`.
- *Postconditions*: On success, `StatusMessage` is set and the launch time is recorded through the owner
  (`RecordLaunched`: the stamp is saved first, then `LastLaunchedUtc` is set and `LaunchRecorded` raised; when the
  save fails the stamp is not recorded and the process was launched all the same,
  `AgentControl-RepoCardViewModel-Launch`, `-ChangeSavedBeforeApplied`); on failure, `ErrorOccurred` is raised.

Calls `EnsureAgentFilesSyncedBeforeLaunchAsync` purely for its best-effort sync side effect, then
always proceeds to resolve the shell/agent command and spawn the process regardless of that
call's outcome — the agent-package sync state (missing pin, missing managed folders, failed
re-extraction) never gates the launch, and neither does the user canceling the wait for the sync.

**EnsureAgentFilesSyncedBeforeLaunchAsync** (internal): Best-effort attempt to sync the repo's four
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
  sync genuinely happens. Otherwise resolves the *currently pinned* package at the source the pin
  names when that source is configured and offers the pinned version (never the latest) - or, when it is not
  configured or does not offer that version, at another configured source verified to deliver the identical files (see
  *Portable pins*; never for a pin without a files identity, and a pinned source that delivers other files for the
  version is a republished package, not a reason to look elsewhere) - and re-extracts it directly via
  `PackageContentExtractor.Extract` over the reader the provider returns,
  without displaying release notes but still calling `MaybeOfferAgentsMdTemplate`; if this
  re-extraction attempt fails for any reason, `ErrorOccurred` is raised as a non-blocking
  warning (`AgentControl-RepoCardViewModel-EnsureSyncedBeforeLaunch`); when no source can supply the package the
  warning says that the pinned source is not configured (or does not offer the version) and no other configured source
  delivers the identical files (`AgentControl-RepoCardViewModel-UnresolvedPinExplained`). Before it extracts from the
  pinned source, the content identity of
  the reader is compared with the one the repo's pin recorded (`PackageContentIdentity.Compare`): when the version
  the repo is pinned to now delivers different files, nothing is extracted, a warning is logged and
  `ErrorOccurred` says in plain language that the agent files were not synced because the package may have been
  republished and that the way to accept the change, if it is trusted, is the card menu's "Change package..."
  (it no longer suggests deleting the pin file), and the method returns `false`
  (`AgentControl-RepoCardViewModel-RefuseChangedContent`). A pin that
  recorded no identity of its files (or a reader that reports none) is not comparable and is synced without any
  warning. The
  restore **never writes the pin**, whatever the outcome - and so never rewrites the alias when another source
  supplied the files - because recording an identity here would change a file under
  version control every time the tool is launched, so such a pin adopts an identity only at the next Select Package,
  Change package
  or Upgrade (`AgentControl-RepoCardViewModel-LaunchSyncNeverWritesPin`,
  `AgentControl-RepoCardViewModel-AdoptMissingIdentity`). That includes the AGENTS.md template offer the restore may
  raise: the card remembers that the open offer came from the restore, and a decline of such an offer is not
  persisted (the offer can only return when a later restore happens), whereas declining the offer raised by an
  explicit apply is recorded in the pin as before.
- Marked `internal` rather than `private` so it can be unit-tested directly, separated from
  `Launch`'s process-spawning side effect, mirroring `AgentToolLauncher.BuildProcessStartInfo`'s
  own precedent.

**Pull**: Runs `git pull` in the repo, then refreshes working-tree status
(`AgentControl-RepoCardViewModel-Pull`). Reports success/failure via `StatusMessage`, never
throwing for a failed pull.

**Upgrade**: Finds the latest package at the pin's source for the pinned package name, then
applies it via `ApplyPackageAndShowReleaseNotesAsync` (`AgentControl-RepoCardViewModel-Upgrade`). An
unconfigured source or a failing source is reported through `ErrorOccurred`.

**ApplySelectedPackageAsync**: Resolves the exact user-selected package at the chosen source,
then applies it via `ApplyPackageAndShowReleaseNotesAsync`.

- *Parameters*: `PackageReference selection` (source alias, package name, version), a
  cancellation token and `acceptContentChange` (default `false`).
- *Postconditions*: On success, the pin and release notes are updated exactly as `Upgrade`
  (`AgentControl-RepoCardViewModel-ApplySelectedPackage`), the pin recording the selection's source;
  on a version no longer available at the source, raises `ErrorOccurred` without mutating the
  existing pin; cancellation leaves the pin unchanged. When the selection is the version the repo is pinned to
  and its content differs from the recorded identity, nothing is changed and
  `ContentChangeConfirmationRequested` is raised with the selection and a plain-language question; the view layer
  calls this method again with `acceptContentChange: true` if the user agrees
  (`AgentControl-RepoCardViewModel-ConfirmChangedContent`, `-ApplyConfirmedContent`). With no subscriber there is nobody
  to ask, so the change is refused with `ErrorOccurred` instead (`-RefuseChangeWithoutAsking`).

**ApplyPackageAndShowReleaseNotesAsync** (private): Shared extract-and-pin sequence reused by
`Upgrade` and `ApplySelectedPackageAsync`: first loads the repo's existing pin (a newer-schema pin
aborts here with its actionable message while the repo is still untouched; a corrupt one is
tolerated and replaced), then - before anything is acquired - asks git which managed folders are tracked and, if
any are, asks the user (see **Converting committed agent files** below), acquires the package from its provider,
compares the acquired content's identity with the
pin's when the pin is for this very package version (`ContentChangedSincePinned`; a different version, a pin
without an identity and a reader without one are never a change) and, on a change, applies the requested
`ContentChangeHandling` before anything is touched - `Refuse` (Upgrade: log a warning, `ErrorOccurred` saying the
package may have been republished, that nothing was changed and that "Change package..." accepts it, without a hash
or any advice to edit the pin file;
`AgentControl-RepoCardViewModel-RefuseChangedContent`, `-RefuseChangedContentMessage`), `Ask` (Select Package / Change
package: raise
`ContentChangeConfirmationRequested`, return) or `Accept` (the user already confirmed: log and continue); then, for
a repo whose managed folders are tracked and whose conversion the user confirmed, stops git tracking them
immediately before it extracts the package
(blind-delete-and-replace), proactively ensures the repo's `.gitignore` covers the four managed
agent folders (via `EnsureGitIgnoreCoversManagedFolders`, its own non-blocking step — see Error
Handling), updates the loaded pin in place with the package's source alias (the source actually used), name, version,
content identity and source revision (replacing whatever was recorded before, including nothing or an older kind of
identity, `AgentControl-RepoCardViewModel-RecordContentIdentity`) and
saves it (so `AgentsMdTemplateDeclined` and any unknown fields are preserved), refreshes
pin/upgrade-status properties, calls `MaybeOfferAgentsMdTemplate` and finally raises
`ReleaseNotesReady` with the new package's release notes. The offer is raised *before* the release notes so
the view can show the modal question at once and hold the release notes back until it is answered
(`MainWindow.ShowReleaseNotesAsync`): the notes window then opens in front as the completion of the upgrade
instead of being covered by, or lost behind, the question
(`AgentControl-RepoCardViewModel-AgentsMdOfferBeforeReleaseNotes`). The acquired package reader stays open until the
sequence ends.

**Converting committed agent files** (inside `ApplyPackageAndShowReleaseNotesAsync`): Putting a repo under
package management is impossible while git tracks its managed folders, because `.gitignore` has no effect on tracked
files - the package's files would be invisible to git while the old ones showed as modified or deleted, and a commit
of everything would record a broken mix. The sequence therefore converts such a repo, deliberately:

1. *Fresh check* (`FindTrackedAgentFoldersAsync`): on a worker thread, `GitClient.GetTrackedAgentFolders` asks git
   which of the four folders have tracked files, right now. The cached `HasCommittedAgentFiles` badge is never
   consulted, because it is keyed by the `HEAD` commit and does not notice files staged since. A git that cannot
   answer (not installed, not a repository) counts as "nothing tracked", exactly as the badge treats it, and the
   apply continues as before (`AgentControl-RepoCardViewModel-CheckTrackedFoldersFresh`,
   `-NoConvertWhenNotTracked`).
2. *Ask before anything is acquired* (`ConfirmCommittedFilesConversionAsync`): when folders are tracked, the card
   raises `CommittedFilesConversionRequested` with a `CommittedFilesConversionRequest` whose message names the repo,
   the tracked folders and the package, says AgentControl will stop tracking the folders in git (the files stay on
   disk until the package replaces them), add them to `.gitignore` and apply the package, and that the result is to
   be committed afterwards. The request is answered in place: the operation awaits `request.Answer` while it holds
   the card busy (`IsBusy`), so the card's other actions stay disabled and the operation stays serialized - unlike
   the content-change question, which ends the operation and re-enters it with `acceptContentChange`. With no
   subscriber nobody can be asked, so `ErrorOccurred` explains that confirmation is needed and nothing is changed
   (`-ConfirmBeforeConverting`, `-NeverConvertWithoutAsking`). The question is asked once the existing pin is read
   and before the package is acquired, so declining costs nothing.
3. *Declined*: the method returns having acquired nothing and changed nothing - no git change, no file, no
   `.gitignore`, no pin - and `StatusMessage` says the action was canceled and nothing was changed
   (`-DeclineConvertChangesNothing`).
4. *Agreed*: the package is acquired and passes the content-identity checks as before. A republished version asks its
   own question and ends the apply, so nothing has been untracked at that point; the user's confirmation of the
   changed content re-enters the apply, which asks about the conversion again, because the conversion is only
   confirmed for the apply that carries it out. Immediately before extraction `UntrackAgentFoldersAsync` calls
   `GitClient.UntrackAgentFolders` for exactly the tracked folders (`git rm -r --cached`, index only), so a package
   that could not be fetched or was refused leaves git as it was (`-ConvertTrackedFolders`). A failure is rethrown as
   an `InvalidOperationException` saying AgentControl could not stop git tracking the folders and nothing was
   changed, with git's message; the outer handler reports it through `ErrorOccurred`, and the apply stops before
   any file is replaced, `.gitignore` or the pin is written (`-StopWhenUntrackFails`). If the extraction then fails
   (for example the extractor refuses the package's layout), `RestoreAgentFolderTrackingAsync` puts the folders back
   into git's index with `GitClient.RestoreAgentFolderTracking` (best effort, logged on failure) before the error is
   reported, so a failed apply never leaves the folders' removal staged and no pin is written
   (`-RestoreTrackingWhenApplyFails`).
5. *Afterwards*: the extraction, `EnsureGitIgnoreCoversManagedFolders`, the pin write and the release notes run as
   always. Because git's index changed, a `finally` clause on every way out of the apply invalidates the card's
   `CommittedAgentFilesCache` entry, re-runs `RefreshBranchAndCommittedFiles` (so the "Committed agent files" chip
   clears - `git ls-files` reads the index) and `RefreshGitStatus` (so the "Uncommitted changes" chip shows,
   expectedly). On success `StatusMessage` is the usual success message followed by a sentence saying the agent
   folders are no longer tracked by git and that committing the change (the deletions, `.gitignore` and
   `.agentcontrol.json`) finishes it (`-ConvertedRepoStatus`).

Launch behavior is unchanged: `EnsureAgentFilesSyncedBeforeLaunchAsync` still skips the sync for a repo whose badge
shows committed agent files and never converts anything.

**MaybeOfferAgentsMdTemplate** (private): Decides whether to offer the package's optional
root-level `AGENTS.md` template, called immediately after every successful extraction in both
`ApplyPackageAndShowReleaseNotesAsync` and `EnsureAgentFilesSyncedBeforeLaunchAsync`'s re-extraction
branch — never as an independent, standing per-launch poll.

- *Parameters*: `IPackageContentReader reader` — the package just successfully extracted.
- *Returns*: `void`.
- *Postconditions*: Does nothing if the repo already has a root-level `AGENTS.md` file, or if
  the repo's pin records a prior decline (`AgentsMdTemplateDeclined == true`), or if the
  package has no root-level `AGENTS.md`. Otherwise reads the template via
  `PackageContentExtractor.ReadAgentsMdTemplate` and raises `AgentsMdTemplateOfferRequested` with
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
`ApplyPackageAndShowReleaseNotesAsync`, before the pin-file write. Deliberately wrapped in its own
try/catch, separate from `ApplyPackageAndShowReleaseNotesAsync`'s outer catch, so a `.gitignore` I/O
failure can never abort the pin write or release-notes display that follow it — mirroring the
non-blocking idiom already established by `EnsureAgentFilesSyncedBeforeLaunchAsync`.

**SelectPackage** (private, bound to `SelectPackageCommand`): Validates at least one package source is configured,
then raises
`SelectPackageRequested` (a plain event) for the view layer to show the dialog, which starts on
the source the repo is pinned to when it has one
(`AgentControl-RepoCardViewModel-SelectPackage`). The command is executable for any repo that is present and idle,
whether or not it has a pin: for an unpinned repo it is "Select Package...", for a pinned one "Change package..."
(`AgentControl-RepoCardViewModel-ChangePackage`, `-ChangePackageRequest`). Raising the event never reads or writes the
pin; the view layer
passes the pin's source, package and version to the dialog so it opens on them, and the confirmed choice comes back
through `ApplySelectedPackageAsync`, where choosing the pinned version again is the explicit way to accept changed
content (`ConfirmChangedContent`; for a Git source the content differs when the tag now delivers a different commit).

**EvaluatePinnedSource** (private): Part of the cheap refresh. It clears the source problem and decides what can be
settled without reading a source. A pin whose source is configured goes on to the asynchronous check. A pin whose
source is *not* configured goes on too - without any problem set yet - when it recorded a files identity and at least
one source is configured, because only reading the sources can tell whether another one delivers the identical files;
otherwise (no identity to verify against, or no source at all) `SourceProblem` is set at once with the explanation
(`AgentControl-RepoCardViewModel-FallbackNeverWithoutIdentity`, `-UnresolvedPinExplained`). A stand-in source that is
no longer configured is dropped. An unreachable or failing source is
reported by the background upgrade check as "no upgrade offered".

**Portable pins** (`AgentControl-System-PortablePin`, `AgentControl-System-SourcePinIntegrity`). The alias in a pin is
user-specific configuration, so it is a *preferred* source, resolved in this order wherever the pinned package is
acquired or checked (`FindVerifiedSourceAsync`):

1. If the pinned alias is configured and offers the pinned package and version, that source is used, with the
   identity check as always - a version it now delivers with other files is refused as republished, never handed to
   another source.
2. Otherwise - the alias is not configured, or it does not offer that version - and only if the pin recorded a files
   identity (`PackageContentIdentity.IsFilesIdentity`; a pin from an older version, or a source that named none, has
   nothing to verify against), the other configured sources are tried in Settings order. The candidates come from
   `PackageVersionCache.GetVersionsAcrossSourcesAsync` (excluding the pinned alias), so no source is read twice and
   a source that cannot be read is skipped; of those, only the ones that list the pinned name and version are
   *acquired*, and the first whose reader's `ContentIdentity` compares `Same` with the pin's is accepted
   (`-FallbackToIdenticalSource`). A candidate that delivers other files is disposed and passed over
   (`-FallbackRejectsDifferentFiles`); one that fails to acquire is skipped. Acquisition happens only here, so
   nothing is downloaded or fetched when the pinned source works.
3. No verified match gives today's behavior: the red "Package source problem" chip, or the non-blocking launch warning,
   with the explanation described under **SourceProblem**. This holds in the upgrade check too when the pinned alias
   *is* configured but does not offer the pinned version (and the version parses as one): the card sets the source
   problem ("The pinned source 'X' does not offer {package} {version}, and no other configured source delivers the
   identical files.", with " Upgrade to Y to move to a version it offers." appended when it offers a newer one) and
   never shows "up to date". Unlike an unconfigured alias this source *answered*, so a newer version it offers is
   still offered as an upgrade (`IsUpgradeAvailable`/`LatestAvailableVersion` come from its listing): upgrading is the
   natural way out of the state. The red status outranks the amber upgrade state in `Status` and the accent bar, but
   the upgrade chip and the menu's Upgrade item are bound to `IsUpgradeAvailable` alone, so both stay reachable while
   the problem chip is shown; with no newer version nothing is offered
   (`AgentControl-RepoCardViewModel-UnresolvedPinExplained`).

A verified stand-in is never written into the pin (the launch never writes it, and the alias remains the preferred
source for the next user): the card remembers its alias, `_supplyingSource`, and `PinnedSourceLabel`, the icon, the
signature shield and the tooltip then describe the source that really supplies the files, with the tooltip's
`SourceSubstitutionNote` saying why; no source problem is shown
(`-ShowSupplyingSource`). When the pinned alias supplies the package again (the user added the source) the next check
drops the stand-in. Credentials are unchanged: the background check uses a registry that never prompts, so a
candidate that needs a sign-in is simply one that cannot be verified.

*Upgrades across sources.* If the pinned alias is configured, the upgrade check is as before. If it is not (and a
stand-in was verified), `RefreshUpgradeStatusAsync` asks `PackageVersionCache.IsNewerVersionAvailableAcrossSourcesAsync`

- the newest version across the configured sources wins, a tie keeps Settings order - and `UpgradeCoreAsync` upgrades
from the source that offers it, so the pin then records the alias of the source actually used, like every deliberate
action (Select, Change, Upgrade) does (`-UpgradeAcrossSources`). Moved-tag detection stays tied to the pinned alias
being a configured Git source.

**CreateSourceRegistry**: Besides the credential context, the registry is given the Git options: the git executable
(the setting, else the `--git-path` override, else `git`), the cache root (`GetGitCacheRoot`: a `git-cache`
folder inside the
`--config-dir` when one was given, so automated tests never touch the real profile) and `Interactive = allowPrompt`.
Work the user started (Select Package, Upgrade) is therefore interactive; the background upgrade check and the restore
before launch are not, and a missing Git sign-in there is a source authentication failure, which the card already
presents as "sign-in needed" (`AgentControl-RepoCardViewModel-GitSourceOptions`, `-GitSourcePrompts`,
`-GitSourceCacheRoot`, `AgentControl-LauncherUI-GitSourceSetup`).

#### Error Handling

`Launch` catches `InvalidOperationException`/`ArgumentException` from
`AgentToolLauncher.BuildProcessStartInfo`/`Launch` and raises `ErrorOccurred`.
`EnsureAgentFilesSyncedBeforeLaunchAsync` catches `InvalidOperationException` (extraction failure, an invalid source
configuration or a failure of the pinned source) and `DirectoryNotFoundException`
(unreachable source), raising `ErrorOccurred` as a non-blocking warning for each rather than
propagating or blocking the launch. `Pull` catches `InvalidOperationException` from `GitClient.Pull`
and reports it via `ErrorOccurred`. `RefreshGitStatus`/`RefreshBranchAndCommittedFiles` catch
`InvalidOperationException` from `GitClient` and degrade to "unknown"/`false` rather than
propagating, since these run as part of routine, frequent UI refreshes.
`ApplyPackageAndShowReleaseNotesAsync` catches `InvalidOperationException` and
`DirectoryNotFoundException`, raising `ErrorOccurred` without applying a partial pin update (a failure of
`GitClient.UntrackAgentFolders` is rethrown with an explanation that nothing was changed and so stops the apply
before any file is replaced; a git that cannot answer the fresh tracking check is treated as "nothing tracked");
its
read of a prior pin's `AgentsMdTemplateDeclined` value is guarded by its own inner try/catch that
treats an `InvalidOperationException` (corrupt/unreadable existing pin file) as "no persisted
decline" (defaults to `false`) rather than letting it propagate to the outer catch and abort the
apply after extraction has already succeeded; the subsequent `RepoPinStore.Save` call's own failure
handling is unchanged. `EnsureGitIgnoreCoversManagedFolders` catches `InvalidOperationException`
from `GitIgnoreEnsurer.Ensure` separately, in its own nested try/catch, raising `ErrorOccurred` as a
non-blocking warning without ever propagating — a `.gitignore` I/O failure never aborts the pin
write or release-notes display. `MaybeOfferAgentsMdTemplate` catches
`InvalidOperationException`/`ArgumentException`/`NotSupportedException` (covering both
`PackageZipExtractor.ReadAgentsMdTemplate` and `PathHelpers.SafePathCombine` failure modes) in its
own non-blocking try/catch, logging nothing further and never propagating. `AcceptAgentsMdTemplate`
writes via `FileMode.CreateNew` in a step isolated from the subsequent write/flush; only that
creation step's `IOException` is guarded by a check that the target file now exists (another
user/process created it after the offer was raised but before this call ran) to report a
non-destructive, informational `StatusMessage` rather than an error. A failure during the
write/flush step that follows a successful create is always a genuine error (e.g. disk full) and is
never misreported as "already exists", since it is handled by a separate, later catch. Any
`IOException`/`UnauthorizedAccessException`/ `ArgumentException`/`NotSupportedException` from either
step (other than the guarded create-time "already exists" case) raises `ErrorOccurred` rather than
throwing (it never calls `RepoPinStore`, so `InvalidOperationException` cannot occur on this path).
`DeclineAgentsMdTemplate` catches those same four exception types plus `InvalidOperationException`
from `RepoPinStore.Load`/`Save` and likewise raises `ErrorOccurred` rather than throwing. The
constructor throws `ArgumentNullException` for a null `recentRepo`, `getSettings`, or
`packageVersionCache`. `ApplySelectedPackageAsync` throws `ArgumentNullException` for a null
`selection`. `MoveToGroup` and `SetCardColor` never throw: a name that is not valid, or not a palette color, is
refused by returning `false` and changing nothing, and stored values that are missing, blank or unknown read as
"no group" and the automatic color (`GroupName` is sanitized on every read). A change the owner could not save is not
an exception either: the owner reports it to the user, the callback returns `false`, and the card stays as it was
(`MoveToGroup` and `SetCardColor` return `false` for it, as for a refused name). The picture refresh never faults the
background task, and a retired card raises nothing.

#### Dependencies

- **RepoGroupName** (`Settings` subsystem) — cleans up, validates and repairs group names, and unifies the
  spelling of groups that differ only in letter case.
- **RepoImageLocator** (`LauncherUI`) — finds and decodes the repo's picture, enforcing every rule about what
  may be shown.
- **RepoPinStore** (`RepoConfig` subsystem) — reads/writes the repo's pin, including the recorded content
  identity and source revision.
- **PackageContentIdentity**, **IPackageContentReader.ContentIdentity** / **SourceRevision**
  (`AgentPackageManagement`/`RepoSync` subsystems) — the identity of the files a source delivered, the revision a Git
  tag pointed to, and the comparison with the recorded identity (also what verifies another source).
- **PackageSourceRegistry**, **IPackageSourceProvider**, **PackageVersionCache**,
  **DiscoveredPackage**, **PackageSourceAccessException** (`AgentPackageManagement` subsystem) — source
  resolution at the pin's alias, package discovery (also across sources), acquisition, upgrade-availability checks, and
  the failure cause that distinguishes "needs a sign-in" from other failures.
- **PackageCredentialContext** (`PackageCredentials` subsystem) — the session credentials and sign-in prompt
  handed to server sources; `WithoutPrompt` for background work.
- **PackageContentExtractor**, **PackageZipExtractor** (`RepoSync` subsystem) — extracts a
  package into the repo, reads its release notes, and reads an optional root-level `AGENTS.md`
  template via `ReadAgentsMdTemplate`; `PackageZipExtractor.AllManagedFoldersExist` checks whether
  a re-extraction is needed.
- **GitIgnoreEnsurer** (`RepoSync` subsystem) — proactively ensures the repo's `.gitignore`
  covers the four managed agent folders after a successful extraction.
- **PathHelpers** (`Utilities` subsystem) — used by `MaybeOfferAgentsMdTemplate` and
  `AcceptAgentsMdTemplate` to safely combine the repo root with the fixed `AGENTS.md`
  filename before any read or write.
- **GitClient**, **CommittedAgentFilesCache** (`GitIntegration` subsystem) — git status,
  branch, and committed-files queries, the fresh check of which managed folders git tracks and the removal of
  those folders from git's index when a repo is converted.
- **CommittedFilesConversionRequest** (`LauncherUI`, own file) — carries the conversion question to the view layer
  and the answer back to the waiting operation.
- **ShellDetector**, **AgentToolLauncher** (`AgentToolLauncher` subsystem) — shell detection
  and process launch.
- **AppLogging**, **ILogger** (`Logging` subsystem) — `_logger` logs the real business
  operations this view model performs (launch, pull, upgrade/apply-package, AGENTS.md
  accept/decline), mirroring the convention already established by `GitClient` and
  `AgentToolLauncher`.
- **SelectPackageWindowViewModel** (`LauncherUI` subsystem) — constructed by the view layer
  in response to `SelectPackageRequested`; its `Confirmed` event is wired to
  `ApplySelectedPackageAsync`.
- **AsyncRelayCommand**, **PackageSourceIcons** (`LauncherUI` subsystem) — back the asynchronous
  commands and supply the source-kind icon of the package chip.

#### Callers

- **MainWindowViewModel** — constructs one `RepoCardViewModel` per `RecentRepo`, passing its `commitChange` callback
  (which saves each of the card's changes before applying it), subscribes to
  `FavoriteChanged`/`GroupChanged`/`LaunchRecorded` to re-sort/re-section the displayed list (settings are already
  saved by then; `CardColorChanged` needs no re-ordering), and retires a card when its repo is removed.
- **MainWindow** (code-behind) — subscribes to `RemoveRequested`
  (`AgentControl-RepoCardViewModel-RemoveRequest`), `SelectPackageRequested`, `NewGroupRequested`,
  `ReleaseNotesReady`, `AgentsMdTemplateOfferRequested`, `ContentChangeConfirmationRequested`,
  `CommittedFilesConversionRequested` (answered through a modal `ConfirmationWindow`), and `ErrorOccurred` to drive
  dialogs/windows (detaching these handlers when a card leaves the list), calling
  `AcceptAgentsMdTemplate`/`DeclineAgentsMdTemplate` from the
  `ConfirmationWindow` shown for the AGENTS.md template offer, `MoveToGroup` from the card menu's
  "Move to group" entries and the name prompt, and `SetCardColor` from the color palette pop-up.
