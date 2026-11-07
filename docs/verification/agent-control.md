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

`AgentControl-System-ResponsiveOperations` is verified mainly at unit level, with deliberately
slow and failing package sources, because a frozen UI thread is not observable through UI
automation without an artificially slow share; the end-to-end tests cover the happy path of
the same flows.

`AgentControl-System-CredentialSafety` has one end-to-end scenario, `NuGetServerSourceTests.cs`, which runs
the published application against an in-process mock NuGet V3 server (the same `MockNuGetFeed` the unit tests
use, linked into the UI test project; plain HTTP, so the source ticks "Allow insecure HTTP", and each launch of the
application gets a private `NUGET_HTTP_CACHE_PATH` so a reused port is never answered from an earlier run's
cache). It drives the
Add NuGet-Package Server dialog, the sign-in dialog and Select Package with the real NuGet client, then
searches every file the application wrote - the settings, the repo's pin and the configuration directory's
log and text files - for the password the test typed. That a credential is withheld from a second server is not
observable end to end and is verified one level down.

`AgentControl-System-CredentialSafety` also covers the opt-in to remember a sign-in, verified end to end in
`RememberedSignInTests.cs` with the arg-logger stub standing in for git and emulating the user's credential helper:
when the environment variable `ARGLOGGER_CREDENTIAL_STORE` names a file, the stub answers the helper question and the
`git credential fill|approve|reject` invocations from that file, which lives outside every folder the application
writes to, so a test can show that the sign-in is kept only by "the helper" and can never touch the developer's real
credential store. The saved-sign-in behavior that is not observable end to end is verified at the subsystem and unit
levels: through the credential logic with a fake credential store (a later session finds the saved sign-in without
asking, a rejected one is removed, a wrong password is never saved), through the real `git credential` protocol
against a throw-away helper in a temporary folder, and through a stub git that records exactly what is sent, in what
environment and within what time limit.

`AgentControl-System-PackageAuthenticity` has one end-to-end scenario in `MultipleSourcesTests.cs`, which runs the
published application against a real NuGet folder feed that holds an unsigned newer package, with the source
configured to require signed packages: the upgrade is refused with a message that says the package is not validly
signed and names the source, and the repo's files and pin are left unchanged. Producing a validly signed package
needs a real signing certificate, so acceptance of a valid signature, the publisher pin and the independence from the
user's own NuGet trust settings are verified at the subsystem and unit levels, against a real signed package downloaded
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

**AgentControl_Pull_PullButton_InvokesConfiguredGitExecutableWithPullArgument**: The Pull
button is clicked on a repo card with a clean working tree; the configured (stubbed) git
executable is invoked with the `pull` argument, proving the pull action is wired to the
configured git executable and gated on a clean working tree. This scenario is tested by
`windows@PullButton_Click_InvokesConfiguredGitExecutableWithPullArgument`, covering
`AgentControl-System-Pull`.

**AgentControl_SelectPackage_SelectPackageMenuItem_AppliesChosenPackageAndSyncsFolders**: The
Select Package menu item is clicked on an unpinned repo card, a package name/version is chosen
and confirmed; the repo's pin is set and its managed agent-file folders are synced to match,
proving the initial-pin selection flow works end-to-end. A second scenario chooses the NuGet
folder source in the dialog, selects its package and version, and confirms that the card then
shows the package chip naming that source. A third scenario selects a package from the NuGet server source
added in the previous test, with no second sign-in because the credential typed earlier is remembered for the
session, and confirms the pin names the server source and the card chip shows its alias. These scenarios are
tested by
`windows@SelectPackageMenuItem_Click_AppliesChosenPackageAndSyncsManagedFolders`,
`windows@SelectPackage_FromNuGetFolderSource_PinsThatSourceAndCardShowsPackageChip` and
`windows@SettingsWindow_AddNuGetServerWithSignIn_SelectPackagePinsServerSourceAndStoresNoPassword`, covering
`AgentControl-System-SelectPackage`.

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
communicated for persistence. The Add NuGet-Package Server dialog is also exercised: plain `http` is refused
until "Allow insecure HTTP" is ticked, **Test connection** against the mock server opens the sign-in dialog
(which names the server's origin and says the credential is used for this session only), the connection test
then reports success, and after saving the source is listed as "Not checked" and persisted with its
insecure-HTTP choice. The editor of a NuGet source also offers the signature options - a "Require signed packages"
checkbox and the publisher's certificate fingerprint field - verified with headless window tests: ticking the box
enables the field, an unreadable fingerprint blocks saving and a readable one is saved normalized. This scenario is
tested by
`windows@SettingsWindow_AddZipPackageFolderViaDialog_ListsSourceAndPersistsIt`,
`windows@SettingsWindow_AddNuGetPackageFolderViaDialog_RequiresPackageIdAndPersistsIt`,
`windows@SettingsWindow_AddNuGetServerWithSignIn_SelectPackagePinsServerSourceAndStoresNoPassword`,
`SettingsWindowViewModel_SaveCommand_Execute_InvokesOnSaveWithCurrentValuesAndRaisesSaved` and
`SourceEditorWindow_SignatureOptions_TickingEnablesTheFingerprintAndValidatesIt`,
covering `AgentControl-System-Settings`.

**AgentControl_ResponsiveOperations_SlowAndFailingSources_DoNotBlock**: An apply waiting on a slow
source reports busy, ignores a repeated request and can be cancelled without changing the pin,
and the background upgrade refresh still checks the cards of reachable sources when another
source is unreachable; the end-to-end selection of a package through the dialog confirms the
happy path of the same flow. A background upgrade check against a server that needs a sign-in never opens the
sign-in dialog. This scenario is tested by
`RepoCardViewModel_ApplySelectedPackageAsync_Running_IsBusyIgnoresSecondCallAndCancelLeavesPinUnchanged`,
`MainWindowViewModel_BeginUpgradeRefresh_OneSourceUnreachable_OtherCardsStillChecked`,
`RepoCardViewModel_RefreshUpgradeStatusAsync_BackgroundCheckNeedsSignIn_DoesNotPromptAndExplains`, and
`windows@SelectPackage_FromNuGetFolderSource_PinsThatSourceAndCardShowsPackageChip`, covering
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

**AgentControl_MultipleSources_SettingsRoundTripAndLegacyMigration**: Settings holding several
sources are saved and loaded back with every field intact, a legacy settings file that holds
only the single package-source folder is loaded as one zip source named `Default`, and sources of
both kinds are added through the Settings dialogs and persisted, proving the tool
supports several configured sources and carries the earlier setup forward. The published
application is also run with a zip source and a NuGet folder source configured side by side. This
scenario is tested by
`windows@UpgradeMenuItem_Click_PinnedToNuGetFolderSource_UpgradesFromThatSourceOnly`,
`windows@UpgradeMenuItem_Click_SyncsManagedFoldersAndShowsReleaseNotes` (older single-folder
settings migrated to `Default`),
`windows@SettingsWindow_AddZipPackageFolderViaDialog_ListsSourceAndPersistsIt`,
`windows@SettingsWindow_AddNuGetPackageFolderViaDialog_RequiresPackageIdAndPersistsIt`,
`windows@SelectPackage_FromNuGetFolderSource_PinsThatSourceAndCardShowsPackageChip`,
`windows@SettingsWindow_AddNuGetServerWithSignIn_SelectPackagePinsServerSourceAndStoresNoPassword`,
`SettingsStore_SaveThenLoad_RoundTripsAllFields` and
`SettingsStore_Load_LegacyPackageSourcePath_MigratesToDefaultZipSource`, covering
`AgentControl-System-MultipleSources`.

**AgentControl_NuGetSources_FolderFeedAndServer_AppliesPackageWithoutTouchingRepoNuGetConfig**: A repo is
given a package chosen from a real NuGet folder feed; the managed folders, release notes, and pin
are produced as for a zip package, and listing and acquiring packages never reads or writes a
`nuget.config` in the repo or the current directory. The published application is run against a
real NuGet folder feed and upgrades a pinned repo from it, and against an in-process NuGet server, to which it
signs in with a typed credential (plain `http` being refused until explicitly allowed for that source) and
from which it applies a package. This scenario is tested by
`windows@UpgradeMenuItem_Click_PinnedToNuGetFolderSource_UpgradesFromThatSourceOnly`,
`windows@SettingsWindow_AddNuGetServerWithSignIn_SelectPackagePinsServerSourceAndStoresNoPassword`,
`RepoCardViewModel_ApplySelectedPackage_NuGetFolderSource_ExtractsPinsAliasAndRaisesReleaseNotes`
and `NuGetSourceProvider_ListAndAcquire_NeverTouchesRepoNuGetConfig`, covering
`AgentControl-System-NuGetSources`.

**AgentControl_CredentialSafety_TypedPasswordIsNeverPersistedAndIsScopedToItsServer**: In the first end-to-end
scenario described under Verification Approach, a password typed in the sign-in dialog is used for the
connection test and again, without a second dialog, for Select Package, and is then absent from the settings
file, the repo's pin file and every log and text file in the configuration directory; the dialog showed the
server's origin and "Used for this session only - AgentControl does not save it." In the remember scenarios,
ticking "Remember on this computer" (which then explains that the user's Git credential helper keeps it) results in
the sign-in being handed to the emulated helper and still being found in nothing AgentControl wrote; a sign-in the
helper already holds connects without any sign-in dialog; "Forget saved sign-in" asks for confirmation naming the
server, leaves the sign-in alone when declined and removes it from the helper when confirmed, saying so; and with no
helper the remember option is shown disabled and "Forget saved sign-in" is unavailable. Everything that cannot be
observed end to end is verified at the subsystem and unit levels and covers the child requirements: that a credential
is never offered to another origin, nothing is written into the target repo's files, the exact and never-interactive
git requests and the refusal of values the protocol cannot carry safely, the removal of a rejected saved sign-in, and
the redaction of an exception that reaches the process-wide handlers. This scenario is tested by
`windows@SettingsWindow_AddNuGetServerWithSignIn_SelectPackagePinsServerSourceAndStoresNoPassword`,
`windows@SettingsWindow_SignInWithRememberTicked_SavedOnlyByTheUsersCredentialHelper`,
`windows@SettingsWindow_SavedSignIn_UsedWithoutDialogAndForgottenOnRequest` and
`windows@SettingsWindow_NoCredentialHelper_RememberAndForgetAreUnavailable`, covering
`AgentControl-System-CredentialSafety`. Residual risk, stated plainly: the tests prove what AgentControl
supplies; the NuGet client, once a server has authenticated, answers 401 challenges from other hosts that
the server's index advertises with the same credential, which is why users must only add servers they trust.

**AgentControl_PackageAuthenticity_SignatureRequirementRefusesUnsignedPackages**: A repo pinned to a NuGet folder
source that requires signed packages is offered the newer, unsigned package (listing does not look at signatures); the
Upgrade action is refused with a message that says the package is not validly signed and names the source, and nothing
is extracted and the pin is unchanged; the package chip also shows that the source requires signatures. Everything
that needs a real signed package or a different platform is verified at the subsystem and unit levels and covers the
child requirements: with a real signed package a valid signature is accepted with no pin and with the author's
certificate pinned, and refused when another publisher is pinned, with the user's own NuGet trusted-signer settings
unable to widen or narrow the pin; a package that was cached before the requirement was turned on is refused; and
on a platform that cannot verify signatures a source that requires them refuses to list or supply. This scenario is
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
`windows@UpgradeMenuItem_Click_PinnedToNuGetFolderSource_UpgradesFromThatSourceOnly`,
`windows@RepoCard_PinnedToUnconfiguredSource_ShowsSourceProblemAndOffersNoUpgrade`,
`PackageSourceRegistry_PinToSourceB_SamePackageIdAtBothSources_OnlyBIsContacted` and
`RepoCardViewModel_Refresh_PinNamesUnconfiguredSource_ShowsSourceProblemAndOffersNoUpgrade`,
covering `AgentControl-System-SourcePinIntegrity`.
