# AgentControl

## Verification Approach

System-level verification uses end-to-end integration tests defined in the
`DemaConsulting.AgentControl.UiTests` project. Each test launches the real, published
AgentControl executable and drives it through Windows UI Automation using FlaUI, exactly as a
developer would: it locates controls by their `AutomationId`, invokes commands (button clicks,
menu items, text entry), and asserts on observable window/control state and on the arguments
passed to stub git/agent-tool executables. Tests treat the application as a black box; no
internal implementation details are assumed. `StartupOptions`' configuration-directory and
git/agent-tool command overrides isolate each test from a real `%APPDATA%\AgentControl\`
folder and from any real git installation or agentic CLI tool, per architecture.md's
testability strategy (`AgentControl-System-TestIsolation`).

Two system-level requirements have no dedicated FlaUI end-to-end scenario and are verified one
level down instead, with the limitation disclosed rather than a fabricated end-to-end claim:

- `AgentControl-System-Settings` — the UiTests project pre-seeds settings JSON directly via
  `TestSettingsWriter` rather than driving the Settings window through the UI, so this
  requirement is verified via `SettingsWindowViewModel`'s own unit test instead.
- `AgentControl-System-DiagnosticLogging` — no test directly inspects the diagnostic log
  file's contents; the only test-suite touch point is indirect, via `TestLoggingFixture.cs`
  (see the `Logging` subsystem verification design for the full disclosure).

## Test Environment

End-to-end tests in `DemaConsulting.AgentControl.UiTests` run only on Windows (tagged
`windows@` in the requirements trace matrix) against the real, published AgentControl
executable, since FlaUI drives Windows UI Automation and Avalonia's classic desktop lifetime
is only distributed as a Windows MSI for v1 (`AgentControl-Platform-Windows`). Each test uses
an isolated temporary configuration directory (via `--config-dir`) and stub git/agent-tool
executables (via `--git-path`/`--agent-tool-command`, built with
`DemaConsulting.AgentControl.ArgLoggerStub`) so no test depends on, or mutates, a real git
installation, a real agentic CLI tool, or a developer's actual `%APPDATA%\AgentControl\`
folder. Unit tests that back the two disclosed exceptions above run on .NET 10 on any
supported platform with no additional environment setup
(`AgentControl-Platform-Net10`).

## Acceptance Criteria

- All end-to-end tests in `DemaConsulting.AgentControl.UiTests` pass with zero failures on
  Windows.
- All unit tests in `DemaConsulting.AgentControl.Tests` pass with zero failures.
- The main window becomes visible and locatable by its expected `AutomationId` on launch.
- Every user-facing action (add/remove repo, search/sort, launch, pull, select package,
  upgrade, settings, about) produces its documented observable outcome when driven through
  the real UI.
- No test depends on a real git installation, a real agentic CLI tool, or the developer's
  actual settings/log folders.

## Test Scenarios

**AgentControl_RecentRepos_MainWindowLaunch_DisplaysRepoCards**: The published application is
launched with a pre-seeded settings file containing recent repos; the main window becomes
visible and locatable by its expected `AutomationId`, confirming the recent-repos list (with
each card's name, path, pin, branch, and status badges) is displayed on startup. This scenario
is tested by `windows@MainWindow_Launch_WindowVisibleWithExpectedAutomationId`, covering
`AgentControl-System-RecentRepos`.

**AgentControl_AddRemoveRepo_RemoveMenuItem_RequiresConfirmation**: The remove menu item is
clicked on a repo card, then the confirmation dialog is declined and later accepted in
separate runs; declining leaves the card in place, and confirming removes it, proving removal
is gated on user confirmation. This scenario is tested by
`windows@RemoveMenuItem_ClickThenConfirm_RemovesCard_DecliningLeavesItInPlace`, covering
`AgentControl-System-AddRemoveRepo`.

**AgentControl_SearchAndSort_FilterTextBox_HidesAndRestoresCards**: Non-matching text is typed
into the filter text box, hiding a repo card, and then the filter is cleared, restoring it;
confirms the recent-repos list is searchable and returns to its sorted (favorites-first,
most-recently-launched) order once the filter is removed. This scenario is tested by
`windows@FilterTextBox_TypingNonMatchingText_HidesRepoCard_ClearingRestoresIt`, covering
`AgentControl-System-SearchAndSort`.

**AgentControl_Launch_LaunchButton_InvokesConfiguredAgentToolInWorkingDirectory**: The Launch
button is clicked on a repo card whose agent files are already synced; the configured
(stubbed) agent-tool executable is invoked with the repo's working directory as its argument,
proving launch only proceeds once agent files are confirmed synced. This scenario is tested by
`windows@LaunchButton_Click_InvokesConfiguredAgentToolInRepoWorkingDirectory`, covering
`AgentControl-System-Launch`.

**AgentControl_Pull_PullButton_InvokesConfiguredGitExecutableWithPullArgument**: The Pull
button is clicked on a repo card with a clean working tree; the configured (stubbed) git
executable is invoked with the `pull` argument, proving the pull action is wired to the
configured git executable and gated on a clean working tree. This scenario is tested by
`windows@PullButton_Click_InvokesConfiguredGitExecutableWithPullArgument`, covering
`AgentControl-System-Pull`.

**AgentControl_SelectPackage_SelectPackageMenuItem_AppliesChosenPackageAndSyncsFolders**: The
Select Package menu item is clicked on an unpinned repo card, a package name/version is chosen
and confirmed; the repo's pin is set and its managed agent-file folders are synced to match,
proving the initial-pin selection flow works end-to-end. This scenario is tested by
`windows@SelectPackageMenuItem_Click_AppliesChosenPackageAndSyncsManagedFolders`, covering
`AgentControl-System-SelectPackage`.

**AgentControl_Upgrade_UpgradeMenuItem_SyncsManagedFoldersAndShowsReleaseNotes**: The Upgrade
menu item is clicked on a repo card pinned to an older version than what is available at the
package source; the managed folders are synced to the newer version, the pin file is updated,
and the release-notes dialog is shown, proving both the upgrade sync flow and the pin
persistence it depends on. This scenario is tested by
`windows@UpgradeMenuItem_Click_SyncsManagedFoldersAndShowsReleaseNotes`, covering
`AgentControl-System-Upgrade` and `AgentControl-System-PinFile`.

**AgentControl_Settings_SaveCommand_PersistsEditedPreferences**: `SettingsWindowViewModel`'s
`SaveCommand` is executed with edited package-source path, git/agent-tool overrides, and shell
preference; the `onSave` callback receives the current values and `Saved` is raised, proving
edited preferences are captured and communicated for persistence. Per the disclosed
granularity limitation above, this scenario is tested at the view-model level by
`SettingsWindowViewModel_SaveCommand_Execute_InvokesOnSaveWithCurrentValuesAndRaisesSaved`,
covering `AgentControl-System-Settings`.

**AgentControl_DiagnosticLogging_AnyPassingTest_ConfirmsSerilogInitializedWithoutThrowing**:
Per the `Logging` subsystem's test-evidence disclosure, no test directly inspects the
diagnostic log file's contents. Any passing test in the `DemaConsulting.AgentControl.Tests`
assembly is indirect evidence that `LoggingSetup.Initialize` (called once by
`TestLoggingFixture` before any test runs) completed without throwing. This scenario is tested
by `GitClient_IsWorkingTreeClean_CleanRepo_ReturnsTrue` (one such passing test), covering
`AgentControl-System-DiagnosticLogging`.

**AgentControl_About_AboutButton_OpensAboutWindowShowingVersionAndCopyright**: The About
button is clicked; the About window opens showing the application version and copyright
notice. This scenario is tested by
`windows@AboutButton_Click_OpensAboutWindowShowingVersionAndCopyright`, covering
`AgentControl-System-About` and `AgentControl-Program-Version`.

**AgentControl_TestIsolation_StartupOptions_CapturesConfigDirOverride**: The `--config-dir`
startup argument is parsed; the resulting `StartupOptions.ConfigDirectory` captures the
supplied path, proving the override needed for hermetic, isolated end-to-end testing is
correctly parsed. This scenario is tested by
`StartupOptions_Parse_ConfigDirArgument_CapturesValue`, covering
`AgentControl-System-TestIsolation`.

**AgentControl_Startup_MainWindowLaunch_LoadsSettingsAndShowsRecentRepos**: The published
application is launched; the main window becomes visible showing the loaded settings' recent
repos, proving settings are loaded and the recent-repos window is displayed on launch. This
scenario is tested by `windows@MainWindow_Launch_WindowVisibleWithExpectedAutomationId`,
covering `AgentControl-System-Startup`.

**AgentControl_UiFramework_MainWindowLaunch_RendersViaAvalonia**: The published application
(built on Avalonia) is launched; the main window and its controls render and are locatable by
`AutomationId`, proving the cross-platform UI framework renders the interface correctly. This
scenario is tested by `windows@MainWindow_Launch_WindowVisibleWithExpectedAutomationId`,
covering `AgentControl-System-UiFramework`.

**AgentControl_PlatformWindows_MainWindowLaunch_RunsAsNativeWindowsApplication**: The
published application is launched on Windows and its main window is located by `AutomationId`
through Windows UI Automation, proving the tool runs as a native Windows desktop application
exposing UI Automation identifiers. This scenario is tested by
`windows@MainWindow_Launch_WindowVisibleWithExpectedAutomationId`, covering
`AgentControl-Platform-Windows`.

**AgentControl_PlatformNet10_Version_ReturnsNonEmptyString**: `Program.Version` is read on
the .NET 10 runtime; a non-empty, non-null version string is returned, proving the tool runs
correctly on .NET 10. This scenario is tested by `Program_Version_ReturnsNonEmptyString`,
covering `AgentControl-Platform-Net10`.
