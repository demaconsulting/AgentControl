# Introduction

This document provides the verification design for the Agent Control, a .NET desktop launcher
application that lets a company distribute proprietary AI-agent configuration alongside public
source repositories without committing that content to source control.

## Purpose

The purpose of this document is to describe how each requirement for the Agent Control is
verified. For every software item — system, subsystem, and unit — this document names the
verification approach, identifies the test scenarios (including boundary conditions and error
paths), describes what is mocked or stubbed, and maps each requirement to at least one named
test scenario. The document does not restate design; it explains how the design is proven correct.

## Scope

This document covers the verification design for the following software items:

**Local items:**

- **AgentControl** — system-level verification:
  - **Program** — entry point and bootstrap orchestrator (also documents Avalonia's `App`
    bootstrap responsibility, which has no dedicated unit test file)
  - **LauncherUI** subsystem
    - **MainWindowViewModel**, **RepoCardViewModel**, **SelectPackageWindowViewModel**,
      **SettingsWindowViewModel**
  - **AgentPackageManagement** subsystem
    - **PackageSource**, **PackageVersion**, **PackageVersionCache**
  - **RepoSync** subsystem
    - **PackageZipExtractor**, **ReleaseNotesViewerViewModel**
  - **RepoConfig** subsystem
    - **RepoPinStore**
  - **AgentToolLauncher** subsystem
    - **ShellDetector**, **AgentToolLauncher**
  - **GitIntegration** subsystem
    - **GitClient**, **CommittedAgentFilesCache**
  - **Settings** subsystem
    - **SettingsStore**
  - **Logging** subsystem (no dedicated unit — indirect-only test coverage, disclosed below)
  - **Startup** subsystem
    - **StartupOptions**
  - **Utilities** subsystem
    - **PathHelpers** — safe path combination utilities

**OTS items:**

- **Avalonia** — cross-platform UI framework
- **BuildMark** — build-notes documentation tool
- **FileAssert** — document assertion tool
- **FlaUI** — Windows UI Automation testing library
- **Pandoc** — Markdown-to-HTML conversion tool
- **ReqStream** — requirements traceability tool
- **ReviewMark** — file review enforcement tool
- **SarifMark** — SARIF report conversion tool
- **Serilog** — structured-logging library
- **SonarMark** — SonarCloud quality report tool
- **SysML2Tools** — architecture model validation and diagram rendering tool
- **VersionMark** — tool-version documentation tool
- **WeasyPrint** — HTML-to-PDF conversion tool
- **WiX Toolset** — Windows MSI installer packaging tool
- **xUnit** — unit-testing framework

The following topics are out of scope:

- Verification documents are not produced for the test projects themselves — they are the
  means of verification, not subjects of it
- Build pipeline CI configuration is excluded
- The internal implementation of OTS software items is excluded; only integration and usage
  are verified

## Folder Layout

The test folder structure mirrors the source subsystem breakdown:

```text
test/
├── DemaConsulting.AgentControl.Tests/    — unit and integration tests (xUnit v3)
│   ├── LauncherUI/
│   ├── AgentPackageManagement/
│   ├── RepoSync/
│   ├── RepoConfig/
│   ├── AgentToolLauncher/
│   ├── GitIntegration/
│   ├── Settings/
│   ├── Startup/
│   └── Utilities/
└── DemaConsulting.AgentControl.UiTests/  — FlaUI end-to-end Windows UI Automation tests
```

## Companion Artifact Structure

In-house items have corresponding artifacts in parallel directory trees:

- Requirements: `docs/reqstream/{system}/.../{item}.yaml` (kebab-case)
- Design docs: `docs/design/{system}/.../{item}.md` (kebab-case)
- Verification design: `docs/verification/{system}/.../{item}.md` (kebab-case)
- Source code: `src/{System}/.../{Item}.cs` (PascalCase for C#)
- Tests: `test/{System}.Tests/.../{Item}Tests.cs` (PascalCase for C#)

OTS items have parallel artifacts in:

- Requirements: `docs/reqstream/ots/{ots-name}.yaml` (kebab-case)
- Verification: `docs/verification/ots/{ots-name}.md` (kebab-case)

Review-sets: defined in `.reviewmark.yaml`

## References

- Agent Control Software Design Document
- Agent Control releases (<https://github.com/demaconsulting/AgentControl/releases>)
