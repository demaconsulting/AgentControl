### SelectPackageWindowViewModel

#### Verification Approach

`SelectPackageWindowViewModel` is verified with unit tests defined in
`SelectPackageWindowViewModelTests.cs`. No Avalonia `Window` is ever constructed, matching
`SettingsWindowViewModelTests`'s headless pattern; only real temporary directories and
package-zip fixtures are used for the underlying `PackageVersionCache`, so no mocking of the
package-discovery layer is required.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Package name and version lists reflect only what is actually present at the source.
- Confirmation is permitted only when both a name and version are selected.
- The confirmed pair matches exactly what the user selected.

#### Test Scenarios

**SelectPackageWindowViewModel_ListPackages_EmptySourceProducesEmptyList**: An empty package
source directory produces an empty package-names list and `CanConfirm` is false. This scenario
is tested by
`SelectPackageWindowViewModel_EmptySource_EmptyPackageNamesAndCanConfirmFalse`, covering
`AgentControl-SelectPackageWindowViewModel-ListPackages`.

**SelectPackageWindowViewModel_VersionSelection_DescendingWithLatestDefaultAndRecompute**:
Selecting a package name populates its versions in descending order with the newest
pre-selected, and changing the selected name recomputes the version list and resets the
selection. This scenario is tested by
`SelectPackageWindowViewModel_SelectPackageName_PopulatesVersionsDescendingAndDefaultsToLatest`
and
`SelectPackageWindowViewModel_ChangeSelectedPackageName_RecomputesVersionsAndResetsSelection`,
covering `AgentControl-SelectPackageWindowViewModel-VersionSelection`.

**SelectPackageWindowViewModel_ConfirmGating_RequiresBothNameAndVersion**: With no package
name or version selected, `CanConfirm` is false. This scenario is tested by
`SelectPackageWindowViewModel_NoSelection_CanConfirmIsFalse`, covering
`AgentControl-SelectPackageWindowViewModel-ConfirmGating`.

**SelectPackageWindowViewModel_Confirm_RaisesConfirmedWithSelectedPair**: `ConfirmCommand`
raises `Confirmed` carrying the exact selected name/version pair. This scenario is tested by
`SelectPackageWindowViewModel_ConfirmCommand_RaisesConfirmedWithSelectedPair`, covering
`AgentControl-SelectPackageWindowViewModel-Confirm`.
