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
This scenario is tested by
`MainWindowViewModel_ApplySettings_PreservesRecentReposAndPersistsUpdatedFields` and
`MainWindowViewModel_ApplySettings_InvalidatesSharedPackageVersionCache`, covering
`AgentControl-MainWindowViewModel-ApplySettings`.
