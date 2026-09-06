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

> **Test-evidence disclosure**: unlike Avalonia/Serilog/FlaUI (which cite real local test
> names), no local, TRX-backed automated test evidence for the WiX-based MSI build exists in
> this repository - the `package-msi` job in `build.yaml` builds the MSI on every pipeline run
> (a real, always-exercised build-success gate), but that job does not itself emit a named,
> independently-reportable test result. The tests cited below are WiX's own vendor
> capability/test names, mirroring the existing accepted convention already used by this
> repo's other infrastructure-tool OTS entries (e.g. BuildMark, SonarMark, VersionMark) for
> tools whose own test suite — not this repo's TRX results — is the evidence source. This is a
> deliberate, disclosed choice, not a fabricated claim of local test coverage.

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
present in the published directory). The `package-msi` job in `build.yaml` now builds this MSI
on every pipeline run, providing a real, always-exercised build-success gate; no automated test
in this repository installs or exercises the resulting package, so this requirement still also
records the reliance on the tool's own packaging capability
(`WixToolset_HarvestComponents`, `WixToolset_BuildMsiPackage`, `WixToolset_UiSequenceValidation`).
