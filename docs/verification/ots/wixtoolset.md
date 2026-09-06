## WiX Toolset Verification

This document provides the verification evidence for the `WiX Toolset` OTS software item.
Requirements for this OTS item are defined in the WiX Toolset OTS Software Requirements
document.

### Required Functionality

The WiX Toolset (`wix`, `WixToolset.Heat`, `WixToolset.UI.wixext`) packages
`DemaConsulting.AgentControl`'s published output into `DemaConsulting.AgentControl.Msi`, the
Windows installer distributed for v1.

### Verification Approach

The `package-msi` CI job builds the MSI from the self-contained, single-file publish output on
every pipeline run, then asserts (via FileAssert's `WixToolset_BuildMsiPackage` check) that a
non-trivial MSI file exists at the expected output path. This produces a real, TRX-backed test
result — not a fabricated claim of local unit-test coverage.

### Test Scenarios

#### WixToolset_BuildMsiPackage

**Scenario**: WiX Toolset compiles the project's `.wxs` sources (using `WixToolset.Heat` to
harvest the published application's output files into installer components, and
`WixToolset.UI.wixext` for standard installer UI sequences) into a Windows Installer database.

**Expected**: A valid, non-trivial MSI package is produced from the published application
output at `src/DemaConsulting.AgentControl.Msi/bin/x64/Release/*.msi`.

**Requirement coverage**: `AgentControl-OTS-WixToolset-BuildInstaller`.
