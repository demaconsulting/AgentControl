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

One system-level requirement has no dedicated FlaUI end-to-end scenario and is verified one
level down instead, with the limitation disclosed rather than a fabricated end-to-end claim:

- `AgentControl-System-DiagnosticLogging` — no test directly inspects the diagnostic log
  file's contents; the only test-suite touch point is indirect, via `TestLoggingFixture.cs`
  (see the `Logging` subsystem verification design for the full disclosure).

`AgentControl-System-ResponsiveOperations` states only what UI automation can observe: a background upgrade
check never opens a sign-in dialog. That the window stays responsive, shows progress and can cancel is verified one
level down, with deliberately slow and failing package sources, because a frozen UI thread is not observable through
UI automation without an artificially slow share; those behaviors are specified by the `LauncherUI` requirements and
are not claimed as system-level behavior.

Four groups of system requirements state only what the end-to-end tests can observe; the rest of what the
corresponding `LauncherUI` and `Settings` requirements say is verified one level down, and is not claimed here:

- Modal dialogs and keyboard (`AgentControl-System-DialogModal`, `AgentControl-System-DialogKeyboard`): the real
  mouse and keyboard are used (`ModalDialogTests.cs`), because a UI Automation call can act on a window that a real
  user could not. The requirements name the dialogs that are exercised - Settings and About for modality, and the
  Select Package dialog besides them for the keyboard - and the other dialogs are verified in the headless window
  tests of the `LauncherUI` subsystem. The keyboard focus ring is invisible to UI Automation, so it is not asserted
  end to end at all: it is verified (as the focus-visible state of the focused control) by the headless dialog tests
  and by the `DialogBehavior` unit tests.
- Legibility (`AgentControl-System-WindowsLegible`): every window of the application is opened in both themes
  (`WindowAppearanceTests.cs`) and, from the pixels of a screenshot, the backdrop of the window must have the
  brightness of the theme and one text of the window must contrast with it by at least 4.5:1. The contrast of every
  state of every control is not measured, and the focus ring is not asserted; the contrast of the theme's text and
  graphic colors is verified in the `LauncherUI` subsystem tests.
- Groups, card colors and favorites (`AgentControl-System-RepoGroups` and the requirements next to it,
  `AgentControl-System-CardColor` and its two companions): the scenarios below name what is exercised. Renaming and
  removing a group from its header and the keeping of the keyboard focus on the star are not exercised end to end;
  they are verified in the headless window tests of the `LauncherUI` subsystem.
- A change that cannot be saved (`AgentControl-System-ListChangesKept`): a failing save cannot be provoked through
  the published application, so only the keeping of the changes is shown end to end; the refusal and the message are
  verified in the `LauncherUI` subsystem and unit tests.

`AgentControl-System-AgentsMdOfferBeforeReleaseNotes` is verified end to end by
`MainWindow_UpgradeWithAgentsMdOfferInRealUi_ShowsQuestionFirstThenReleaseNotes` in `AgentsMdTemplateOfferTests.cs`; the
headless window test of the `LauncherUI` subsystem has the same name, so the trace matrix cannot tell the two apart
by name - the `windows@` link of the system requirement is satisfied by either run on Windows, and the end-to-end
test is the one this scenario describes.

The credential system requirements (`AgentControl-System-CredentialSafety`,
`AgentControl-System-CredentialSessionScope`, `AgentControl-System-SavedSignInLifecycle` and
`AgentControl-System-CredentialOriginScope`) are verified end to end in `NuGetServerSourceTests.cs` and
`RememberedSignInTests.cs`. `NuGetServerSourceTests.cs` runs
the published application against an in-process mock NuGet V3 server (the same `MockNuGetFeed` the unit tests
use, linked into the UI test project; plain HTTP, so the source ticks "Allow insecure HTTP", and each launch of the
application gets a private `NUGET_HTTP_CACHE_PATH` so a reused port is never answered from an earlier run's
cache). It drives the
Add NuGet-Package Server dialog, the sign-in dialog and Select Package with the real NuGet client, then
searches every file the application wrote - the settings, the repo's pin and the configuration directory's
log and text files - and every file in the target repo for the password the test typed.

`RememberedSignInTests.cs` covers the opt-in to remember a sign-in, with the arg-logger stub standing in for git and
emulating the user's credential helper:
when the environment variable `ARGLOGGER_CREDENTIAL_STORE` names a file, the stub answers the helper question and the
`git credential fill|approve|reject` invocations from that file, which lives outside every folder the application
writes to, so a test can show that the sign-in is kept only by "the helper" and can never touch the developer's real
credential store. It also starts two mock servers on different origins to show that a credential typed for one is
never sent to the other. The details that are not observable end to end are verified at the subsystem and unit
levels: through the credential logic with a fake credential store (a later session finds the saved sign-in without
asking, a wrong password is never saved), through the real `git credential` protocol
against a throw-away helper in a temporary folder, and through a stub git that records exactly what is sent, in what
environment and within what time limit.

The Git source scenarios (`GitSourceTests.cs`, `GitSourceSafetyTests.cs` and `GitSignInTests.cs`,
with `GitFixtureRepository.cs`
and `GitHttpsServer.cs`) are the exception to the stub git: each builds a real local bare Git repository in a temporary
folder, runs the application with the real `git` (isolated from the developer's global and system configuration and
credential helpers, so no credential store is touched) and is skipped when git is not installed - or failed instead
when the environment variable `CI` is set, so that a pipeline can never pass with this evidence silently missing. No
network is used beyond loopback. The sign-in scenarios use a private Git server of the test on a loopback port (TLS
with a throw-away certificate, 401 without credentials) and a test credential helper that behaves like Git Credential
Manager in an isolated git configuration; they show, with the real application and the real git, that background work
never prompts and that work the user started may. What is not exercised end to end is named in the scenarios: an SSH
address, and Git Credential Manager's own dialog - whether it can be shown from a hidden-console process - which is
left to a manual check on a machine with a private https repository.

`AgentControl-System-PackageAuthenticity` is stated as far as one end-to-end scenario in `MultipleSourcesTests.cs`
can show it: the published application runs against a real NuGet folder feed that holds an unsigned newer package,
with the source configured to require signed packages, and the upgrade is refused with a message that says the
package is not validly signed and names the source, leaving the repo's files and pin unchanged. Producing a validly
signed package needs a real signing certificate, so acceptance of a valid signature, the publisher pin and the
independence from the user's own NuGet trust settings are not system-level claims; they are specified and verified at
the subsystem and unit levels, against a real signed package downloaded
from nuget.org (skipped only where nuget.org cannot be reached or the machine lacks the trust roots it needs), as
disclosed in the `AgentPackageManagement` verification design; the platform that cannot verify signatures is simulated
there.

## Test Environment

End-to-end tests in `DemaConsulting.AgentControl.UiTests` run only on Windows (tagged
`windows@` in the requirements trace matrix) against the real, published AgentControl
executable, since FlaUI drives Windows UI Automation and Avalonia's classic desktop lifetime
is only distributed as a Windows MSI for v1 (`AgentControl-Platform-Windows`). Each test uses
an isolated temporary configuration directory (via `--config-dir`) and stub git/agent-tool
executables (via `--git-path`/`--agent-tool-command`, built with
`DemaConsulting.AgentControl.ArgLoggerStub`) so no test depends on, or mutates, a real git
installation, a real agentic CLI tool, or a developer's actual `%APPDATA%\AgentControl\`
folder (the Git source tests use the real `git` against a local repository, as described above, which needs git on the
machine; the Git source tests are failed instead of skipped when the environment variable `CI` is set and git is
absent). Unit tests that back
the disclosed exceptions above run on .NET 10 on any
supported platform with no additional environment setup
(`AgentControl-Platform-Net10`). The server-source end-to-end test also needs loopback networking: it starts
the in-process mock feed (Kestrel, hosted by the ASP.NET Core shared framework that the test project references)
on a fresh port; the Git sign-in tests likewise start the private Git server (Kestrel with TLS on a fresh loopback
port, and the real `git upload-pack`).

## Acceptance Criteria

- All end-to-end tests in `DemaConsulting.AgentControl.UiTests` pass with zero failures on
  Windows.
- All unit tests in `DemaConsulting.AgentControl.Tests` pass with zero failures.
- The main window becomes visible and locatable by its expected `AutomationId` on launch.
- Every user-facing action (add/remove repo, search/sort, launch, pull, select package,
  upgrade, settings, about) produces its documented observable outcome when driven through
  the real UI.
- No test depends on a real git installation, a real agentic CLI tool, or the developer's
  actual settings/log folders; the Git source end-to-end tests are the one exception for git (a throw-away local
  repository, skipped when git is absent and failed when it is absent in CI) and still never touch the developer's
  settings or credentials; their only network is loopback.
- A package from a Git repository is applied only as a package layout: a hostile user configuration (a hook and a filter
  program that plain git runs) runs nothing and the delivered bytes are those of the commit, a tag whose layout holds a
  link, a submodule or names that collide on a case-insensitive file system is refused with an explanation and writes
  nothing, an address that names a program to run or carries a credential is refused by the editor, and nothing of the
  repository besides its layout reaches the repo.
- A Git repository that needs a sign-in is never prompted for by a background check (the card shows "Sign-in needed",
  the user's helper was asked but could not prompt, the server saw no credential) and may be prompted for by work the
  user starts (Refresh on the card, Test connection in the editor).
- A package's content identity (a SHA-256 over its agent files, the same for every kind of source) is recorded in the
  pin for a zip, a NuGet-package and a Git source; a republished zip or NuGet package of the pinned version is not
  synced at launch.
- Several package sources can be configured and carried forward from the legacy setting, a NuGet
  folder source supplies packages applied like zip packages, a NuGet server source can be added with a
  typed sign-in and supplies packages the same way, and a repo pin names no source: it is resolved against the user's
  configured sources, the first of them (in Settings order) that offers the version and delivers files with the
  identical identity supplying it, which the card then shows without reporting a problem; a repo whose pinned version
  no configured source offers shows the problem.
- No credential typed during an end-to-end run is found in any file the application wrote; a sign-in the user chose
  to remember is found only in the user's own (emulated) credential helper, is used again without a dialog and is
  removed by "Forget saved sign-in".
- A repo can be favorited and unfavorited through the real UI (moving between sections), a missing repo can be
  re-checked from its menu, a pinned package that is up to date shows its check mark, and a click on the card's
  upgrade chip runs the upgrade with all four managed folders synced.
- While the Settings or About dialog is open, the main window behind it is disabled and neither a real mouse click
  nor real key presses reach it, Tab and Shift+Tab stay inside the dialog, and the main window is usable again when
  the dialog closes; the Select Package dialog starts with the keyboard focus inside, keeps Tab inside and is canceled
  by Escape.
- Every window is legible in both themes (the backdrop has the theme's brightness and the text contrasts by at
  least 4.5:1), a theme saved by an earlier session is in effect when the application starts, and choosing a theme
  in Settings re-themes the running application without a restart.
- A repo can be moved into a new group, into an existing group and out of its group through the real UI; the
  groups show as sections - favorites first, then the groups alphabetically, then the repos in no group, and no
  sections while no repo is in a group; a collapsed section is still collapsed after a restart; the filter finds a
  repo inside a collapsed group; groups and card colors are stored only in the user's settings (the repository stays
  untouched), and a chosen card color leaves the card's status unchanged.
- The AGENTS.md question of an upgrade is shown before its release notes.
- A source that requires signed packages is refused an unsigned package end to end and the repo is left unchanged;
  a valid signature, the publisher pin and the platform fail-closed behavior are proven at the subsystem and unit
  levels.

## Test Scenarios

**AgentControl_RecentRepos_MainWindowLaunch_DisplaysRepoCards**: The published application is
launched with a pre-seeded settings file containing recent repos; the main window becomes
visible and locatable by its expected `AutomationId`, confirming the recent-repos list (with
each card's name, path, pin, branch, and status badges) is displayed on startup; a card pinned
to a package with a newer version available shows the package chip (name, version and source)
and the upgrade chip. This scenario
is tested by `windows@MainWindow_Launch_WindowVisibleWithExpectedAutomationId` and
`windows@RepoCard_PinnedRepoWithNewerVersion_ShowsPackageAndUpgradeChips`, covering
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

**AgentControl_Pull_PullMenuItem_InvokesConfiguredGitExecutableWithPullArgument**: Git Pull is
chosen in the "..." menu of a repo card with a clean working tree; the configured (stubbed) git
executable is invoked with the `pull` argument, proving the pull action is wired to the
configured git executable and gated on a clean working tree. This scenario is tested by
`windows@PullMenuItem_Click_InvokesConfiguredGitExecutableWithPullArgument`, covering
`AgentControl-System-Pull`.

**AgentControl_SelectPackage_SelectPackageMenuItem_AppliesChosenPackageAndSyncsFolders**: The
Select Package menu item is clicked on an unpinned repo card, a package name/version is chosen
and confirmed; the repo's pin is set and its managed agent-file folders are synced to match,
proving the initial-pin selection flow works end-to-end. A second scenario chooses the NuGet
folder source in the dialog, selects its package and version, and confirms that the card then
shows the package chip naming that source. These scenarios are
tested by
`windows@SelectPackageMenuItem_Click_AppliesChosenPackageAndSyncsManagedFolders` and
`windows@SelectPackage_FromNuGetFolderSource_PinsThePackageAndCardShowsPackageChip`, covering
`AgentControl-System-SelectPackage`.

**AgentControl_ConvertCommittedAgentFiles_AsksFirstAndStopsGitTrackingOnlyWhenConfirmed**: The user applies a
package to a repo whose agent folders are committed to git. A modal question appears before anything is downloaded or
changed, while the card is busy; answering No leaves git, the files and the pin as they were; applying again and
answering Yes stops git tracking the folders, applies the package, writes the pin, clears the committed-files badge and
tells the user to commit the change. The evidence is the headless test of the real main window and card over a real
git repository, `MainWindow_ApplyingToRepoWithTrackedAgentFolders_AsksFirstAndConvertsOnlyWhenConfirmed`, which is not
a UI-automation test (the other system scenarios above are) and so carries no `windows@` filter; it covers
`AgentControl-System-ConvertCommittedAgentFiles`.

**AgentControl_ServerSignIn_SelectPackageFromServer_AsksForSignInOnce**: A NuGet server source that needs
credentials is added; the user starts an operation on it (testing the connection, then selecting a package) and the
sign-in dialog opens, naming the server's origin and saying the credential is used for this session only. After
signing in, the operation continues, and selecting a package from the server asks for no second sign-in; the
pin then holds no source and the card chip shows the alias of the server source that supplies it. This scenario is
tested by
`windows@SettingsWindow_AddNuGetServerWithSignIn_SelectPackagePinsServerSourceAndStoresNoPassword`, covering
`AgentControl-System-ServerSignIn`.

**AgentControl_Upgrade_UpgradeMenuItem_SyncsManagedFoldersAndShowsReleaseNotes**: The Upgrade
menu item is clicked on a repo card pinned to an older version than what is available at the
package source; the managed folders are synced to the newer version, the pin file is updated,
and the release-notes dialog is shown, proving both the upgrade sync flow and the pin
persistence it depends on. This scenario is tested by
`windows@UpgradeMenuItem_Click_SyncsManagedFoldersAndShowsReleaseNotes`, covering
`AgentControl-System-Upgrade` and `AgentControl-System-PinFile`.

**AgentControl_AgentsMdTemplate_ApplySelectedPackage_OffersWritesAndRemembersDecline**: A
dedicated FlaUI end-to-end scenario drives the AGENTS.md template offer's modal
`ConfirmationWindow` dialog end-to-end (accept writes the template verbatim to the repo root;
decline neither writes the file nor re-prompts on a later sync, even after a fresh app
session), exercising the real `MainWindow` event subscription/dialog-routing wiring. The
`RepoCardViewModel` unit tests verify the same behavior in isolation and carry the unit requirements. This
scenario is tested by `windows@AgentsMdTemplateOffer_Accept_WritesTemplateToRepoRoot` and
`windows@AgentsMdTemplateOffer_Decline_DoesNotWriteFileAndDoesNotReprompt`, covering
`AgentControl-System-AgentsMdTemplate`.

**AgentControl_AgentsMdOfferBeforeReleaseNotes_QuestionComesFirstThenReleaseNotes**: An upgrade of a repo with no
`AGENTS.md`, whose newer package offers a template, is run in the real application: while the question is open no
release-notes window exists, and the release-notes window appears only after the question is answered. This scenario
is tested by `windows@MainWindow_UpgradeWithAgentsMdOfferInRealUi_ShowsQuestionFirstThenReleaseNotes`, covering
`AgentControl-System-AgentsMdOfferBeforeReleaseNotes`.

**AgentControl_Settings_SourceDialogs_AddSourcesAndPersistPreferences**: The Settings window is
opened, a zip-package folder source is added through the add dialog and appears in the sources
list, and a NuGet-package folder source is added through its dialog, which cannot be saved until
a package ID is listed; after saving, the settings file in the isolated configuration directory
holds both sources. `SettingsWindowViewModel`'s `SaveCommand` is also executed with edited
git/agent-tool overrides and shell preference, proving the edited preferences are captured and
communicated for persistence (this part is verified only by the view-model's own unit test, a
disclosed granularity limitation). The Add NuGet-Package Server dialog is also exercised: plain `http` is refused
until "Allow insecure HTTP" is ticked, and after saving the source is listed as an "Online source" and persisted with its
insecure-HTTP choice. The tooltip that shows the whole location of a source in the editor is not exercised end to
end; it is verified in the `LauncherUI` subsystem tests. This scenario is tested by
`windows@SettingsWindow_AddZipPackageFolderViaDialog_ListsSourceAndPersistsIt`,
`windows@SettingsWindow_AddNuGetPackageFolderViaDialog_RequiresPackageIdAndPersistsIt`,
`windows@SettingsWindow_AddNuGetServerWithSignIn_SelectPackagePinsServerSourceAndStoresNoPassword` and
`SettingsWindowViewModel_SaveCommand_Execute_InvokesOnSaveWithCurrentValuesAndRaisesSaved`,
covering `AgentControl-System-Settings`.

**AgentControl_ConnectionTest_ServerSource_ReportsResultInWords**: In the Add NuGet-Package Server dialog,
**Test connection** against the mock server opens the sign-in dialog and, once the user has signed in, reports
"Connection OK"; the same test is run again with the remember option ticked. This scenario is tested by
`windows@SettingsWindow_AddNuGetServerWithSignIn_SelectPackagePinsServerSourceAndStoresNoPassword` and
`windows@SettingsWindow_SignInWithRememberTicked_SavedOnlyByTheUsersCredentialHelper`, covering
`AgentControl-System-ConnectionTest`.

**AgentControl_ResponsiveOperations_BackgroundCheckOfServerNeedingSignIn_NeverPrompts**: A repo pinned to a NuGet
server that needs a sign-in is shown when the application starts; the background upgrade check does not open the
sign-in dialog, and the card shows a red "Sign-in needed" chip instead. Window responsiveness, progress and
cancellation are verified one level down (see the Verification Approach). This scenario is tested by
`windows@RepoCard_PinnedToServerNeedingSignIn_BackgroundCheckNeverOpensSignInDialog`, covering
`AgentControl-System-ResponsiveOperations`.

**AgentControl_DiagnosticLogging_AnyPassingTest_ConfirmsSerilogInitializedWithoutThrowing**:
Per the `Logging` subsystem's test-evidence disclosure, no test directly inspects the
diagnostic log file's contents. Any passing test in the `DemaConsulting.AgentControl.Tests`
assembly is indirect evidence that `LoggingSetup.Initialize` (called once by
`TestLoggingFixture` before any test runs) completed without throwing. This scenario is tested
by `GitClient_IsWorkingTreeClean_CleanRepo_ReturnsTrue` (one such passing test), covering
`AgentControl-System-DiagnosticLogging`.

**AgentControl_About_AboutButton_OpensAboutWindowShowingVersionAndCopyright**: The About
button is clicked; the About window opens showing the application version, the copyright notice, the license
summary and a non-empty dependency list. This scenario is tested by
`windows@AboutButton_Click_OpensAboutWindowShowingVersionAndCopyright`, covering
`AgentControl-System-About` and `AgentControl-Program-Version`. That the dialog is modal is the
`AgentControl_DialogModal_SettingsAndAboutDisableTheMainWindowUntilTheyClose` scenario.

**AgentControl_AboutVersionRow_VersionIsLabeledAndWithoutBuildMetadata**: In the same run the version is one short,
labeled "Version" value: it contains no build metadata (no `+`) and does not repeat the product name. This scenario is
tested by `windows@AboutButton_Click_OpensAboutWindowShowingVersionAndCopyright`, covering
`AgentControl-System-AboutVersionRow`. That the full build version is the tooltip of the value is verified in the
headless window tests of the `LauncherUI` subsystem.

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

**AgentControl_MultipleSources_ZipAndNuGetSourcesSideBySide**: The published application is run with a zip
source and a NuGet folder source configured side by side, sources of both kinds are added through the Settings
dialogs, and a package is selected from the NuGet sources, proving the tool supports several configured sources.
This scenario is tested by
`windows@UpgradeMenuItem_Click_NuGetFolderSourceOffersNewestVersion_UpgradesFromThatSource`,
`windows@SettingsWindow_AddZipPackageFolderViaDialog_ListsSourceAndPersistsIt`,
`windows@SettingsWindow_AddNuGetPackageFolderViaDialog_RequiresPackageIdAndPersistsIt`,
`windows@SelectPackage_FromNuGetFolderSource_PinsThePackageAndCardShowsPackageChip` and
`windows@SettingsWindow_AddNuGetServerWithSignIn_SelectPackagePinsServerSourceAndStoresNoPassword`, covering
`AgentControl-System-MultipleSources`.

**AgentControl_SourcesPersisted_AddedSourcesAreInTheSavedSettings**: A zip-package folder source and a
NuGet-package folder source are added through the Settings dialogs and the Settings window is saved; the settings
file in the isolated configuration directory then holds both sources. This scenario is tested by
`windows@SettingsWindow_AddZipPackageFolderViaDialog_ListsSourceAndPersistsIt` and
`windows@SettingsWindow_AddNuGetPackageFolderViaDialog_RequiresPackageIdAndPersistsIt`, covering
`AgentControl-System-SourcesPersisted`.

**AgentControl_LegacySourceMigration_SingleFolderSettings_BecomeTheDefaultSource**: The end-to-end tests that
seed the older single package-source folder setting (no source list) select a package from that folder and upgrade
from it, which works only when the folder was carried forward as the zip source named `Default`. This scenario is
tested by `windows@SelectPackageMenuItem_Click_AppliesChosenPackageAndSyncsManagedFolders` and
`windows@UpgradeMenuItem_Click_SyncsManagedFoldersAndShowsReleaseNotes`, covering
`AgentControl-System-LegacySourceMigration`.

**AgentControl_NuGetSources_FolderFeedAndServer_AppliesPackageLikeAZipPackage**: A repo is
given a package chosen from a real NuGet folder feed; the managed folders, release notes, and pin
are produced as for a zip package. The published application is run against a real NuGet folder feed and upgrades
a pinned repo from it, and against an in-process NuGet server from which it applies a package. This scenario is
tested by
`windows@UpgradeMenuItem_Click_NuGetFolderSourceOffersNewestVersion_UpgradesFromThatSource`,
`windows@SelectPackage_FromNuGetFolderSource_PinsThePackageAndCardShowsPackageChip` and
`windows@SettingsWindow_AddNuGetServerWithSignIn_SelectPackagePinsServerSourceAndStoresNoPassword`, covering
`AgentControl-System-NuGetSources`.

**AgentControl_NuGetTransportSecurity_PlainHttpRefusedUntilAllowed**: In the Add NuGet-Package Server dialog a
plain `http` address is refused with a message until "Allow insecure HTTP" is ticked for that source. This scenario
is tested by
`windows@SettingsWindow_AddNuGetServerWithSignIn_SelectPackagePinsServerSourceAndStoresNoPassword`, covering
`AgentControl-System-NuGetTransportSecurity`.

**AgentControl_NuGetRepoConfigUntouched_RepoNuGetConfigIsNeitherUsedNorChanged**: A repo that carries its own
`nuget.config` - pointing at an unreachable source and redirecting the package folder to a folder inside the repo -
is upgraded from its configured NuGet folder source; the upgrade succeeds, the `nuget.config` is byte for byte as it
was, the redirected folder was never created and no other NuGet configuration file appeared. This scenario is
tested by `windows@UpgradeMenuItem_Click_PinnedToNuGetFolderSource_UpgradesFromThatSourceOnly`, covering
`AgentControl-System-NuGetRepoConfigUntouched`.

**AgentControl_CredentialSafety_TypedPasswordIsNeverWritten**: In the first end-to-end scenario described under
Verification Approach, a password typed in the sign-in dialog is used for the connection test and again, without a
second dialog, for Select Package, and is then absent from the settings file, the repo's pin file, every log and
text file in the configuration directory and every file in the target repo. In the remember scenario, ticking
"Remember on this computer" results in the sign-in being handed to the emulated helper and still being found in
nothing AgentControl wrote. What no log line may contain in every form an address, header or exception can take,
including an exception that reaches the process-wide handlers or escapes startup, is verified at the subsystem and
unit levels and covers the child requirements. This scenario is tested by
`windows@SettingsWindow_AddNuGetServerWithSignIn_SelectPackagePinsServerSourceAndStoresNoPassword` and
`windows@SettingsWindow_SignInWithRememberTicked_SavedOnlyByTheUsersCredentialHelper`, covering
`AgentControl-System-CredentialSafety`. Residual risk, stated plainly: the tests prove what AgentControl
supplies; the NuGet client, once a server has authenticated, answers 401 challenges from other hosts that
the server's index advertises with the same credential, which is why users must only add servers they trust.

**AgentControl_CredentialSessionScope_SessionOnlyUnlessRememberedInTheUsersHelper**: The sign-in dialog shows the
server's origin and "Used for this session only - AgentControl does not save it."; a typed sign-in is reused within
the session without a second dialog. Ticking "Remember on this computer" (which then explains that the user's Git
credential helper keeps it) hands the sign-in to the emulated helper and to nothing else, and with no helper the
remember option is shown disabled and "Forget saved sign-in" is unavailable. This scenario is tested by
`windows@SettingsWindow_AddNuGetServerWithSignIn_SelectPackagePinsServerSourceAndStoresNoPassword`,
`windows@SettingsWindow_SignInWithRememberTicked_SavedOnlyByTheUsersCredentialHelper` and
`windows@SettingsWindow_NoCredentialHelper_RememberAndForgetAreUnavailable`, covering
`AgentControl-System-CredentialSessionScope`.

**AgentControl_SavedSignInLifecycle_UsedWithoutAskingAndRemovedWhenRejectedOrForgotten**: A sign-in the helper
already holds connects without any sign-in dialog; "Forget saved sign-in" asks for confirmation naming the server,
leaves the sign-in alone when declined and removes it from the helper when confirmed, saying so; and a saved
sign-in that the server refuses is removed from the helper. This scenario is tested by
`windows@SettingsWindow_SavedSignIn_UsedWithoutDialogAndForgottenOnRequest` and
`windows@SettingsWindow_SavedSignInRejectedByServer_IsRemovedFromTheUsersHelper`, covering
`AgentControl-System-SavedSignInLifecycle`.

**AgentControl_CredentialOriginScope_SignInIsNeverOfferedToAnotherServer**: Two mock servers on different origins
both need a sign-in. After signing in to the first, the user adds and tests the second: the second server gets its
own sign-in dialog naming its own origin, and the credential for the first server is found in the requests the
first server received and in none of the requests the second received. This scenario is tested by
`windows@SettingsWindow_SignInToOneServer_IsNeverOfferedToAnotherServer`, covering
`AgentControl-System-CredentialOriginScope`.

**AgentControl_PackageAuthenticity_SignatureRequirementRefusesUnsignedPackages**: A repo pinned to a NuGet folder
source that requires signed packages is offered the newer, unsigned package (listing does not look at signatures); the
Upgrade action is refused with a message that says the package is not validly signed and names the source, and nothing
is extracted and the pin is unchanged; the package chip also shows that the source requires signatures. The rest of
the signature rules - acceptance of a
valid signature with no pin and with the author's certificate pinned, refusal when another publisher is pinned, the
user's own NuGet trusted-signer settings being unable to widen or narrow the pin, the re-check of a package cached
before the requirement was turned on, and the refusal to list or supply on a platform that cannot verify
signatures - needs a real signed package or a different platform, is specified by the child requirements and is
verified at the subsystem and unit levels only; it is not claimed as system-level evidence. This scenario is
tested by
`windows@UpgradeMenuItem_Click_NuGetSourceRequiresSignaturesAndPackageUnsigned_IsRefusedAndRepoUnchanged`, covering
`AgentControl-System-PackageAuthenticity`.

**AgentControl_PinNamesNoSource_OnlyPackageVersionAndIdentityAreRecorded**: A pin never names a package source: the pin
file written after choosing a package from a NuGet-package folder source, and from a Git source, holds the package,
the version and the files identity and no `Source` or `SourceRevision` property, while the card shows the package chip
with the source that supplies it (kept in the user's own settings). A repo whose pin was recorded without any identity
of its files (an older pin) is still served from the configured source (here `Default`) and is never refused. The
published application shows both behaviors: Upgrade from a NuGet folder source, which offers a newer version than
the zip source does for the package name, upgrades from that source and writes a pin without a source, and a repo with
a pin that no configured source offers shows a package source problem together with the upgrade to the newer version
a source does offer, and keeps its pin file unchanged. This scenario is tested by
`windows@SelectPackage_FromNuGetFolderSource_PinsThePackageAndCardShowsPackageChip`,
`windows@SelectPackage_FromGitSource_PinsPackageAndFilesIdentityAndCardShowsGitSourceChip`,
`windows@UpgradeMenuItem_Click_NuGetFolderSourceOffersNewestVersion_UpgradesFromThatSource`,
`windows@UpgradeMenuItem_Click_SyncsManagedFoldersAndShowsReleaseNotes` and
`windows@RepoCard_NoConfiguredSourceOffersPinnedVersion_ShowsSourceProblemAndStillOffersNewerVersion`, covering
`AgentControl-System-PinNamesNoSource`, `AgentControl-System-LegacyPinSourceHint`,
`AgentControl-System-NoSourceOffersProblem` and `AgentControl-System-UpgradeAcrossSources`.

**AgentControl_PortablePin_AnotherSourceWithTheIdenticalFilesSuppliesThePackage**: A repo whose committed pin was made
from a source this machine does not have, while a configured source - of the same or another kind - delivers the
identical files, shows its package on the card with the supplying source named instead of a package source problem, and
the pin file is left as it was; for such a repo the upgrade chip is offered for the newest version of the configured
sources and the same files are restored on a fresh clone from the verified source only; a source whose files differ is
never used. The published application is not run for this scenario (it needs two differently named configurations of
one repo, which the FlaUI fixtures do not model, and the application is not started during this change); it is
verified end to end through the real main window, headless, with real zip sources and a pin written by a real apply:
`MainWindow_PinNamesNoSourceAndIdenticalFilesElsewhere_ChipNamesSupplierAndShowsNoProblem` and
`MainWindow_PinNamesNoSource_UpgradeChipOffersNewestVersionFromConfiguredSource`, covering
`AgentControl-System-PortablePin` and `AgentControl-System-UpgradeAcrossSources`. The rules (the order of the sources,
the identical-files check, the hints for a pin without an identity, the unchanged pin) are verified at the unit and
subsystem levels (see the `LauncherUI` and `RepoCardViewModel` verification designs).

**AgentControl_PackageContentIntegrity_IdentityIsRecordedForEveryKindOfSource**: In the published application,
selecting a package for a never-pinned repo writes a pin (schema version 4) whose content identity names what was
applied, computed independently by the test: for a zip source, for a NuGet-package folder source and for a Git source
the `files-sha256:` identity of the agent files the package delivers (the same value whatever the container). The pin
holds nothing else about the source (the Git run is
`SelectPackage_FromGitSource_PinsPackageAndFilesIdentityAndCardShowsGitSourceChip`, described with the Git
scenarios below). This scenario is tested by `windows@SelectPackageMenuItem_Click_RecordsFilesIdentityInPinFile`,
`windows@SelectPackage_FromNuGetFolderSource_RecordsFilesIdentityInPinFile` and
`windows@SelectPackage_FromGitSource_PinsPackageAndFilesIdentityAndCardShowsGitSourceChip`, covering
`AgentControl-System-PackageContentIntegrity` and `AgentControl-System-PinFile`. For a NuGet package the identity
describes the copy held on this machine (see the NuGet source unit verification), so a republish in the feed of a
version already held is not noticed until that copy is cleared; the NuGet scenario of the next paragraph republishes
a version the application has not held yet.

**AgentControl_ChangedContentRefusedAtLaunch_RepublishedPackageIsExplainedAndNothingIsSynced**: In the published
application a repo is pinned to 1.0.0 with the identity of the original package, its managed folders are missing, and
the 1.0.0 package has since been republished with other content - in a zip folder in the first run, in a NuGet-package
folder in the second. Clicking Launch shows a message that the agent files were not synced because the package now
contains different files than the copy the repo was set up with, no managed folder is created, the pin file is byte
for byte unchanged, and the launch goes on: the configured agent tool is still invoked in the repo's working directory
(how that fits the system Launch requirement is a pre-existing follow-up, not changed by this scenario). The refusal
of an Upgrade of a republished version is a rule of the repo card, specified by the
child requirements and verified at the subsystem and unit levels; the application offers an Upgrade only for a newer
version than the one pinned, so there is no end-to-end path to it and it is not claimed as system-level evidence. This
scenario is tested by `windows@LaunchButton_Click_PinnedVersionRepublished_ExplainsAndSyncsNothing` and
`windows@LaunchButton_Click_NuGetPinnedVersionRepublished_ExplainsAndSyncsNothing`, covering
`AgentControl-System-ChangedContentRefusedAtLaunch`.

**AgentControl_IdentityAdoptedOnUpgrade_PinWithoutIdentityRecordsOneWithoutWarning**: A repo whose pin has no identity
(as written before identities existed) is upgraded from 1.0.0 to 1.1.0 with the upgrade chip: the release notes open,
no message is shown, and the pin now holds the files identity of the 1.1.0 package. This scenario is tested by
`windows@UpgradeChip_Click_PinWithoutIdentity_AdoptsIdentityOfNewVersionWithoutWarning`, covering
`AgentControl-System-IdentityAdoptedOnUpgrade`.

**AgentControl_ChangedContentAcceptedDeliberately_ChangePackageOnAPinnedRepoAsksFirst**: A pinned repo's card menu
offers "Change package...", which opens the Select Package dialog on the source, package and version that supply the
repo's files (run against a real local bare Git repository, git isolated from the user's configuration and credential
helpers, no network, in the published application). Choosing the version a repo already uses when its content has
changed (a republished package, or a Git tag moved to a commit with other files) shows the plain-language question
that the package now contains different files, changes nothing when declined and applies the new content, recording
its new identity, only when the user confirms; a changed version is never applied by a background check, an Upgrade
or the restore at launch, and no "Tag moved" state exists. This scenario is tested by
`windows@ChangePackageMenuItem_OnPinnedGitRepo_OpensDialogOnTheSupplyingSourcePackageAndVersion` (the dialog in the
published application) and `MainWindow_SelectingRepublishedPinnedVersion_AsksFirstAndAppliesOnlyWhenConfirmed` (the
question and its answers through the real main window, headless), covering
`AgentControl-System-ChangedContentAcceptedDeliberately`. The confirmation for a republished zip and for a Git tag is
covered at the subsystem and unit levels.

**AgentControl_UpgradeFromChip_ClickStartsTheUpgrade**: On a repo pinned to 1.0.0 with 1.1.0 at the source, the
real application shows the upgrade chip as a button (invocable through UI Automation); invoking it runs the same
flow as the card menu's Upgrade item - the release notes window opens and the four managed folders are on disk.
This scenario is tested by `windows@UpgradeChip_Click_SyncsManagedFoldersAndShowsReleaseNotes`, covering
`AgentControl-System-UpgradeFromChip`.

**AgentControl_Appearance_ChoosingLightThenDarkReThemesWithoutRestart**: In the real application, Settings'
Appearance picker is set to Light and saved, and the main window's app bar background (measured from the pixels of
a screenshot of the window) turns light without a restart and the file holds `"Theme": "Light"`; then Dark is
chosen and saved and the app bar turns dark. The test never changes the operating-system theme (it chooses
explicit themes, not the system default). This scenario is tested by
`windows@SettingsWindow_ChooseLightThenDark_ReThemesMainWindowWithoutRestart`, covering
`AgentControl-System-Appearance`. A second scenario shows that Settings has an Appearance heading with the Theme
picker under it, above the Tools heading and the git setting, tested by
`windows@SettingsWindow_AppearanceIsItsOwnSectionAboveTools`, also covering `AgentControl-System-Appearance`.

**AgentControl_RepoGroups_MoveToNewExistingAndOutOfGroup**: In the real application a repo is moved to a new group
through the card's "..." menu (Move to group, then New group...), a name is typed into the prompt and confirmed,
and a header named after the group, counting one repo and expanded, appears with the repo under it. In a second
run a repo is moved to an existing group through the same submenu: that section now counts both repos and the
section the repo left is gone. In a third run "Remove from group" takes the only grouped repo out of its group
and the list is flat again, without any header. Renaming and removing a group from its header is not exercised
end to end (see the Verification Approach). This scenario is tested by
`windows@CardMenu_MoveToNewGroup_ShowsGroupHeaderAndStoresInSettingsOnly`,
`windows@CardMenu_MoveToExistingGroup_JoinsThatSectionAndStoresInSettingsOnly` and
`windows@CardMenu_RemoveFromGroup_ReturnsRepoToUngroupedAndRemovesTheHeader`, covering
`AgentControl-System-RepoGroups`.

**AgentControl_RepoGroupSections_FavoritesThenGroupsAlphabeticallyThenUngrouped**: With one favorite, repos in two
groups and one repo in no group, the real application lists the sections in the order Favorites, then the groups
alphabetically, then the repos in no group, with each repo under its section (a favorite only under Favorites); and
after the last repo has been taken out of its group no section header is left. This scenario is tested by
`windows@RepoList_FavoritesAndGroups_SectionsAreFavoritesThenGroupsAlphabeticallyThenUngrouped` and
`windows@CardMenu_RemoveFromGroup_ReturnsRepoToUngroupedAndRemovesTheHeader`, covering
`AgentControl-System-RepoGroupSections`.

**AgentControl_RepoGroupCollapseRemembered_CollapsedSectionStaysCollapsedAfterRestart**: Clicking a section header
in the real application collapses the section (its repos leave the list) and stores the choice; after the
application is closed and started again the section is still collapsed, and clicking the header expands it. This
scenario is tested by `windows@GroupHeaderToggleButton_Click_CollapsesSectionAndRemembersItAfterRestart`, covering
`AgentControl-System-RepoGroupCollapseRemembered`.

**AgentControl_RepoGroupFilter_FilterFindsRepoInCollapsedGroup**: Settings put the seeded repo in a group that is
remembered as collapsed: its header is shown collapsed and only the other repo is listed; typing the seeded repo's
path in the filter box lists that repo (the filter expands the group) and nothing else, and clearing the filter
collapses the group again. This scenario is tested by
`windows@FilterTextBox_RepoInCollapsedGroup_IsFoundByFilterAndHiddenAgainAfterwards`, covering
`AgentControl-System-RepoGroupFilter`.

**AgentControl_RepoGroupsSettingsOnly_GroupIsStoredInSettingsAndNeverInTheRepo**: After a repo is moved to a new
group, and in another run to an existing group, the group is found in the settings file and the repository folder
stays empty (nothing is written to the repository). This scenario is tested by
`windows@CardMenu_MoveToNewGroup_ShowsGroupHeaderAndStoresInSettingsOnly` and
`windows@CardMenu_MoveToExistingGroup_JoinsThatSectionAndStoresInSettingsOnly`, covering
`AgentControl-System-RepoGroupsSettingsOnly`.

**AgentControl_ListChangesKept_FavoriteCollapseAndColorAreStored**: The favorite star, a section's collapse and a
card color are each changed in the real application and found in the settings file (the collapse also after a
restart). That a change which cannot be saved changes nothing and is reported is verified in the `LauncherUI`
subsystem and unit tests, because a failing save cannot be provoked through the published application. This scenario
is tested by `windows@FavoriteToggleButton_ToggledOnThenOff_MovesRepoBetweenSectionsAndStoresChoice`,
`windows@GroupHeaderToggleButton_Click_CollapsesSectionAndRemembersItAfterRestart` and
`windows@CardMenu_CardColor_ChooseSwatchStoresColorInSettingsOnly`, covering
`AgentControl-System-ListChangesKept`.

**AgentControl_Favorites_ToggleStarMovesRepoBetweenSections**: In the real application, with one repo in a
group and one in none, the first card's star (accessible name "Favorite") is toggled on: a Favorites section
holding one repo appears, the star reports "on" and the settings file holds `"IsFavorite": true`; toggling it off
removes the Favorites section, the star reports "off" and the file no longer has the favorite. This scenario is
tested by `windows@FavoriteToggleButton_ToggledOnThenOff_MovesRepoBetweenSectionsAndStoresChoice`, covering
`AgentControl-System-Favorites`. (Real pointer and key input on the star are tested in the headless window, see the
LauncherUI verification.)

**AgentControl_MissingRepoRecheck_CheckAgainFindsTheFolder**: In the real application a repo whose folder does not
exist shows the "Folder missing" badge, its card menu offers "Check again" enabled, and after the folder is
created, choosing it clears the badge. This scenario is tested by
`windows@RefreshMenuItem_MissingFolder_IsEnabledAsCheckAgain_AndFindsFolderOnceBack`, covering
`AgentControl-System-MissingRepoRecheck`.

**AgentControl_DialogKeyboard_KeyboardStaysInsideTheDialog**: In the real application, with the Settings window
and then the About window open over the main window, a real mouse click on a main-window button opens nothing,
typed keys do not change the main window's filter box, and Tab and Shift+Tab go all the way round without the
keyboard focus leaving the dialog; the Select Package dialog opened from a repo card starts with the keyboard focus
on its first control, keeps Tab and Shift+Tab inside, and is canceled by Escape (pinning nothing) with the focus
handed back to the main window. This scenario is tested by
`windows@SettingsWindow_Opened_ClicksAndKeysOnMainWindowDoNotReachIt`,
`windows@AboutWindow_Opened_ClicksAndKeysOnMainWindowDoNotReachIt` and
`windows@SelectPackageWindow_Opened_FocusStartsInsideTabCyclesAndEscapeCancels`, covering
`AgentControl-System-DialogKeyboard`. The focus ring is not claimed end to end (see the Verification Approach).

**AgentControl_DialogModal_SettingsAndAboutDisableTheMainWindowUntilTheyClose**: In the real application, while the
Settings window is open the operating system has disabled the main window behind it (read from the main window's own
window handle) and saving enables it again; while About is open the main window is disabled and About itself is
enabled, and closing About enables the main window again and hands it the focus; and with either dialog open a real
click on the main window does not reach it. This scenario is tested by
`windows@SettingsWindow_Opened_MainWindowBehindItIsDisabledUntilItCloses`,
`windows@SettingsWindow_Opened_ClicksAndKeysOnMainWindowDoNotReachIt`,
`windows@AboutWindow_Opened_MainWindowIsDisabledUntilItCloses_AndRegainsFocus` and
`windows@AboutWindow_Opened_ClicksAndKeysOnMainWindowDoNotReachIt`, covering `AgentControl-System-DialogModal`.
The other dialogs are verified in the headless windows of the `LauncherUI` subsystem.

**AgentControl_UpToDateCue_CheckMarkOnlyWhenPinnedPackageIsNewest**: In the real application a repo pinned to the
only version of a package shows the "Agent files are up to date" check mark in its package chip; with a newer version
at the source the upgrade badge appears and the check mark does not. This scenario is tested by
`windows@UpToDateCue_ShownOnlyForPinnedPackageThatIsUpToDate`, covering `AgentControl-System-RecentRepos`.

**AgentControl_CardColor_PaletteOffersEveryColorAndAutomaticClearsTheChoice**: In the real application the card's
"..." menu item "Card color..." opens the palette pop-up, which offers a swatch for every palette color, each by its
name, and Automatic; invoking the Teal swatch stores `"CardColor": "Teal"` in the settings file, and choosing
Automatic clears the stored choice again. This scenario is tested by
`windows@CardMenu_CardColor_ChooseSwatchStoresColorInSettingsOnly` and
`windows@CardMenu_CardColor_PaletteOffersEveryColorAndAutomaticClearsTheChoice`, covering
`AgentControl-System-CardColor`. The legibility of the letters on every palette color is verified in the
`LauncherUI` subsystem tests.

**AgentControl_CardColorKeepsStatus_StatusAccentIsUnchangedByTheChosenColor**: In both card-color scenarios the
card's status accent bar keeps its accessible status text before and after the color is chosen and after Automatic is
chosen. This scenario is tested by `windows@CardMenu_CardColor_ChooseSwatchStoresColorInSettingsOnly` and
`windows@CardMenu_CardColor_PaletteOffersEveryColorAndAutomaticClearsTheChoice`, covering
`AgentControl-System-CardColorKeepsStatus`.

**AgentControl_CardColorSettingsOnly_ColorIsStoredInSettingsAndNeverInTheRepo**: After the Teal swatch is invoked the
color is found in the settings file and the repository folder stays empty. This scenario is tested by
`windows@CardMenu_CardColor_ChooseSwatchStoresColorInSettingsOnly`, covering
`AgentControl-System-CardColorSettingsOnly`.

**AgentControl_AddSourceMenu_OffersTheSourceTypesWithIconAndDescription**: In the real application the Settings
window's one Add button opens a menu with the zip-package folder, NuGet-package folder and NuGet-package server
types, each with an icon and a description, and a Git repository item that titles itself "Git repository", carries
the Git icon (the test finds its AutomationId `AddGitSourceButton` and its title in the UI Automation tree; the
icon is a drawn glyph that is not read back) and, when invoked, opens the source editor in Git mode, with the
defaults of that editor in place. This scenario is tested by
`windows@SettingsWindow_AddMenu_OffersAllFourSourceTypesWithIconAndDescription` (all four types, with their icons and
descriptions, and the empty-list text naming them) and
`windows@SettingsWindow_AddGitRepositoryViaDialog_TestsConnectionListsTagsAndPersistsIt` (the Git item and its
editor), covering `AgentControl-System-AddSourceMenu`.

**AgentControl_AppearanceAtStartup_SavedThemeIsInEffectFromTheFirstWindow**: The application is launched with Light,
and in another run Dark, already saved in its settings: the main window's app bar has the theme's brightness before
anything is changed, Settings offers the saved theme as the current choice, and the start did not overwrite the
saved choice. The operating-system theme is never changed. This scenario is tested by
`windows@MainWindow_LaunchWithPersistedLightTheme_AppliesLightFromStartup` and
`windows@MainWindow_LaunchWithPersistedDarkTheme_AppliesDarkFromStartup`, covering
`AgentControl-System-AppearanceAtStartup` (and the `Program` unit requirement
`AgentControl-Program-StartupTheme`).

**AgentControl_WindowsLegible_EveryWindowIsLegibleInBothThemes**: With the theme seeded in the settings file, each
window of the application is opened in the real application - the main window, Settings, the source editor, About,
the group-name prompt, the confirmation, the message box, the package picker, the release notes window and the
sign-in prompt - and the pixels of a screenshot show that the backdrop behind a text of the window has the
brightness of the theme (light in the light theme, dark in the dark theme) and that the text contrasts with it by at
least 4.5:1. The contrast of every control state and the keyboard focus ring are not measured. This scenario is
tested by `windows@MainSettingsSourceEditorAndAbout_LightTheme_RenderLegibly`,
`windows@MainSettingsSourceEditorAndAbout_DarkTheme_RenderLegibly`,
`windows@GroupNameConfirmationAndMessageBox_LightTheme_RenderLegibly`,
`windows@GroupNameConfirmationAndMessageBox_DarkTheme_RenderLegibly`,
`windows@SelectPackageAndReleaseNotes_LightTheme_RenderLegibly`,
`windows@SelectPackageAndReleaseNotes_DarkTheme_RenderLegibly`,
`windows@CredentialPrompt_LightTheme_RendersLegibly` and `windows@CredentialPrompt_DarkTheme_RendersLegibly`,
covering `AgentControl-System-WindowsLegible`. The 3:1 contrast of the status bars and the favorite star, which its
child requirement adds, is verified in the `LauncherUI` subsystem tests only.

**AgentControl_GitSources_AddedInSettingsAndChosenInSelectPackage**: In the published application, against a real local
bare Git repository whose tags include `release/1.0.0` and `release/1.1.0` and two that are not versions (git isolated
from the user's configuration and credential helpers, no network): the Add menu's Git repository item opens the editor
in Git mode with the tag pattern `v{version}` in place; with an alias, the repository path and the pattern
`release/{version}` entered, Test connection reports "Connection OK" with the newest version, the summary "2 of 4 tags
match the pattern." and a list holding the two version tags (and not the others); Save and then Save in Settings write
a source of type `Git` with the address, the pattern and the package name (the alias) to the settings file. In a second
run a pattern whose capitalization differs ("Release/{version}") gives "No matching tags" with a hint that names a real
tag (`release/1.0.0`) and says the fixed parts are case-sensitive, no preview list is shown, and correcting the pattern
makes the same test succeed. In a third run, with the Git source preconfigured, a never-pinned repo is offered Select
Package... (and not Change package...); choosing the package and version 1.0.0 applies the tag's files,
opens its release
notes and writes a pin holding the package, version and the files identity (no source and no commit), and the
card shows the package and version, the alias and (as drawn ink beside the alias - the icon itself is decorative and
not in the UI Automation tree) the Git icon. This scenario is tested by
`windows@SettingsWindow_AddGitRepositoryViaDialog_TestsConnectionListsTagsAndPersistsIt`,
`windows@SettingsWindow_AddGitRepositoryWithWrongCasePattern_ShowsNoMatchingTagsThenSucceedsWhenFixed` and
`windows@SelectPackage_FromGitSource_PinsPackageAndFilesIdentityAndCardShowsGitSourceChip`, covering
`AgentControl-System-GitSources`, `AgentControl-System-GitSourceSetup` (the first two),
`AgentControl-System-GitSourceSelection` and `AgentControl-System-GitSourcePresentation` (the third). The tag that the
card shows in its tooltip is not read back end to end - the tooltip is not reliably
reachable through UI Automation - so `AgentControl-System-GitSourcePresentation` states only the Git origin (the icon
and the alias), and the tooltip is verified in the `LauncherUI` subsystem and unit tests.

**AgentControl_GitSourceSettings_AddressPatternAndSubfolderAreKeptAndNeverHoldACredential**: In the published
application the source added through the Settings dialogs is persisted with its address, tag pattern and package name
(the first scenario above), and an address that carries a password (`https://user:...@host/...`) is refused in the
Git source editor with an error that does not repeat the secret and the source cannot be saved; after leaving the
editor and saving the settings, the secret is in no file the application wrote. This scenario is tested by
`windows@SettingsWindow_AddGitRepositoryViaDialog_TestsConnectionListsTagsAndPersistsIt` and
`windows@SettingsWindow_AddGitRepositoryWithCredentialInAddress_IsRefusedAndNothingIsSaved`, covering
`AgentControl-System-GitSourceSettings`.

**AgentControl_GitSourceSafety_HostileRepositoryAndHostileUserConfigurationRunAndEscapeNothing**: The tests of
`GitSourceSafetyTests.cs` run the published application with the real git against a real local bare Git repository
(built by `GitFixtureRepository.cs`, isolated from the developer's global and system configuration and credential
helpers, no network), through the same windows a user uses. They cover the following requirements.

- `AgentControl-System-GitSourceSafety` (no code from the repository, the remote or the user's configuration): the
  user's global git configuration is replaced by one that would run a post-checkout hook for every new repository (a
  template directory and a global hooks path) and a `pwn` filter program for every file whose attributes select it
  (and asks for line-ending conversion); a control first shows that a plain `git checkout` of the package's tag under
  that configuration really runs both, and the test is skipped when it cannot be shown. Choosing the package in Select
  Package then applies it normally, neither the hook nor the filter ran (the markers they would write do not exist) and
  the delivered files hold exactly the bytes of the commit - carriage returns and an identifier the attributes would
  have expanded are untouched. A tag whose layout holds a submodule is refused (below). This part is tested by
  `windows@SelectPackage_FromGitRepositoryUnderHostileUserConfiguration_RunsNoCodeAndAppliesRawBytes` and
  `windows@SelectPackage_FromGitRepositoryWithSubmoduleInLayout_IsRefusedAndWritesNothing`.
- `AgentControl-System-GitSourceTransport`: an address that asks git to run a program (`ext::sh -c "..."`) is refused
  by the Git source editor - the error is shown, the source cannot be saved - and the program is never run (its marker
  file does not exist). Git's own refusal of a transport other than the one of the address is verified in the
  `GitIntegration` subsystem tests. This part is tested by
  `windows@SettingsWindow_AddGitRepositoryWithRemoteHelperAddress_IsRefusedAndRunsNothing`.
- `AgentControl-System-GitSourceLayoutOnly`: the repository of the first bullet also holds a `README.md` and a `docs`
  folder; after the package is applied the repo holds neither, nor a `.git` folder - only the package layout. This part
  is tested by `windows@SelectPackage_FromGitRepositoryUnderHostileUserConfiguration_RunsNoCodeAndAppliesRawBytes` and
  `windows@SelectPackage_FromGitSource_PinsPackageAndFilesIdentityAndCardShowsGitSourceChip`.
- `AgentControl-System-GitSourceRefusesLinks` and `AgentControl-System-GitSourceRefusesCollisions`: a tag whose layout
  holds a symbolic link in a managed folder (staged without creating a link on disk, which needs no privilege), a
  submodule, or two files whose names differ only in letter case is chosen in Select Package; a message explains that
  the package holds "a symbolic link", "a submodule" or names that "differ only in letter case", the repo gets no
  files, no pin and no release notes, nothing of the tag (no folder named by its commit, no temporary folder) is left in
  the application's cache, and nothing is written outside the repo and the configuration folder. This part is tested by
  `windows@SelectPackage_FromGitRepositoryWithLinkInLayout_IsRefusedAndWritesNothing`,
  `windows@SelectPackage_FromGitRepositoryWithSubmoduleInLayout_IsRefusedAndWritesNothing` and
  `windows@SelectPackage_FromGitRepositoryWithCaseCollidingNames_IsRefusedAndWritesNothing`.

**AgentControl_GitSignIn_ThroughAPrivateGitServerAndTheUsersOwnHelper**: The tests of `GitSignInTests.cs` run the
published application with the real git against a private Git server of the test (`GitHttpsServer.cs`, shared with the
`GitIntegration` subsystem tests: Kestrel on a loopback port, TLS with a throw-away self-signed certificate, the smart
HTTP protocol served by the real `git upload-pack`, and a 401 for every request without the right HTTP Basic
credentials). The user's own sign-in is replaced by a credential helper in an isolated global git configuration
(certificate checks off there, because the certificate is self-signed) that behaves like Git Credential Manager: it
supplies the credential only when it may show its sign-in UI, that is when `GCM_INTERACTIVE` is not `never`, and it
logs `asked` when it was asked and could not prompt and `prompted` when it supplied the credential. The tests assert on
that log, on what the server received and on what the card and the editor show; no real credential store and no
network are involved. This part is the only evidence of sign-in behavior against a server that really asks for one.
Git Credential Manager's own dialog (whether it can be shown from a hidden-console process) is not exercised: that is
a manual check on a machine with a private https repository.

- `AgentControl-System-GitSignInBackgroundNeverPrompts`: a repo whose package a Git source at the private server
  supplies (the card remembers that source); the
  application's background check at start-up asks git, git asks the helper, which may not prompt (`asked`, never
  `prompted`), the server receives only anonymous requests and no signed-in one, the card shows the red "Sign-in
  needed" chip, and no sign-in dialog opens. This part is tested by
  `windows@RepoCard_PinnedToPrivateGitRepository_BackgroundCheckNeverPromptsAndReportsSignInNeeded`.
- `AgentControl-System-GitSignIn`: on that card the user chooses Refresh (work the user started): the helper is now
  allowed to prompt and supplies the credential (`prompted`), the server receives a signed-in request, the "Sign-in
  needed" chip goes away, the check completes (the newer version is offered) and the password is in no file the
  application wrote; and in the Git source editor, Test connection to the private repository lets the helper prompt,
  reports "Connection OK" and shows the summary "2 of 4 tags match the pattern.". Select Package and Change package
  start user-started work in the same way and are verified at the `LauncherUI` subsystem and unit levels (the card
  hands the same user-started options to every one of them). This part is tested by
  `windows@RepoCard_PrivateGitRepositoryNeedingSignIn_RefreshMayPromptAndThenSignsIn` and
  `windows@SettingsWindow_TestConnectionToPrivateGitRepository_MaySignInAndListsTheTags`.
