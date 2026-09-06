# Introduction

This document provides the detailed design for the Agent Control, a .NET desktop launcher
application that lets a company distribute proprietary AI-agent configuration alongside
public source repositories without committing that content to source control. It covers
local software items (systems, subsystems, and units) and the OTS software items they
consume.

## Purpose

The purpose of this document is to describe the internal design of each software unit that
comprises the Agent Control. It captures data models, algorithms, key methods, and
inter-unit interactions at a level of detail sufficient for formal code review, compliance
verification, and future maintenance. A reviewer should be able to understand how each item
satisfies its requirements without reading source code. The document does not restate
requirements; it explains how they are realized.

## Scope

Local items:

- **AgentControl**: system, subsystem, and unit design for all local components.

OTS items:

- **Avalonia**: integration and usage design.
- **BuildMark**: integration and usage design.
- **FileAssert**: integration and usage design.
- **FlaUI**: integration and usage design.
- **Pandoc**: integration and usage design.
- **ReqStream**: integration and usage design.
- **ReviewMark**: integration and usage design.
- **SarifMark**: integration and usage design.
- **Serilog**: integration and usage design.
- **SonarMark**: integration and usage design.
- **SysML2Tools**: integration and usage design.
- **VersionMark**: integration and usage design.
- **WeasyPrint**: integration and usage design.
- **WiX Toolset**: integration and usage design.
- **xUnit**: integration and usage design.

The following topics are out of scope:

- Design documents are not produced for the test projects or build pipeline CI configuration
- The internal design of OTS software items is excluded; only integration and usage design is documented

## Software Structure

The software structure is modeled in SysML2 under `docs/sysml2/` and rendered to the diagram
below by SysML2Tools as part of the build pipeline. AI agents should query the SysML2 model
directly (see the `sysml2tools-query` skill) rather than parsing this diagram or the prose
elsewhere in this document. The model captures the shipped system's runtime composition only;
the OTS items listed in Scope above are build-time/pipeline tooling, documented separately
(see `docs/design/ots.md`) rather than modeled as SysML2 parts.

![Software Structure](SoftwareStructureView.svg)

## Folder Layout

- **src/** - source files and projects
  - **DemaConsulting.AgentControl/** - main Avalonia desktop application source
    - **LauncherUI/** - main window, repo cards, package-selection and settings windows
    - **AgentPackageManagement/** - package discovery and version comparison
    - **RepoSync/** - package zip extraction and release-notes viewing
    - **RepoConfig/** - per-repo pin file persistence
    - **AgentToolLauncher/** - shell detection and agent-tool process launching
    - **GitIntegration/** - git status/pull/branch queries
    - **Settings/** - application-wide settings persistence
    - **Logging/** - diagnostic file logging setup
    - **Startup/** - startup-argument parsing
    - **Utilities/** - shared utilities subsystem
  - **DemaConsulting.AgentControl.Msi/** - WiX Toolset MSI installer project
- **test/** - test projects
  - **DemaConsulting.AgentControl.UiTests/** - FlaUI end-to-end UI test project

## Document Conventions

Throughout this document:

- Class names, method names, property names, and file names appear in `monospace` font.
- The word **shall** denotes a design constraint that the implementation must satisfy.
- Section headings within each unit chapter follow a consistent structure: overview, data model,
  methods/algorithms, and interactions with other units.
- Text tables are used in preference to diagrams, which may not render in all PDF viewers.

## Companion Artifact Structure

Local software items have corresponding artifacts in parallel directory trees:

- Requirements: `docs/reqstream/{system}/.../{item}.yaml` (kebab-case)
- Design docs: `docs/design/{system}/.../{item}.md` (kebab-case)
- Verification design: `docs/verification/{system}/.../{item}.md` (kebab-case)
- Source code: `src/{System}/.../{Item}.cs` (PascalCase for C#)
- Tests: `test/{System}.Tests/.../{Item}Tests.cs` (PascalCase for C#)

OTS items have integration/usage design documentation parallel to system folders:

- Requirements: `docs/reqstream/ots/{ots-name}.yaml`
- Design: `docs/design/ots/{ots-name}.md`
- Verification: `docs/verification/ots/{ots-name}.md`

Review-sets: defined in `.reviewmark.yaml`

## References

- Agent Control User Guide
- Agent Control Repository (<https://github.com/demaconsulting/AgentControl>)
