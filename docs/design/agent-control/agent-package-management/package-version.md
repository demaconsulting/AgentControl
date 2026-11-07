### PackageVersion

![AgentPackageManagement Structure](AgentPackageManagementView.svg)

#### Purpose

`PackageVersion` parses a version string into comparable components and implements ordering so
releases and prereleases compare correctly against each other. It implements Semantic
Versioning 2.0.0 (numeric major/minor/patch, an optional dot-separated prerelease identifier
list, and optional build metadata after `+`) plus the one extension NuGet versions need: an
optional fourth numeric segment (the revision). Zip package file names only ever carry the
strict three-part form, so a second, strict parse entry point keeps their name/version
splitting exactly as it always was.

#### Data Model

**Major**, **Minor**, **Patch**: `int` — non-negative numeric version components.

**Revision**: `int` — the optional fourth numeric segment, zero when absent. It ranks after the
patch, so `1.0.0.0` and `1.0.0` are the same version and `1.0.0.1` follows `1.0.0`.

**Prerelease**: `string?` — the text after the first `-`, or `null` for a release version.

**Metadata**: `string?` — the build metadata after `+`, or `null`. It is kept but takes no part
in comparison, equality, or hashing, per semver precedence rules.

**Original** / `ToString`: `string` — the exact text the version was parsed from (including any
metadata), or the canonical rendering when the version was built from components. A repo pin
stores this text so the spelling a source reported survives a round trip.

The class is immutable and thread-safe once constructed; all properties are read-only.

#### Key Methods

**Constructor**: Builds a version from explicit components.

- *Parameters*: `int major`, `int minor`, `int patch`, `string? prerelease = null`,
  `int revision = 0`, `string? metadata = null`.
- *Preconditions*: All numeric components non-negative.
- *Postconditions*: An empty `prerelease` or `metadata` string is normalized to `null`; the
  original text is the canonical rendering (`AgentControl-PackageVersion-Parse`).

**TryParse**: Attempts to parse a version string using the extended, NuGet-compatible grammar
(three or four numeric segments, optional prerelease, optional `+metadata`).

- *Parameters*: `string? value`, `out PackageVersion? version`.
- *Returns*: `bool`.
- *Postconditions*: On success, `version` holds the parsed components and the original text.
  Does not throw on malformed input (`AgentControl-PackageVersion-Parse`,
  `AgentControl-PackageVersion-ExtendedVersions`). Rejects a numeric part that is not three or
  four dot-separated non-negative integers, an empty prerelease identifier (a trailing `-` with
  nothing after it) or a prerelease that is not valid semver (empty identifiers, characters
  other than ASCII letters, digits, and hyphens, or numeric identifiers with leading zeros), and
  metadata that is empty or has an empty or invalid identifier.

**TryParseStrict**: Attempts to parse the narrower, strict grammar used for zip file names:
exactly three numeric segments, an optional prerelease, and no metadata.

- *Parameters* and *Returns*: as `TryParse`.
- *Postconditions*: Rejects everything `TryParse` rejects, plus a fourth segment and any
  `+metadata`, so which zip file names are recognized as packages does not change
  (`AgentControl-PackageVersion-Parse`).

**CompareTo**: Compares this version to another following semver precedence.

- *Parameters*: `PackageVersion? other`.
- *Returns*: `int` — negative/zero/positive per `IComparable<T>` convention.
- *Postconditions*: Compares major, minor, patch, then revision numerically; for equal numeric
  components, a release (`Prerelease is null`) outranks any prerelease of the same numeric
  version; two prereleases compare identifier by identifier (split on `.`): numeric identifiers
  compare numerically (as arbitrary-length digit strings, so there is no overflow) and rank
  below alphanumeric identifiers, alphanumeric identifiers compare in ASCII order ignoring letter case, and when all
  shared identifiers are equal the longer list ranks higher. Metadata is never consulted
  (`AgentControl-PackageVersion-Compare`, `AgentControl-PackageVersion-MetadataIgnoredInComparison`).
  Prerelease labels compare without regard to letter case, as NuGet does, so `1.0.0-beta` and
  `1.0.0-BETA` are equal and the "latest" chosen for a NuGet source matches NuGet's own choice.

**Equals / GetHashCode / ToString**: `Equals` delegates to `CompareTo(other) == 0`;
`GetHashCode` combines the numeric components and the case-insensitive canonical prerelease so versions that
compare equal hash equally; `ToString` returns the original text, which round-trips through
`TryParse` (`AgentControl-PackageVersion-Compare`).

Comparison operators (`<`, `<=`, `>`, `>=`, `==`, `!=`) are implemented in terms of
`CompareTo`/`Equals` for natural use in `OrderByDescending` and direct comparisons elsewhere
in the codebase.

#### Error Handling

The constructor throws `ArgumentOutOfRangeException` for a negative major, minor, patch, or
revision component. `TryParse` and `TryParseStrict` never throw; they return `false` for any
malformed input. The `<`/`<=`/`>`/`>=` operators throw `ArgumentNullException` when their
left-hand operand is `null` (the type does not support ordering relative to a null left side,
unlike `Equals`, which treats two nulls as equal).

#### Dependencies

- **.NET BCL** — `int.TryParse` (invariant culture, `NumberStyles.None`), `string.Compare` (ordinal, ignoring case).

The unit tests additionally compare its ordering with `NuGet.Versioning` (a transitive
dependency of the NuGet caching OTS item) to prove both agree on which version is newest.

#### Callers

- **PackageSource** — parses each zip file's version substring with `TryParseStrict` and orders
  discovered packages; parses a pinned version with `TryParse`.
- **NuGetSourceProvider** — parses the versions the NuGet library reports with `TryParse`.
- **PackageVersionCache** — parses a repo's pinned version string with `TryParse` when checking
  for an upgrade.
- **RepoCardViewModel** — matches a pinned or selected version text against discovered versions.
