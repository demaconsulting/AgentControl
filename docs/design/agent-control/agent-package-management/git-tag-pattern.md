### GitTagPattern

![AgentPackageManagement Structure](AgentPackageManagementView.svg)

#### Purpose

`GitTagPattern` turns the tags of a Git repository into package versions: it validates the pattern a user writes (for
example `v{version}` or `release/{version}`), decides whether a tag name is a version under it, builds a tag name from a
version, and derives the glob that narrows the remote listing (`AgentControl-AgentPackageManagement-GitSource`). The
matching rules are pure and security-relevant (a server chooses the tag names), so they are a unit of their own.

#### Data Model

**GitTagPattern**: immutable, created only by `TryCreate` - `Text` and `ListingGlob`; holds the literal text before
(`prefix`) and after (`suffix`) the placeholder.

Rules:

- The placeholder is exactly `{version}` (lower case) and occurs exactly once; no other braces
  (`AgentControl-GitTagPattern-RequireSinglePlaceholder`).
- The literal parts use only `A-Z a-z 0-9 . _ / + -` and a sample tag built from the pattern (`1.0.0` for the
  placeholder) must be a legal tag name: no empty, dot-leading or `.lock`-ending path component, no `..`, not ending in
  `.`, not starting with `-`, at most 200 characters (`AgentControl-GitTagPattern-RejectInvalidRefNames`).
- A tag matches when it starts with the prefix and ends with the suffix (ordinal, so case-sensitive) and the text
  between is a strict semantic version (`PackageVersion.TryParseStrict`: three numeric parts and an optional
  prerelease; `+metadata` and four parts do not match) (`AgentControl-GitTagPattern-ExtractStrictVersion`).
- `ListingGlob` is `refs/tags/` + prefix + `*` (`AgentControl-GitTagPattern-ListingPrefix`).

#### Key Methods

**TryCreate / GetProblem**: Validate a pattern; the message never repeats the pattern.

**TryMatch**: `(string? tagName, out PackageVersion? version)` - `true` when the name is a safe tag name, has the prefix
and suffix and a strict version between.

**Format**: `(PackageVersion)` - prefix + the version's text + suffix (`AgentControl-GitTagPattern-FormatTag`).

**IsSafeTagName** (static): Vets a tag name a server reports before it can reach a refspec: only
`A-Z a-z 0-9 . _ / + -`, at most 255 characters, a legal ref name, not starting with `-`
(`AgentControl-GitTagPattern-VetServerTagNames`). `TryMatch` applies it first.

#### Error Handling

Nothing throws for invalid text (`false` and a message); `Format` throws `ArgumentNullException` for a null version.

#### Dependencies

- **PackageVersion** (same subsystem) - strict version parsing and the version's text.
- **.NET BCL** - generated, `\z`-anchored regular expression.

#### Callers

- **PackageSourceDefinition** - `GetGitSettingsProblem`.
- **GitSourceProvider** - listing glob, matching of listed tags, vetting of server tag names, check of a package's tag.
