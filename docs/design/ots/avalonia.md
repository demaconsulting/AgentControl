## Avalonia

This document describes the integration and usage design for the `Avalonia` OTS software item.

### Purpose

Avalonia is the cross-platform UI framework AgentControl's `LauncherUI` subsystem is built
on: the main window, repo cards, package-selection window, settings window, about dialog, and
release-notes viewer are all Avalonia windows/controls. It is chosen for its classic desktop
lifetime (showing the main window on startup) and its UI Automation identifier support, which
lets FlaUI-driven end-to-end tests reliably locate UI elements
(`AgentControl-OTS-Avalonia-RenderUi`). Unlike the project's build/CI/quality tooling, Avalonia
ships as part of the AgentControl application itself, so it also has a corresponding SysML2
part (`docs/sysml2/model/ots.sysml`).

### Features Used

- Classic desktop application lifetime (`ClassicDesktopStyleApplicationLifetime`), showing the
  main window on startup.
- XAML-based window/control declarations (`.axaml`) with MVVM data binding.
- `AutomationId` support, enabling FlaUI to locate UI elements by automation identifier.

### Integration Pattern

Avalonia is consumed as a set of NuGet package references in the main project. `Program.Main`
builds and starts the Avalonia application (`BuildAvaloniaApp`), and `App.axaml.cs`'s
`OnFrameworkInitializationCompleted` creates and shows `MainWindow` when running with a classic
desktop lifetime. Avalonia does not provide dotnet self-validation; its output is validated by a
passing unit test that the application builder is configured for the `App` class
(`Program_BuildAvaloniaApp_ReturnsConfiguredAppBuilder`) and a passing FlaUI end-to-end smoke
test that finds the main window by its expected automation identifier
(`windows@MainWindow_Launch_WindowVisibleWithExpectedAutomationId`), together demonstrating
Avalonia renders the application correctly.
