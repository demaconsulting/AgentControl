## FlaUI

This document describes the integration and usage design for the `FlaUI` OTS software item.

### Purpose

FlaUI.Core and FlaUI.UIA3 back the `DemaConsulting.AgentControl.UiTests` project's end-to-end
tests, which launch the published application and interact with its windows/controls exactly
as a developer would (`AgentControl-OTS-FlaUI-DriveUiTests`). Per the project's established
convention, FlaUI receives OTS requirements only (no SysML2 part), matching the asymmetric
requirements-only precedent already used for other test/build tools in this repo (e.g. xUnit,
Pandoc) that have OTS requirements without a corresponding `docs/sysml2/model` entry.

### Features Used

- Windows UI Automation (UIA3) element location by `AutomationId`.
- Command invocation (button clicks, menu item selection, text entry) against a real running
  Avalonia window.
- Control state reading (visibility, text content) for test assertions.

### Integration Pattern

FlaUI is consumed as a set of NuGet package references in the
`DemaConsulting.AgentControl.UiTests` project. Each test launches the published AgentControl
executable (using `StartupOptions`' config-directory and git/agent-tool command overrides for
isolation), attaches a FlaUI `UIA3Automation` session, and drives the window exactly as a user
would. FlaUI does not provide dotnet self-validation; passing FlaUI-driven tests are the
integration evidence, for example
`windows@MainWindow_Launch_WindowVisibleWithExpectedAutomationId`,
`windows@LaunchButton_Click_InvokesConfiguredAgentToolInRepoWorkingDirectory`, and
`windows@SelectPackageMenuItem_Click_AppliesChosenPackageAndSyncsManagedFolders`.
