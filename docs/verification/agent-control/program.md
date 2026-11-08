## Program

### Verification Approach

`Program` is verified with unit tests defined in `ProgramTests.cs`. `Program.Main` is only
exercised for the argument-parsing error path: a successful parse hands off to Avalonia's
`StartWithClassicDesktopLifetime`, which blocks for the lifetime of the application and
requires a real UI backend, so it is never invoked from a headless unit test.
`Program.BuildAvaloniaApp` is exercised directly instead, since configuring (but not starting)
an `AppBuilder` is safe to do headlessly. No mocking is required: `Program` calls real
`StartupOptions.Parse` and `Avalonia.AppBuilder` configuration APIs directly. Avalonia's `App`
class bootstrap responsibility (loading settings and showing the main window) is documented
alongside `Program` per this unit's design (`docs/design/agent-control/program.md`) and is
exercised only indirectly here, via `Program_BuildAvaloniaApp_ReturnsConfiguredAppBuilder`, and
end-to-end via the FlaUI `DemaConsulting.AgentControl.UiTests` project (see the system-level
verification design's `AgentControl_Startup_MainWindowLaunch_LoadsSettingsAndShowsRecentRepos`
scenario).

The saved theme being applied at start (`AgentControl-Program-StartupTheme`) is part of that bootstrap
responsibility and has no unit test: it is verified end to end only, by the FlaUI tests
`MainWindow_LaunchWithPersistedLightTheme_AppliesLightFromStartup` and
`MainWindow_LaunchWithPersistedDarkTheme_AppliesDarkFromStartup`, which seed the theme in an isolated
configuration directory and measure the first window.

### Test Environment

N/A - standard test environment.

### Acceptance Criteria

- All unit tests pass with zero failures.
- `Program.Version` returns a non-empty, non-null string.
- `Program.BuildAvaloniaApp` returns a non-null, configured `AppBuilder` targeting the `App`
  class, without starting any UI lifetime.
- `Program.Main` returns a non-zero exit code and writes an error message to stderr when given
  an unsupported startup argument, without attempting to start the Avalonia UI.
- A theme saved by an earlier session is in effect when the first window is shown (verified end to end).
- An exception escaping startup is logged at `Critical` and passed on only as a sanitized
  stand-in: an authorization header value, a URL's user information and query string never
  appear in the log entry or in the exception handed on.

### Test Scenarios

**Program_Version_ReturnsNonEmptyString**: The `Program.Version` static property is read; the
returned string is non-empty and non-null, confirming the version is resolvable from the
assembly attributes. This scenario is tested by `Program_Version_ReturnsNonEmptyString`,
covering `AgentControl-Program-Version`.

**Program_BuildAvaloniaApp_ReturnsConfiguredAppBuilder**: `Program.BuildAvaloniaApp` is called;
a non-null `AppBuilder` is returned whose `ApplicationType` is `App`, confirming Avalonia's
classic desktop lifetime is configured for settings to be loaded and the main window shown once
the framework starts. This scenario is tested by
`Program_BuildAvaloniaApp_ReturnsConfiguredAppBuilder`, covering
`AgentControl-Program-Bootstrap`.

**Program_StartupTheme_SavedThemeIsInEffectBeforeTheFirstWindow**: The application is launched end to end with Light,
and in another run Dark, already saved in the settings file of an isolated configuration directory; the main window's
app bar has the theme's brightness before anything is changed, the Settings window offers the saved theme as the
current choice, and the start did not overwrite the saved choice. This scenario is tested by
`windows@MainWindow_LaunchWithPersistedLightTheme_AppliesLightFromStartup` and
`windows@MainWindow_LaunchWithPersistedDarkTheme_AppliesDarkFromStartup`, covering
`AgentControl-Program-StartupTheme`.

**Program_Main_WithInvalidArgument_ReturnsNonZeroExitCode**: `Program.Main` is invoked with
`["--not-a-real-option"]` while stderr is redirected; the exit code is 1 and the captured
stderr output contains "Error", confirming an invalid startup argument is reported without
starting the UI. This scenario is tested by
`Program_Main_WithInvalidArgument_ReturnsNonZeroExitCode`, covering
`AgentControl-Program-ErrorHandling`.

**Program_ReportStartupFailure_ExceptionCarryingSecrets_LogsAndRethrowsOnlySanitizedDescription**:
`Program.ReportStartupFailure` is given an exception chain whose texts carry an `Authorization`
bearer value and a URL with user information and a query token, and a capturing logger; the single
`Critical` entry names the failure and the exception type but contains none of the secrets, and the
exception returned for rethrow wraps a `SanitizedException` that also contains none of them. This
scenario is tested by
`Program_ReportStartupFailure_ExceptionCarryingSecrets_LogsAndRethrowsOnlySanitizedDescription`,
covering `AgentControl-Program-StartupFailureRedacted`.
