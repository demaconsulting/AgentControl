## WiX Toolset

This document describes the integration and usage design for the `WiX Toolset` OTS software
item.

### Purpose

The WiX Toolset (`wix`, `WixToolset.Heat`, `WixToolset.UI.wixext`) packages
`DemaConsulting.AgentControl`'s published output into `DemaConsulting.AgentControl.Msi`, the
Windows installer distributed for v1 (per architecture.md's Technology Stack and Scope)
(`AgentControl-OTS-WixToolset-BuildInstaller`). Per the project's established convention, WiX
Toolset receives OTS requirements only (no SysML2 part), matching the asymmetric
requirements-only precedent already established for other tools in this repo.

> **Test-evidence disclosure**: the `package-msi` job in `build.yaml` builds the MSI on every
> pipeline run and then runs FileAssert's `WixToolset_BuildMsiPackage` check, which asserts
> that a non-trivial MSI file exists at the expected output path. This produces a real,
> TRX-backed test result cited by `AgentControl-OTS-WixToolset-BuildInstaller` — not a
> fabricated claim of local unit-test coverage. No automated test in this repository installs
> or exercises the resulting package.

### Features Used

- WiX source compilation (`.wxs`) into a Windows Installer database.
- `WixToolset.Heat` harvesting of the published application's output files into installer
  components.
- `WixToolset.UI.wixext` standard installer UI sequences.

### Integration Pattern

WiX Toolset is consumed via the `DemaConsulting.AgentControl.Msi` project's NuGet package
references and MSBuild targets, building an MSI from `DemaConsulting.AgentControl`'s published
output — now a self-contained, single-file publish output rather than a framework-dependent
one (no `HarvestDirectory` change was required, since it harvests whatever is physically
present in the published directory). The `package-msi` job in `build.yaml` builds this MSI
on every pipeline run, then asserts (via FileAssert's `WixToolset_BuildMsiPackage` check) that
a non-trivial MSI file exists at the expected output path — a real, TRX-backed evidence
source. No automated test in this repository installs or exercises the resulting package.
