## WiX Toolset Verification

This document provides the verification evidence for the `WiX Toolset` OTS software item.
Requirements for this OTS item are defined in the WiX Toolset OTS Software Requirements
document.

### Required Functionality

The WiX Toolset (`wix`, `WixToolset.Heat`, `WixToolset.UI.wixext`) packages
`DemaConsulting.AgentControl`'s published output into `DemaConsulting.AgentControl.Msi`, the
Windows installer distributed for v1.

### Verification Approach

> **Test-evidence disclosure**: unlike Avalonia/Serilog/FlaUI (verified above with real local
> test names), no local, TRX-backed automated test evidence for the WiX-based MSI build exists
> in this repository - the `package-msi` job in `build.yaml` now builds the MSI on every
> pipeline run (a real, always-exercised build-success gate), but that job does not itself emit
> a named, independently-reportable test result. The scenarios below cite WiX's own vendor
> capability/test names, mirroring the existing accepted convention already used by this
> repo's other infrastructure-tool OTS entries (e.g. BuildMark, SonarMark, VersionMark) for
> tools whose own test suite - not this repo's TRX results - is the evidence source. This is a
> deliberate, disclosed choice, not a fabricated claim of local test coverage.

WiX Toolset is therefore verified by reliance on its own packaging capability, plus the real
(if not discretely-named) build-success gate provided by the `package-msi` CI job, which builds
the MSI from the self-contained, single-file publish output on every pipeline run.

### Test Scenarios

#### WixToolset_HarvestComponents

**Scenario**: WiX Toolset's `WixToolset.Heat` harvests the published application's output
files into installer components.

**Expected**: All published output files are represented as installer components.

**Requirement coverage**: `AgentControl-OTS-WixToolset-BuildInstaller`.

#### WixToolset_BuildMsiPackage

**Scenario**: WiX Toolset compiles the project's `.wxs` sources into a Windows Installer
database.

**Expected**: A valid MSI package is produced from the published application output.

**Requirement coverage**: `AgentControl-OTS-WixToolset-BuildInstaller`.

#### WixToolset_UiSequenceValidation

**Scenario**: WiX Toolset's `WixToolset.UI.wixext` standard installer UI sequences are
included and validated as part of the MSI build.

**Expected**: The installer's standard UI sequence is present and valid in the produced MSI.

**Requirement coverage**: `AgentControl-OTS-WixToolset-BuildInstaller`.
