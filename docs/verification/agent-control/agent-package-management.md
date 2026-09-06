## AgentPackageManagement

### Verification Approach

The `AgentPackageManagement` subsystem is verified indirectly through its constituent units'
test suites (`PackageSourceTests.cs`, `PackageVersionTests.cs`, and
`PackageVersionCacheTests.cs`), each of which is documented in its own unit-level verification
design. All tests use real temporary directories containing real zip files, so package
discovery, version parsing, and caching are exercised against genuine filesystem state rather
than mocked collaborators.

### Test Environment

N/A - standard test environment; no external services or hardware required.

### Acceptance Criteria

- All unit tests for `PackageSource`, `PackageVersion`, and `PackageVersionCache` pass with
  zero failures.
- Package discovery reflects only what is physically present in a source folder.
- Upgrade detection produces a definite true/false answer for every input, including an
  unparsable pinned version or an empty source folder.

### Test Scenarios

**AgentPackageManagement_DiscoverPackages_UnitTestsCoverNameVersionAndEnumeration**: Package
name/version discovery and enumeration are verified by the `PackageSource` unit tests
(see the `PackageSource` unit verification design), with
`PackageSource_EnumeratePackageNames_MixedDirectory_ReturnsDistinctSortedNames` cited directly
at the subsystem level, covering `AgentControl-AgentPackageManagement-DiscoverPackages`
(children: `AgentControl-PackageSource-EnumeratePackages`,
`AgentControl-PackageSource-DiscoverNames`, `AgentControl-PackageSource-FindLatest`,
`AgentControl-PackageVersion-Parse`, `AgentControl-PackageVersionCache-CachePackageNames`,
`AgentControl-PackageVersionCache-CacheVersions`,
`AgentControl-PackageVersionCache-Invalidate`).

**AgentPackageManagement_UpgradeDetection_UnitTestsCoverNewerVersionAndUnparsablePin**:
Upgrade detection is verified by the `PackageSource`, `PackageVersion`, and
`PackageVersionCache` unit tests (see their respective unit verification designs), with
`PackageSource_IsNewerVersionAvailable_SourceHasNewerVersion_ReturnsTrue` and
`PackageSource_IsNewerVersionAvailable_UnparsablePin_ReturnsTrue` cited directly at the
subsystem level, covering `AgentControl-AgentPackageManagement-UpgradeDetection` (children:
`AgentControl-PackageSource-DetectNewerVersion`, `AgentControl-PackageVersion-Compare`,
`AgentControl-PackageVersionCache-CacheNewerVersion`).
