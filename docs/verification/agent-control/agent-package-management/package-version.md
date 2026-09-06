### PackageVersion

#### Verification Approach

`PackageVersion` is verified with unit tests defined in `PackageVersionTests.cs`. Every test
operates purely on in-memory version strings and constructor arguments; no filesystem or
external dependency is involved.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Parsing fails gracefully (returns false) for malformed or empty input rather than throwing.
- Construction rejects a negative major-version component.
- Comparison treats a release version as greater than a prerelease with the same numeric
  components.
- Equality and string round-tripping behave per conventional semantic-versioning expectations.

#### Test Scenarios

**PackageVersion_Parse_ParsesReleaseAndPrereleaseAndFailsGracefully**: A release version
string parses into its numeric components; a prerelease version string parses with its
prerelease identifier; a malformed input returns false rather than throwing; and constructing
with a negative major version throws `ArgumentOutOfRangeException`. This scenario is tested by
`PackageVersion_TryParse_ReleaseVersion_ParsesComponents`,
`PackageVersion_TryParse_PrereleaseVersion_ParsesPrereleaseIdentifier`,
`PackageVersion_TryParse_MalformedInput_ReturnsFalse`, and
`PackageVersion_Constructor_NegativeMajor_ThrowsArgumentOutOfRangeException`, covering
`AgentControl-PackageVersion-Parse`.

**PackageVersion_Compare_NumericOrderingReleasePrecedenceEqualityAndRoundTrip**: A version with
higher numeric components compares greater; a release version compares greater than a
prerelease with the same numeric components; two versions built from the same version string
are equal; and a parsed version round-trips through its string representation back through
`TryParse`. This scenario is tested by
`PackageVersion_CompareTo_HigherNumericVersion_ComparesGreater`,
`PackageVersion_CompareTo_ReleaseVsPrereleaseSameNumeric_ReleaseIsGreater`,
`PackageVersion_Equals_SameVersionString_ReturnsTrue`, and
`PackageVersion_ToString_ParsedVersion_RoundTripsThroughParse`, covering
`AgentControl-PackageVersion-Compare`.
