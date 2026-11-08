### MainWindowViewModel

#### Verification Approach

`MainWindowViewModel` is verified with unit tests defined in `MainWindowViewModelTests.cs`.
Each test constructs a real `MainWindowViewModel` against a unique temporary configuration
directory (via `SettingsStore`) and unique temporary repo directories, so tests are hermetic
and can run in parallel with each other, subject to the `RealProcess` xUnit collection (which
disables parallelization) since constructing a view model with recent repos spawns real git
subprocesses via `GitStub` or the system git executable as part of each card's eager
`RefreshCheap()`. No Avalonia `Window` is constructed; only the view-model class itself is
under test.

#### Test Environment

N/A - standard test environment; `RealProcess` collection applies (see Verification Approach).

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Repo cards are created for every recent repo recorded in settings on construction.
- Add/remove operations correctly mutate both in-memory collections and persisted settings.
- Search filtering and sort ordering produce the documented result for every tested input.
- Applying updated settings preserves the recent-repos list and invalidates the shared
  package-version cache.
- Applying settings drops cached package results only for sources that were edited or removed.
- Constructing the view model reads no package source; upgrade availability is checked in the
  background, and an unreachable source affects only the cards pinned to it.
- The connection-test provider shares the application's session credentials with the cards.

#### Test Scenarios

**MainWindowViewModel_PopulateRecentRepos_ConstructorCreatesCardsAndSkipsEagerGitCheck**: The
constructor is called with settings containing recent repos; a card is created per repo, and
a git stub's invocation log confirms the working-tree dirty-check subprocess (`status
--porcelain`) is never invoked eagerly during construction (only the cheap checks run at
launch). This scenario is tested by
`MainWindowViewModel_Constructor_SettingsHaveRecentRepos_PopulatesRepoCards` and
`MainWindowViewModel_Constructor_DoesNotInvokeWorkingTreeDirtyCheckSubprocess`, covering
`AgentControl-MainWindowViewModel-PopulateRecentRepos`.

**MainWindowViewModel_AddRepo_ValidatesExistenceAndDuplication**: `AddRepo` is called with a
valid new existing path (inserted and persisted), a nonexistent path (rejected, returns
false), and an already-tracked path (rejected, returns false). This scenario is tested by
`MainWindowViewModel_AddRepo_ValidNewPath_InsertsCardAndPersistsSettings`,
`MainWindowViewModel_AddRepo_PathDoesNotExist_ReturnsFalse`, and
`MainWindowViewModel_AddRepo_DuplicatePath_ReturnsFalse`, covering
`AgentControl-MainWindowViewModel-AddRepo`.

**MainWindowViewModel_RemoveRepo_RemovesAndPersistsOnlyOnExplicitCall**: `RemoveRepo` is
called directly, removing the card from both collections and persisting the update; a card's
own `RemoveCommand` (raising `RemoveRequested`) is confirmed to not remove the card by itself.
This scenario is tested by
`MainWindowViewModel_RemoveRepo_RemovesFromCollectionsAndPersistsSettings` and
`MainWindowViewModel_CardRaisesRemoveRequested_DoesNotRemoveCardWithoutConfirmation`, covering
`AgentControl-MainWindowViewModel-RemoveRepo`.

**MainWindowViewModel_SearchFilter_MatchesNameOrPathCaseInsensitively**: `FilterText` is set
to a mixed-case substring of a repo's display name, and separately to a substring of a repo's
full path; in each case only the matching card remains in `DisplayedRepoCards`. This scenario
is tested by
`MainWindowViewModel_FilterText_MatchesRepoNameCaseInsensitive_FiltersDisplayedRepoCards` and
`MainWindowViewModel_FilterText_MatchesRepoPath_FiltersDisplayedRepoCards`, covering
`AgentControl-MainWindowViewModel-SearchFilter`.

**MainWindowViewModel_EmptyState_DistinguishesNoReposFromNoFilterMatches**:
`HasNoDisplayedRepos`/`EmptyStateMessage` are checked with no recent repos configured at all
(message prompts adding a repo), with a tracked repo excluded by a non-matching filter
(message references the filter text), and with at least one displayed card
(`HasNoDisplayedRepos` is false). This scenario is tested by
`MainWindowViewModel_NoRepoCardsAtAll_HasNoDisplayedReposTrueWithAddRepoMessage`,
`MainWindowViewModel_FilterMatchesNoRepos_HasNoDisplayedReposTrueWithFilterMessage`, and
`MainWindowViewModel_HasDisplayedRepos_HasNoDisplayedReposFalse`, covering
`AgentControl-MainWindowViewModel-EmptyState`.

**MainWindowViewModel_SortOrder_FavoritesFirstThenRecencyWithNullsLast**: A favorite repo
launched longer ago sorts above a more-recently-launched non-favorite; among non-favorites, a
recently-launched repo sorts above an earlier-launched one, which sorts above a never-launched
repo. This scenario is tested by
`MainWindowViewModel_DisplayedRepoCards_SortsFavoritesAboveNonFavorites` and
`MainWindowViewModel_DisplayedRepoCards_SortsByLastLaunchedUtcDescendingWithNullsLast`,
covering `AgentControl-MainWindowViewModel-SortOrder`.

**MainWindowViewModel_ApplySettings_PreservesReposAndInvalidatesPackageCache**: `ApplySettings`
is called with an updated `AppSettings`; the existing recent-repos list survives, the new
field values are adopted and persisted, and the shared `PackageVersionCache` is invalidated so
a changed package-source path is reflected immediately rather than served from a stale cache.
A repo that reappeared since its last check gets an immediate fresh Git status (`RefreshCheap`
followed by `RefreshDirtyStatus`), rather than being left showing a stale or indefinitely
"not yet checked" Pull-disabled state. A present repo also gets an immediate fresh Git status
when `GitExecutablePath` itself changed, so a clean/dirty result cached against the previously
configured executable is not kept showing once a different executable is in effect. A repo
that was already present, already checked, and whose executable path did not change only gets
the cheap refresh, so re-saving settings does not re-run a synchronous `git status` for every
tracked repo. This scenario is tested by
`MainWindowViewModel_ApplySettings_PreservesRecentReposAndPersistsUpdatedFields`,
`MainWindowViewModel_ApplySettings_InvalidatesSharedPackageVersionCache`,
`MainWindowViewModel_ApplySettings_RepoReappeared_RefreshesGitStatusImmediately`,
`MainWindowViewModel_ApplySettings_RepoAlreadyPresentAndChecked_DoesNotRecheckGitStatus`, and
`MainWindowViewModel_ApplySettings_GitExecutablePathChanged_RefreshesGitStatus`,
covering `AgentControl-MainWindowViewModel-ApplySettings`.

**MainWindowViewModel_InvalidateChangedSources_OnlyEditedOrRemovedSourcesAreInvalidated**:
`ApplySettings` replaces one package source's location; a card pinned to the unchanged source
keeps its cached upgrade answer even after that source's folder has disappeared (an invalidated
cache would fail to read it and report no upgrade), while a changed source is observed afresh. This
scenario is tested by `MainWindowViewModel_ApplySettings_OnlyEditedSourceIsInvalidated` and
`MainWindowViewModel_ApplySettings_InvalidatesSharedPackageVersionCache`, covering
`AgentControl-MainWindowViewModel-InvalidateChangedSources`.

**MainWindowViewModel_ApplySettings_CredentialBearingSource_IsRefused**: `ApplySettings` with a
package source location that embeds a sentinel password throws `InvalidOperationException` whose
message does not contain it, the live settings instance is unchanged, and no settings file is
written. This scenario is tested by
`MainWindowViewModel_ApplySettings_SourceLocationWithEmbeddedCredential_ThrowsAndKeepsCurrentSettings`,
covering `AgentControl-MainWindowViewModel-ApplySettings`.

**MainWindowViewModel_ApplySettings_SaveFails_KeepsPreviousSettingsLive**: `ApplySettings` against
a configuration location that cannot be written throws `InvalidOperationException`, and the live
settings instance is still the previous one with its previous values and recent repos. This
scenario is tested by `MainWindowViewModel_ApplySettings_SaveFails_KeepsPreviousSettingsLive`,
covering `AgentControl-MainWindowViewModel-ApplySettings`.

**MainWindowViewModel_BackgroundUpgradeCheck_ConstructionReadsNoSourceAndFailuresAreIsolated**:
Constructing the view model with a pinned repo reads no package source until the upgrade refresh
begins; when the refresh runs with one source unreachable, the cards pinned to the other source
are still checked and show their upgrade; and adding a pinned repo checks its upgrade status in
the background. This scenario is tested by
`MainWindowViewModel_Constructor_DoesNotReadSourcesUntilUpgradeRefreshBegins`,
`MainWindowViewModel_BeginUpgradeRefresh_OneSourceUnreachable_OtherCardsStillChecked`, and
`MainWindowViewModel_AddRepo_PinnedRepo_UpgradeCheckRunsInBackground`, covering
`AgentControl-MainWindowViewModel-BackgroundUpgradeCheck`.

**MainWindowViewModel_ConnectionTestProvider_UsesTheSharedCredentialFacilities**: The view model hands out a
real `NuGetSourceProvider` for the settings window's connection test, built with the same credential context
(session store and prompt) as the cards, so a sign-in made during a test is remembered for the session. This
scenario is tested by `MainWindowViewModel_CreateConnectionTestProvider_ReturnsNuGetProviderWithCredentials`
(in `SettingsWindowServerSourceTests.cs`), covering `AgentControl-MainWindowViewModel-ConnectionTestProvider`.

**MainWindowViewModel_ApplyTheme_SavedThemeIsHandedToTheApplierOnlyAfterTheSave**: With a recording theme
applier, `ApplySettings` with the dark theme persists it and calls the applier once with it; when the write
fails (the configuration "directory" is a file) the exception reaches the caller, the applier is not called and
the live settings keep the system default. This scenario is tested by
`MainWindowViewModel_ApplySettings_ThemeChanged_PersistsAndAppliesTheme` and
`MainWindowViewModel_ApplySettings_SaveFails_DoesNotApplyTheme`, covering
`AgentControl-MainWindowViewModel-ApplyTheme`.
