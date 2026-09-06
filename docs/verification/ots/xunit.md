## xUnit Verification

This document provides the verification evidence for the xUnit OTS software item. Requirements
for this OTS item are defined in the xUnit OTS Software Requirements document.

### Required Functionality

xUnit v3 (xunit.v3 and xunit.runner.visualstudio) is the unit-testing framework used by the
project. It discovers and runs all test methods annotated with `[Fact]` or `[Theory]` across
`DemaConsulting.AgentControl.Tests`, and writes TRX result files that feed into coverage
reporting and requirements traceability. Passing tests confirm the framework is functioning
correctly.

### Verification Approach

xUnit is verified by self-validation evidence from the CI pipeline. Each scenario names a
specific test method, drawn from across every subsystem's unit test project, that xUnit must
discover, execute, and record in a TRX result file. A passing pipeline run for all scenarios
constitutes evidence that both requirements are satisfied.

### Test Scenarios

#### Program_Version_ReturnsNonEmptyString

**Scenario**: xUnit discovers and runs this test; the test verifies the `Program` unit's
version string is non-empty.

**Expected**: xUnit executes the test, the test passes, and the result appears in the TRX
output.

**Requirement coverage**: `AgentControl-OTS-xUnit-Execute`, `AgentControl-OTS-xUnit-Report`.

#### PathHelpers_SafePathCombine_ValidPaths_CombinesCorrectly

**Scenario**: xUnit discovers and runs this test; the test verifies that `PathHelpers`'
`SafePathCombine` correctly joins valid path segments.

**Expected**: xUnit executes the test, the test passes, and the result appears in the TRX
output.

**Requirement coverage**: `AgentControl-OTS-xUnit-Execute`, `AgentControl-OTS-xUnit-Report`.

#### PackageVersion_TryParse_ReleaseVersion_ParsesComponents

**Scenario**: xUnit discovers and runs this test; the test verifies that `PackageVersion`
correctly parses a release version string into its components.

**Expected**: xUnit executes the test, the test passes, and the result appears in the TRX
output.

**Requirement coverage**: `AgentControl-OTS-xUnit-Execute`, `AgentControl-OTS-xUnit-Report`.

#### GitClient_IsWorkingTreeClean_CleanRepo_ReturnsTrue

**Scenario**: xUnit discovers and runs this test; the test verifies that `GitClient` reports a
clean working tree as clean.

**Expected**: xUnit executes the test, the test passes, and the result appears in the TRX
output.

**Requirement coverage**: `AgentControl-OTS-xUnit-Execute`, `AgentControl-OTS-xUnit-Report`.

#### SettingsStore_SaveThenLoad_RoundTripsAllFields

**Scenario**: xUnit discovers and runs this test; the test verifies that `SettingsStore`
round-trips all settings fields through a save followed by a load.

**Expected**: xUnit executes the test, the test passes, and the result appears in the TRX
output.

**Requirement coverage**: `AgentControl-OTS-xUnit-Execute`, `AgentControl-OTS-xUnit-Report`.

#### StartupOptions_Parse_NoArguments_AllOverridesNull

**Scenario**: xUnit discovers and runs this test; the test verifies that parsing an empty
argument list leaves every `StartupOptions` override null.

**Expected**: xUnit executes the test, the test passes, and the result appears in the TRX
output.

**Requirement coverage**: `AgentControl-OTS-xUnit-Execute`, `AgentControl-OTS-xUnit-Report`.

#### MainWindowViewModel_Constructor_SettingsHaveRecentRepos_PopulatesRepoCards

**Scenario**: xUnit discovers and runs this test; the test verifies that constructing
`MainWindowViewModel` with settings containing recent repos populates a repo card for each.

**Expected**: xUnit executes the test, the test passes, and the result appears in the TRX
output.

**Requirement coverage**: `AgentControl-OTS-xUnit-Execute`, `AgentControl-OTS-xUnit-Report`.

### Acceptance Criteria

N/A - Acceptance criteria are managed at the system integration level. This OTS item is
considered verified when the integration test scenarios that exercise its functionality pass
in the CI pipeline.
