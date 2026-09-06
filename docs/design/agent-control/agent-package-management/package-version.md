### PackageVersion

![AgentPackageManagement Structure](AgentPackageManagementView.svg)

#### Purpose

`PackageVersion` parses a version string (release or prerelease) into comparable components
and implements ordering so releases and prereleases compare correctly against each other. It
implements the subset of Semantic Versioning 2.0.0 needed for AgentControl: numeric
major/minor/patch plus an optional dot-separated prerelease identifier (build metadata after
`+` is intentionally unsupported, since package file names never carry it).

#### Data Model

**Major**, **Minor**, **Patch**: `int` — non-negative numeric version components.

**Prerelease**: `string?` — the text after the first `-`, or `null` for a release version.

The class is immutable and thread-safe once constructed; all properties are read-only.

#### Key Methods

**Constructor**: Builds a version from explicit components.

- *Parameters*: `int major`, `int minor`, `int patch`, `string? prerelease = null`.
- *Preconditions*: All numeric components non-negative.
- *Postconditions*: An empty `prerelease` string is normalized to `null`
  (`AgentControl-PackageVersion-Parse`).

**TryParse**: Attempts to parse a semantic version string.

- *Parameters*: `string? value`, `out PackageVersion? version`.
- *Returns*: `bool`.
- *Postconditions*: On success, `version` holds the parsed components. Does not throw on
  malformed input — callers enumerating a directory of arbitrary file names are expected to
  skip entries that fail to parse (`AgentControl-PackageVersion-Parse`). Rejects a numeric
  part that is not exactly three dot-separated non-negative integers, and rejects an empty
  prerelease identifier (a trailing `-` with nothing after it).

**CompareTo**: Compares this version to another following semver precedence.

- *Parameters*: `PackageVersion? other`.
- *Returns*: `int` — negative/zero/positive per `IComparable<T>` convention.
- *Postconditions*: Compares major, minor, then patch numerically; for equal numeric
  components, a release (`Prerelease is null`) outranks any prerelease of the same numeric
  version; two prereleases compare via ordinal string comparison
  (`AgentControl-PackageVersion-Compare`).

**Equals / ToString**: `Equals` delegates to `CompareTo(other) == 0`; `ToString` round-trips
through the same format `TryParse` accepts (`major.minor.patch[-prerelease]`)
(`AgentControl-PackageVersion-Compare`).

Comparison operators (`<`, `<=`, `>`, `>=`, `==`, `!=`) are implemented in terms of
`CompareTo`/`Equals` for natural use in `OrderByDescending` and direct comparisons elsewhere
in the codebase.

#### Error Handling

The constructor throws `ArgumentOutOfRangeException` for a negative major, minor, or patch
component. `TryParse` never throws; it returns `false` for any malformed input. The `<`/`<=`/
`>`/`>=` operators throw `ArgumentNullException` when their left-hand operand is `null` (the
type does not support ordering relative to a null left side, unlike `Equals`, which treats two
nulls as equal).

#### Dependencies

- **.NET BCL** — `int.TryParse` (invariant culture, `NumberStyles.None`), `string.CompareOrdinal`.

#### Callers

- **PackageSource** — parses each zip file's version substring and orders discovered packages.
- **PackageVersionCache** — parses a repo's pinned version string when checking for an
  upgrade.
