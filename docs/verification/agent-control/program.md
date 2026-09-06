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

### Test Environment

N/A - standard test environment.

### Acceptance Criteria

- All unit tests pass with zero failures.
- `Program.Version` returns a non-empty, non-null string.
- `Program.BuildAvaloniaApp` returns a non-null, configured `AppBuilder` targeting the `App`
  class, without starting any UI lifetime.
- `Program.Main` returns a non-zero exit code and writes an error message to stderr when given
  an unsupported startup argument, without attempting to start the Avalonia UI.

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

**Program_Main_WithInvalidArgument_ReturnsNonZeroExitCode**: `Program.Main` is invoked with
`["--not-a-real-option"]` while stderr is redirected; the exit code is 1 and the captured
stderr output contains "Error", confirming an invalid startup argument is reported without
starting the UI. This scenario is tested by
`Program_Main_WithInvalidArgument_ReturnsNonZeroExitCode`, covering
`AgentControl-Program-ErrorHandling`.
