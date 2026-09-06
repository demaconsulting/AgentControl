## Program

![AgentControl Structure](AgentControlView.svg)

### Purpose

`Program` is the application entry point and bootstrap orchestrator. Its responsibility is to
parse the fixed set of startup arguments, initialize diagnostic logging as early as possible,
and configure Avalonia's classic desktop lifetime. Avalonia's `App` class
(`src/DemaConsulting.AgentControl/App.axaml.cs`) has no dedicated unit test file — it is
exercised only indirectly, via `Program_BuildAvaloniaApp_ReturnsConfiguredAppBuilder` and
end-to-end via the FlaUI UiTests project — so its bootstrap responsibility (loading settings
and showing the main window once the desktop lifetime initializes) is documented here rather
than as a separate unit, per program.sysml's documented exception for units without a
dedicated companion test file.

### Data Model

**Version**: `string` (static property, `Program`) — The application version read from
`AssemblyInformationalVersionAttribute` on every access, falling back to `AssemblyVersion`,
then `"0.0.0"`. No caching is applied; callers that need the value more than once should store
it locally. Consumed by the About dialog and diagnostic log entries
(`AgentControl-Program-Version`).

**Options**: `StartupOptions?` (static property, `App`) — The parsed startup options (config
directory override, git/agent-tool command test overrides), attached by `Program.Main` before
the desktop lifetime starts. `null` means "use all defaults". Attached via a static property
rather than a constructor parameter because Avalonia's `AppBuilder.Configure<App>()` requires
`App` to have a parameterless constructor so the framework's designer/previewer tooling can
also instantiate it. Not thread-safe by construction, but safe in practice because it is
written exactly once, before the framework starts, and read only from the UI thread.

### Key Methods

**Main**: Entry point for the application process.

- *Parameters*: `string[] args` — command-line arguments from the host environment.
- *Returns*: `int` — exit code; 0 for success, 1 for an expected startup-argument error.
- *Preconditions*: None.
- *Postconditions*: On success, the Avalonia classic desktop lifetime has run for the
  remainder of the process; on failure, the UI never starts.

Marked `[STAThread]` because Windows OLE clipboard operations (cut/copy/paste in any
`TextBox`) require the UI thread to run in a single-threaded apartment. Calls
`StartupOptions.Parse(args)`; an `ArgumentException` is caught, written to stderr as
`"Error: {message}"`, and causes a return of 1 without starting the UI
(`AgentControl-Program-ErrorHandling`). On success, calls `LoggingSetup.Initialize` with the
parsed configuration-directory override — before the Avalonia app builder runs, so
startup-time failures are also captured on disk — then sets `App.Options` and calls
`BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)` (`AgentControl-Program-Bootstrap`).
Any unhandled exception escaping the desktop lifetime is logged at `Critical` and wrapped in
an `InvalidOperationException` before being rethrown; a `finally` block always calls
`Log.CloseAndFlush()` so buffered log entries are not lost.

**BuildAvaloniaApp**: Configures the Avalonia `AppBuilder` used to run `App`.

- *Parameters*: None.
- *Returns*: `AppBuilder` — a configured, not-yet-started builder (`UsePlatformDetect`,
  `LogToTrace`).
- *Preconditions*: None.
- *Postconditions*: None (pure configuration; does not start the application).

Kept as a separate, parameterless static method — rather than inlined into `Main` — following
Avalonia's standard template convention: this allows the same builder configuration to be
reused by design-time tooling and out-of-process previewers, neither of which call `Main`
directly.

**App.OnFrameworkInitializationCompleted**: Loads settings and shows the main window.

- *Parameters*: None (Avalonia lifecycle callback).
- *Returns*: `void`.
- *Preconditions*: `ApplicationLifetime` is `IClassicDesktopStyleApplicationLifetime` (the
  only lifetime supported for v1, per architecture.md's Windows MSI-first distribution
  decision; other lifetimes such as browser are not wired up).
- *Postconditions*: `desktop.MainWindow` is set to a `MainWindow` whose `DataContext` is a
  fully constructed `MainWindowViewModel`.

Calls `SettingsStore.Load(Options?.ConfigDirectory)` so the launcher window opens with the
user's recent repos and preferences already populated, constructs a `MainWindowViewModel`
from the loaded settings and `Options`, and assigns the new `MainWindow` as the desktop
lifetime's main window.

### Error Handling

`Main` treats `ArgumentException` from `StartupOptions.Parse` as an expected error: its
message is written to stderr and 1 is returned without a stack trace or starting the UI. Any
other unhandled exception that escapes the desktop lifetime is logged at `Critical` via
`AppLogging.Factory` before being wrapped in an `InvalidOperationException` and rethrown —
wrapped rather than a bare rethrow so the exception carries explicit contextual information
about where it was caught. The `finally` block's `Log.CloseAndFlush()` call runs regardless
of how `Main` exits, guaranteeing buffered diagnostic log entries are always written.
`App.OnFrameworkInitializationCompleted` does not itself catch exceptions; any failure during
settings load or window construction propagates up through the Avalonia lifetime to `Main`'s
handler.

### Dependencies

- **StartupOptions** (`Startup` subsystem) — `Program.Main` calls `StartupOptions.Parse` to
  obtain the configuration-directory and test-only command overrides.
- **LoggingSetup**, **AppLogging** (`Logging` subsystem) — `Program.Main` calls
  `LoggingSetup.Initialize` before the Avalonia app builder runs, and uses
  `AppLogging.Factory` to log startup information and unhandled exceptions.
- **SettingsStore** (`Settings` subsystem) — `App.OnFrameworkInitializationCompleted` calls
  `SettingsStore.Load` to populate the launcher window's initial state.
- **MainWindowViewModel**, **MainWindow** (`LauncherUI` subsystem) — `App` constructs the
  view-model from loaded settings and options, and assigns the window as the desktop
  lifetime's main window.
- **Serilog** (OTS) — `Program.Main`'s `finally` block calls `Log.CloseAndFlush()` to flush
  the diagnostic log pipeline configured by `LoggingSetup`. See `docs/design/ots/serilog.md`.
- **Avalonia** (OTS) — `BuildAvaloniaApp` configures the `AppBuilder`, and `App` derives from
  `Avalonia.Application`. See `docs/design/ots/avalonia.md`.

### Callers

N/A - entry point, called by the host environment.
