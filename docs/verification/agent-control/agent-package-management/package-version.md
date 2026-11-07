### PackageVersion

#### Verification Approach

`PackageVersion` is verified with unit tests defined in `PackageVersionTests.cs`. Every test
operates purely on in-memory version strings and constructor arguments; no filesystem or
external dependency is involved.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A fourth numeric segment and build metadata parse, with metadata ignored by comparison and
  equality; the strict zip-name parse still rejects both.
- Ordering agrees with NuGet.Versioning for a spread of three-part, four-part, prerelease and
  metadata versions.
- Parsing fails gracefully (returns false) for malformed or empty input rather than throwing.
- Construction rejects a negative major-version component.
- Comparison treats a release version as greater than a prerelease with the same numeric
  components, and orders prerelease identifiers per semver (numerically for numeric
  identifiers, so `beta.2` precedes `beta.10`).
- Prerelease strings with empty or invalid identifiers, or numeric leading zeros, fail to parse.
- Equality and string round-tripping behave per conventional semantic-versioning expectations.

#### Test Scenarios

**PackageVersion_Parse_ParsesReleaseAndPrereleaseAndFailsGracefully**: A release version
string parses into its numeric components; a prerelease version string parses with its
prerelease identifier; a malformed input returns false rather than throwing; and constructing
with a negative major version throws `ArgumentOutOfRangeException`. This scenario is tested by
`PackageVersion_TryParse_ReleaseVersion_ParsesComponents`,
`PackageVersion_TryParse_PrereleaseVersion_ParsesPrereleaseIdentifier`,
`PackageVersion_TryParse_MalformedInput_ReturnsFalse`, and
`PackageVersion_Constructor_NegativeMajor_ThrowsArgumentOutOfRangeException`, and
`PackageVersion_TryParse_InvalidPrereleaseIdentifiers_ReturnsFalse`, covering
`AgentControl-PackageVersion-Parse`.

**PackageVersion_Compare_NumericOrderingReleasePrecedenceEqualityAndRoundTrip**: A version with
higher numeric components compares greater; a release version compares greater than a
prerelease with the same numeric components; two versions built from the same version string
are equal; and a parsed version round-trips through its string representation back through
`TryParse`. This scenario is tested by
`PackageVersion_CompareTo_HigherNumericVersion_ComparesGreater`,
`PackageVersion_CompareTo_ReleaseVsPrereleaseSameNumeric_ReleaseIsGreater`,
`PackageVersion_CompareTo_PrereleaseIdentifiers_FollowSemverPrecedence`,
`PackageVersion_CompareTo_HugeNumericIdentifiers_ComparesNumerically`,
`PackageVersion_Equals_SameVersionString_ReturnsTrue`, and
`PackageVersion_ToString_ParsedVersion_RoundTripsThroughParse`, covering
`AgentControl-PackageVersion-Compare`.

**PackageVersion_ExtendedVersions_FourPartAndMetadataParseAndAgreeWithNuGet**: A four-part
version parses with its revision and orders after its three-part base; a zero revision equals
the three-part form with the same hash code while keeping its own original text; prerelease,
revision and metadata combine; the strict parse (used for zip file names) still rejects the
fourth segment and metadata while accepting the plain forms; and, for every ordered pair in a
spread of versions, comparison and equality agree in sign with `NuGet.Versioning.NuGetVersion`.
This scenario is tested by
`PackageVersion_TryParse_FourPartVersion_ParsesRevisionAndOrdersAfterThreePart`,
`PackageVersion_Equals_ZeroRevisionAndThreePart_AreEqualWithSameHashCode`,
`PackageVersion_TryParse_PrereleaseRevisionAndMetadata_AllParsed`,
`PackageVersion_TryParseStrict_ExtendedOrMalformedForms_ReturnsFalse`,
`PackageVersion_TryParseStrict_ThreePartForms_ReturnsTrue`, and
`PackageVersion_CompareTo_VersionsAgreeWithNuGetVersionOrdering`, covering
`AgentControl-PackageVersion-ExtendedVersions` (and, for the strict parse,
`AgentControl-PackageVersion-Parse`).

**PackageVersion_MetadataIgnoredInComparison_PreservedInTextOnly**: Two versions that differ only
in build metadata compare equal with the same hash code, while the metadata and the original text
are preserved. This scenario is tested by
`PackageVersion_TryParse_BuildMetadata_PreservedButIgnoredInComparison`, covering
`AgentControl-PackageVersion-MetadataIgnoredInComparison`.
