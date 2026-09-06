## FlaUI Verification

This document provides the verification evidence for the `FlaUI` OTS software item.
Requirements for this OTS item are defined in the FlaUI OTS Software Requirements document.

### Required Functionality

FlaUI.Core and FlaUI.UIA3 back the `DemaConsulting.AgentControl.UiTests` project's end-to-end
tests, which launch the published application and interact with its windows/controls exactly
as a developer would.

### Verification Approach

FlaUI does not provide dotnet self-validation, so it is verified by the passing outcome of its
own driven end-to-end tests. Each scenario below is a FlaUI-driven test that launches the
published AgentControl executable (using `StartupOptions`' config-directory and git/agent-tool
command overrides for isolation), attaches a FlaUI `UIA3Automation` session, and drives the
window exactly as a user would. A passing test demonstrates FlaUI can locate elements by
automation identifier, invoke commands, and read control state from a real running Avalonia
window.

### Test Scenarios

#### windows@MainWindow_Launch_WindowVisibleWithExpectedAutomationId

**Scenario**: FlaUI launches the published application and locates the main window by its
expected automation identifier.

**Expected**: The main window is visible and is found by FlaUI via its expected
`AutomationId`.

**Requirement coverage**: `AgentControl-OTS-FlaUI-DriveUiTests`.

#### windows@RemoveMenuItem_ClickThenConfirm_RemovesCard_DecliningLeavesItInPlace

**Scenario**: FlaUI clicks a repo card's remove menu item, confirms the removal, and
separately declines a second removal attempt.

**Expected**: Confirming removes the card; declining leaves it in place.

**Requirement coverage**: `AgentControl-OTS-FlaUI-DriveUiTests`.

#### windows@FilterTextBox_TypingNonMatchingText_HidesRepoCard_ClearingRestoresIt

**Scenario**: FlaUI types non-matching text into the filter text box, then clears it.

**Expected**: The repo card is hidden while the filter does not match, and reappears once the
filter is cleared.

**Requirement coverage**: `AgentControl-OTS-FlaUI-DriveUiTests`.

#### windows@LaunchButton_Click_InvokesConfiguredAgentToolInRepoWorkingDirectory

**Scenario**: FlaUI clicks a repo card's launch button.

**Expected**: The configured agent tool command is invoked in the repo's working directory.

**Requirement coverage**: `AgentControl-OTS-FlaUI-DriveUiTests`.

#### windows@PullButton_Click_InvokesConfiguredGitExecutableWithPullArgument

**Scenario**: FlaUI clicks a repo card's pull button.

**Expected**: The configured git executable is invoked with the pull argument.

**Requirement coverage**: `AgentControl-OTS-FlaUI-DriveUiTests`.

#### windows@SelectPackageMenuItem_Click_AppliesChosenPackageAndSyncsManagedFolders

**Scenario**: FlaUI clicks a repo card's select-package menu item, chooses a package/version
in the resulting dialog, and confirms.

**Expected**: The chosen package is applied and the repo's managed agent-file folders are
synced.

**Requirement coverage**: `AgentControl-OTS-FlaUI-DriveUiTests`.

#### windows@UpgradeMenuItem_Click_SyncsManagedFoldersAndShowsReleaseNotes

**Scenario**: FlaUI clicks a repo card's upgrade menu item.

**Expected**: The repo's managed agent-file folders are synced to the newer version, and the
release-notes dialog is shown.

**Requirement coverage**: `AgentControl-OTS-FlaUI-DriveUiTests`.

#### windows@AboutButton_Click_OpensAboutWindowShowingVersionAndCopyright

**Scenario**: FlaUI clicks the about button.

**Expected**: The about window opens, showing the application's version and copyright
information.

**Requirement coverage**: `AgentControl-OTS-FlaUI-DriveUiTests`.
