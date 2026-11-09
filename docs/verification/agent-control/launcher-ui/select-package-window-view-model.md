### SelectPackageWindowViewModel

#### Verification Approach

`SelectPackageWindowViewModel` is verified with unit tests defined in
`SelectPackageWindowViewModelTests.cs` (which also holds the tests of the preferred package and version) and
`SelectPackageRetryTests.cs`. No Avalonia `Window` is constructed in these tests,
matching `SettingsWindowViewModelTests`'s pattern; they use real temporary directories and
package-zip fixtures behind a real `PackageSourceRegistry` and `PackageVersionCache`, plus
deliberately slow and failing source providers to prove the dialog does not block and isolates
a failing source. The window itself is exercised by the headless Avalonia tests in
`DialogWindowTests.cs` and end to end by the FlaUI tests.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Constructing the view model lists every configured source without reading any of them.
- Loading reads every source without blocking, shows a progress state while it runs, and can be
  cancelled.
- An unreachable source shows its own inline error and never prevents other sources from loading.
- Package and version lists reflect only what is actually present at the selected source.
- A newer package selection supersedes a slower earlier one.
- Confirmation is permitted only when a source, a package name and a version are selected.
- The confirmed package reference matches exactly what the user selected, including the source.
- A failed read of a source or of a package's versions can be retried on its own.
- Given the source, package and version a repo is pinned to, the dialog selects them once the source has been read
  (the newest version stays selected when the pinned one is no longer listed), selects nothing when the source failed,
  does not offer the package or is not configured, and never overrides a choice the user made first; a Git source is
  shown with the Git icon and its type name.

#### Test Scenarios

**SelectPackageWindowViewModel_ListPackages_AllSourcesListedAndLoaded**: Construction lists every
source and selects the preferred one (or the first) without reading any; loading shows the
selected source's packages, and a source with no packages is reported as empty rather than as an
error. This scenario is tested by
`SelectPackageWindowViewModel_Constructor_ListsEverySourceWithoutReadingAny`,
`SelectPackageWindowViewModel_Constructor_PreferredSourceAlias_SelectsThatSourceOrFirst`,
`SelectPackageWindowViewModel_LoadAsync_ReadsEverySourceAndShowsSelectedSourcesPackages`, and
`SelectPackageWindowViewModel_LoadAsync_SourceWithNoPackages_ReportsEmptyNotError`, covering
`AgentControl-SelectPackageWindowViewModel-ListPackages`.

**SelectPackageWindowViewModel_AsyncLoading_ShowsProgressAndSupportsCancellation**: A cancelled
load surfaces as an `OperationCanceledException`; a slow version read leaves the caller free and
shows a progress state; and a later package selection supersedes a slower earlier one. This
scenario is tested by
`SelectPackageWindowViewModel_LoadAsync_Cancelled_ThrowsOperationCanceledException`,
`SelectPackageWindowViewModel_SelectPackageName_VersionsSlow_ShowsProgressWithoutBlocking`, and
`SelectPackageWindowViewModel_ChangeSelectedPackageName_SupersedesSlowerEarlierLoad`, covering
`AgentControl-SelectPackageWindowViewModel-AsyncLoading`.

**SelectPackageWindowViewModel_SourceFailureIsolation_OneBrokenSourceDoesNotBlockOthers**: With
one source that throws, or one whose hand-edited location is blank (its provider refuses to be built),
the other sources still load and the failed source carries an inline
message; a failing version read shows an inline error on the version list. This scenario is
tested by
`SelectPackageWindowViewModel_LoadAsync_OneSourceUnreachable_OthersStillLoadAndErrorIsInline`,
`SelectPackageWindowViewModel_LoadAsync_SourceWithBlankLocation_FailsOnlyThatRow` and
`SelectPackageWindowViewModel_SelectPackageName_VersionReadFails_ShowsInlineError`, covering
`AgentControl-SelectPackageWindowViewModel-SourceFailureIsolation`.

**SelectPackageWindowViewModel_VersionSelection_DescendingWithLatestDefaultAndClearedOnSourceChange**:
Selecting a package name populates its versions in descending order with the newest
pre-selected, and changing the selected source clears the package and version selection. This
scenario is tested by
`SelectPackageWindowViewModel_SelectPackageName_PopulatesVersionsDescendingAndDefaultsToLatest`
and `SelectPackageWindowViewModel_ChangeSelectedSource_ClearsPackageAndVersionSelection`,
covering `AgentControl-SelectPackageWindowViewModel-VersionSelection`.

**SelectPackageWindowViewModel_ConfirmGating_RequiresSourcePackageAndVersion**: With nothing
selected, `CanConfirm` is false. This scenario is tested by
`SelectPackageWindowViewModel_NoSelection_CanConfirmIsFalse`, covering
`AgentControl-SelectPackageWindowViewModel-ConfirmGating`.

**SelectPackageWindowViewModel_Confirm_RaisesConfirmedWithFullPackageReference**:
`ConfirmCommand` raises `Confirmed` carrying the exact selected source alias, package name and
version. This scenario is tested by
`SelectPackageWindowViewModel_ConfirmCommand_RaisesConfirmedWithFullPackageReference`, covering
`AgentControl-SelectPackageWindowViewModel-Confirm`.

**SelectPackageWindowViewModel_SourceScoped_PackageOfOtherSourceIsNotOffered**: With the same
package name in two sources with different versions, selecting one source offers only that
source's versions. This scenario is tested by
`SelectPackageWindowViewModel_SamePackageInTwoSources_UsesOnlySelectedSource`, covering
`AgentControl-SelectPackageWindowViewModel-SourceScoped`.

Argument validation is covered by
`SelectPackageWindowViewModel_Constructor_NullArguments_ThrowsArgumentNullException`.

**SelectPackageWindowViewModel_Retry_ReadsOnlyWhatFailed**: When a package's versions could not be read,
the error is shown with Retry offered, and retrying reads them again and shows the versions; a retry that
fails again keeps the error and Retry available; a source that could not be read is read again by Retry
without touching the other source; and with nothing failed Retry does nothing. The failures are scripted with
`FakePackageSourceProvider`, whose version listing can be told to throw and whose gate lets a test hold a read
in progress. This scenario is tested by
`SelectPackageWindowViewModel_Retry_VersionsFailedThenServerAnswers_ShowsVersions`,
`SelectPackageWindowViewModel_Retry_StillFailing_KeepsErrorAndRetryAvailable`,
`SelectPackageWindowViewModel_Retry_SourceFailedThenAnswers_LoadsOnlyThatSource` and
`SelectPackageWindowViewModel_Retry_NothingFailed_DoesNothing`, covering
`AgentControl-SelectPackageWindowViewModel-Retry`.

**SelectPackageWindowViewModel_PreselectCurrentPackage_OpensOnThePinnedChoice**: Given the source, package and version of
a pin, loading selects that package and version in the preferred source; when the pinned version is no longer listed the
newest stays selected; when the source's package list does not offer the package, when the source could not be read and
when the alias names no configured source nothing is selected; and a package chosen by the user before the load ends is
not replaced by the preference, nor is the preference applied when the user switched to another source and back to the
preferred one while the sources were loading; cancelling the load while the preferred package's versions are read
cancels that read too (its provider never answers, so the load would not end otherwise) and the cancellation surfaces
to the caller. This scenario is tested by
`SelectPackageWindowViewModel_Constructor_PreferredSourceAlias_SelectsThatSourceOrFirst`,
`SelectPackageWindowViewModel_LoadAsync_PreferredPackageAndVersion_AreSelected`,
`SelectPackageWindowViewModel_LoadAsync_PreferredVersionNotListed_KeepsNewestVersion`,
`SelectPackageWindowViewModel_LoadAsync_PreferredPackageNotOffered_SelectsNothing`,
`SelectPackageWindowViewModel_LoadAsync_PreferredSourceFailed_SelectsNothing`,
`SelectPackageWindowViewModel_LoadAsync_PreferredAliasUnknown_SelectsNothing`,
`SelectPackageWindowViewModel_LoadAsync_UserChoiceBeforeLoadEnds_IsNotOverridden`,
`SelectPackageWindowViewModel_LoadAsync_UserSwitchedSourceAndBackBeforeLoadEnds_PreferredNotApplied` and
`SelectPackageWindowViewModel_LoadAsync_CancelledWhilePreferredVersionsLoad_CancelsThatReadToo`, covering
`AgentControl-SelectPackageWindowViewModel-PreselectCurrentPackage`, `-PreselectNewestWhenVersionGone`,
`-PreselectNothingWhenSourceFailed`, `-PreselectNothingWhenNotOffered`, `-PreselectNothingWhenSourceUnknown`,
`-PreselectKeepsUserChoice` and `-PreselectCancelled`. The row of a Git source showing the Git icon and
the type name "Git repository" is checked by `SelectPackageWindowViewModel_Constructor_GitSource_ShowsGitIconAndTypeName`
and is cited by the `LauncherUI` subsystem requirement `AgentControl-LauncherUI-GitSourcePresentation`; the dialog
opening on the pinned choice from the card menu is verified in the `LauncherUI` subsystem tests.
