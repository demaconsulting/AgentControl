## Avalonia Verification

This document provides the verification evidence for the `Avalonia` OTS software item.
Requirements for this OTS item are defined in the Avalonia OTS Software Requirements document.

### Required Functionality

Avalonia 12 is the cross-platform UI framework AgentControl's `LauncherUI` subsystem is built
on (main window, repo cards, package-selection window, settings window, about dialog,
release-notes viewer). Its classic desktop lifetime shows the main window on startup, and its
`AutomationId` support lets FlaUI-driven end-to-end tests reliably locate UI elements.

### Verification Approach

Avalonia does not provide dotnet self-validation, so it is verified by two complementary
layers of evidence. First, a passing unit test confirms Avalonia's application builder is
configured for the `App` class before any window is ever shown
(`Program_BuildAvaloniaApp_ReturnsConfiguredAppBuilder`). Second, a passing FlaUI end-to-end
smoke test finds the main window by its expected automation identifier after launching the
real published application
(`windows@MainWindow_Launch_WindowVisibleWithExpectedAutomationId`), demonstrating Avalonia
actually renders the application and exposes automation identifiers correctly. Together these
two layers demonstrate Avalonia is rendering the application correctly end-to-end.

### Test Scenarios

#### Program_BuildAvaloniaApp_ReturnsConfiguredAppBuilder

**Scenario**: xUnit discovers and runs this test; the test verifies that `Program`'s
`BuildAvaloniaApp` method returns an `AppBuilder` configured for the `App` class.

**Expected**: xUnit executes the test, the test passes, and the result appears in the TRX
output.

**Requirement coverage**: `AgentControl-OTS-Avalonia-RenderUi`.

#### windows@MainWindow_Launch_WindowVisibleWithExpectedAutomationId

**Scenario**: FlaUI launches the published application and locates the main window by its
expected automation identifier.

**Expected**: The main window is visible and is found by FlaUI via its expected
`AutomationId`.

**Requirement coverage**: `AgentControl-OTS-Avalonia-RenderUi`.
