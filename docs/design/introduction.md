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
- **NuGetCaching**: integration and usage design.
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
the OTS items that ship inside the application (Avalonia, Serilog, and NuGetCaching) are modeled
as OTS parts in `docs/sysml2/model/ots.sysml`, while the remaining OTS items in Scope above are
build-time/pipeline tooling, documented separately (see `docs/design/ots.md`) without SysML2
parts. The system has eleven subsystems: `LauncherUI`, `AgentPackageManagement`,
`PackageCredentials` (credentials for package servers), `RepoSync`, `RepoConfig`, `AgentToolLauncher`,
`GitIntegration`, `Settings`, `Logging`, `Startup` and `Utilities`, plus the top-level `Program` unit. Of
the units, `GitCredentialClient` (in `GitIntegration`) and `SignatureVerificationSupport` (in
`AgentPackageManagement`) belong to the package-authenticity and saved-sign-in features, and
`PackageContentIdentity` (in `AgentPackageManagement`) to the package-content-integrity feature. `GitSourceProvider`,
`GitRepositoryAddress` and `GitTagPattern` (in `AgentPackageManagement`) and `GitRemoteClient` (in `GitIntegration`)
belong to the Git repository source feature. `RepoGroupName` (in
`Settings`) is the unit holding the rules for the names of the user's repo groups, and `RepoImageLocator`,
`RepoGroupHeaderViewModel`, `GroupNameViewModel` and `DialogBehavior` (in `LauncherUI`) belong to the repo-picture,
repo-group and dialog features; `PathEllipsisText` and `ThemePreferenceDisplayNameConverter` are documented
supporting types of `LauncherUI`, not units.

![Software Structure](SoftwareStructureView.svg)

## Folder Layout

- **src/** - source files and projects
  - **DemaConsulting.AgentControl/** - main Avalonia desktop application source
    - **LauncherUI/** - main window, repo cards, package-selection, settings, package-source editor
      (including the signature options and "Forget saved sign-in") and sign-in windows
    - **AgentPackageManagement/** - package source model, source providers (zip folder, NuGet
      folder and server, Git repository) and registry, package discovery, version comparison, the content identity of a
      delivered package, and the platform
      check for package-signature verification
    - **PackageCredentials/** - credentials for package servers: server origin, in-memory session
      store, per-operation credential callback, the contract of the user's own credential store,
      and log redaction
    - **RepoSync/** - package extraction (zip, unpacked NuGet package folder or a Git package's layout folder) and
      release-notes viewing
    - **RepoConfig/** - per-repo pin file persistence (package, version, source alias and content identity)
    - **AgentToolLauncher/** - shell detection and agent-tool process launching
    - **GitIntegration/** - git status/pull/branch queries, the user's Git credential helper
      (saved package-feed sign-ins) and hardened git against remote repositories (the Git package source)
    - **Settings/** - application-wide settings persistence (including the package sources and the user's repo
      groups, card colors and theme; `SettingsStore`, which repairs unusable stored values on load - an unknown card
      color reads as automatic) and the `RepoGroupName` unit, the rules for the names of the
      user's repo groups
    - **Logging/** - diagnostic file logging setup
    - **Startup/** - startup-argument parsing
    - **Utilities/** - shared utilities subsystem
  - **DemaConsulting.AgentControl.Msi/** - WiX Toolset MSI installer project
- **test/** - test projects
  - **DemaConsulting.AgentControl.Tests/** - unit, subsystem and headless-window tests, plus test support
    (an in-process NuGet V3 mock feed, a real signed-package fixture, stub and real git credential
    helpers, a stub git for the remote client and a real-repository fixture for the Git source,
    fake credential prompt and remembered-credential store, list logger)
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
