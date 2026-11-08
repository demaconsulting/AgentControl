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
folder. Unit tests that back the disclosed exceptions above run on .NET 10 on any
supported platform with no additional environment setup
(`AgentControl-Platform-Net10`). The server-source end-to-end test also needs loopback networking: it starts
the in-process mock feed (Kestrel, hosted by the ASP.NET Core shared framework that the test project references)
on a fresh port.

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
- Several package sources can be configured and carried forward from the legacy setting, a NuGet
  folder source supplies packages applied like zip packages, a NuGet server source can be added with a
  typed sign-in and supplies packages the same way, and a repo pin is only ever resolved
  against the source it names.
- No credential typed during an end-to-end run is found in any file the application wrote; a sign-in the user chose
  to remember is found only in the user's own (emulated) credential helper, is used again without a dialog and is
  removed by "Forget saved sign-in".
- A repo can be moved into a new group through the real UI, the group shows as a header and is stored only in the
  user's settings (the repository stays untouched), the filter finds a repo inside a collapsed group, and a
  chosen card color is stored the same way.
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
`windows@SelectPackage_FromNuGetFolderSource_PinsThatSourceAndCardShowsPackageChip`, covering
`AgentControl-System-SelectPackage`.

**AgentControl_ServerSignIn_SelectPackageFromServer_AsksForSignInOnce**: A NuGet server source that needs
credentials is added; the user starts an operation on it (testing the connection, then selecting a package) and the
sign-in dialog opens, naming the server's origin and saying the credential is used for this session only. After
signing in, the operation continues, and selecting a package from the server asks for no second sign-in; the
pin then names the server source and the card chip shows its alias. This scenario is tested by
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
session), exercising the real `MainWindow` event subscription/dialog-routing wiring in
addition to the `RepoCardViewModel` unit tests that verify the same behavior in isolation.
These scenarios are tested by `windows@AgentsMdTemplateOffer_Accept_WritesTemplateToRepoRoot`
and `windows@AgentsMdTemplateOffer_Decline_DoesNotWriteFileAndDoesNotReprompt`, plus
`RepoCardViewModel_ApplySelectedPackage_NoAgentsMdAndPackageHasTemplate_RaisesAgentsMdTemplateOfferRequestedWithContent`,
`RepoCardViewModel_AcceptAgentsMdTemplate_WritesFileToRepoRootWithGivenContent`, and
`RepoCardViewModel_DeclineAgentsMdTemplate_PersistsDeclinedFlagInPinFile`, covering
`AgentControl-System-AgentsMdTemplate`.

**AgentControl_Settings_SourceDialogs_AddSourcesAndPersistPreferences**: The Settings window is
opened, a zip-package folder source is added through the add dialog and appears in the sources
list, and a NuGet-package folder source is added through its dialog, which cannot be saved until
a package ID is listed; after saving, the settings file in the isolated configuration directory
holds both sources. `SettingsWindowViewModel`'s `SaveCommand` is also executed with edited
git/agent-tool overrides and shell preference, proving the edited preferences are captured and
communicated for persistence (this part is verified only by the view-model's own unit test, a
disclosed granularity limitation). The Add NuGet-Package Server dialog is also exercised: plain `http` is refused
until "Allow insecure HTTP" is ticked, and after saving the source is listed as "Not checked" and persisted with its
insecure-HTTP choice. This scenario is tested by
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

**AgentControl_MultipleSources_ZipAndNuGetSourcesSideBySide**: The published application is run with a zip
source and a NuGet folder source configured side by side, sources of both kinds are added through the Settings
dialogs, and a package is selected from the NuGet sources, proving the tool supports several configured sources.
This scenario is tested by
`windows@UpgradeMenuItem_Click_PinnedToNuGetFolderSource_UpgradesFromThatSourceOnly`,
`windows@SettingsWindow_AddZipPackageFolderViaDialog_ListsSourceAndPersistsIt`,
`windows@SettingsWindow_AddNuGetPackageFolderViaDialog_RequiresPackageIdAndPersistsIt`,
`windows@SelectPackage_FromNuGetFolderSource_PinsThatSourceAndCardShowsPackageChip` and
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
`windows@UpgradeMenuItem_Click_PinnedToNuGetFolderSource_UpgradesFromThatSourceOnly`,
`windows@SelectPackage_FromNuGetFolderSource_PinsThatSourceAndCardShowsPackageChip` and
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

**AgentControl_SourcePinIntegrity_PinBoundToSource_NoSubstitution**: With the same package id
offered by two sources, a pin to the second source resolves only against it and the first source
is never contacted; a pin naming a source that is not configured gives an error state for the repo
and offers no upgrade from any other source. The published application shows both behaviors: a repo
pinned to the NuGet source is upgraded from it although the zip source offers a higher version of
the same package name, and a repo pinned to an unconfigured source shows a package source problem
badge, offers no upgrade, and keeps its pin file unchanged. This scenario is tested by
`windows@UpgradeMenuItem_Click_PinnedToNuGetFolderSource_UpgradesFromThatSourceOnly` and
`windows@RepoCard_PinnedToUnconfiguredSource_ShowsSourceProblemAndOffersNoUpgrade`,
covering `AgentControl-System-SourcePinIntegrity`.

**AgentControl_UpgradeFromChip_ClickStartsTheUpgrade**: On a repo pinned to 1.0.0 with 1.1.0 at the source, the
real application shows the upgrade chip as a button (invocable through UI Automation); invoking it runs the same
flow as the card menu's Upgrade item - the release notes window opens and the four managed folders are on disk.
This scenario is tested by `windows@UpgradeChip_Click_SyncsManagedFoldersAndShowsReleaseNotes`, covering
`AgentControl-System-UpgradeFromChip`.

**AgentControl_Appearance_ChoosingLightThenDarkReThemesWithoutRestart**: In the real application, Settings'
Appearance picker is set to Light and saved, and the main window's page background (measured from the pixels of
a screenshot of the window) turns light without a restart and the file holds `"Theme": "Light"`; then Dark is
chosen and saved and the page turns dark. The test never changes the operating-system theme (it chooses
explicit themes, not the system default). This scenario is tested by
`windows@SettingsWindow_ChooseLightThenDark_ReThemesMainWindowWithoutRestart`, covering
`AgentControl-System-Appearance`.

**AgentControl_RepoGroups_MoveToNewGroupShowsHeaderAndFilterFindsRepoInCollapsedGroup**: In the real
application a repo is moved to a new group through the card's "..." menu (Move to group, then New group...), a
name is typed into the prompt and confirmed, and a header named after the group, counting one repo and expanded,
appears with the repo under it; the group is found in the settings file, and the repository folder stays empty
(nothing is written to the repository). In a second run settings put the seeded repo in a group that is
remembered as collapsed: its header is shown collapsed and only the other repo is listed; typing the seeded
repo's path in the filter box lists that repo (the filter expands the group) and nothing else, and clearing the
filter collapses the group again. This scenario is tested by
`windows@CardMenu_MoveToNewGroup_ShowsGroupHeaderAndStoresInSettingsOnly` and
`windows@FilterTextBox_RepoInCollapsedGroup_IsFoundByFilterAndHiddenAgainAfterwards`, covering
`AgentControl-System-RepoGroups`.

**AgentControl_CardColor_ChooseSwatchStoresColorInSettingsOnly**: In the real application the card's "..."
menu item "Card color..." opens the palette pop-up, which offers a swatch for every palette color and Automatic;
invoking the Teal swatch stores `"CardColor": "Teal"` in the settings file, the repository folder stays
empty, and the card's status accent bar keeps its accessible status text. This scenario is tested by
`windows@CardMenu_CardColor_ChooseSwatchStoresColorInSettingsOnly`, covering `AgentControl-System-CardColor`.
