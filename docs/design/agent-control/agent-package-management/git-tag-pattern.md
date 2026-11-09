### GitTagPattern

![AgentPackageManagement Structure](AgentPackageManagementView.svg)

#### Purpose

`GitTagPattern` turns the tags of a Git repository into package versions: it validates the pattern a user writes (for
example `v{version}` or `release/{version}`), decides whether a tag name is a version under it, builds a tag name from a
version, and derives the glob that narrows the remote listing (`AgentControl-AgentPackageManagement-GitSource`). The
matching rules are pure and security-relevant (a server chooses the tag names), so they are a unit of their own.

#### Data Model

**GitTagPattern**: `internal sealed class`, immutable and thread-safe, created only by `TryCreate`.

| Member | Type | Meaning and invariant |
| ------ | ---- | --------------------- |
| `Placeholder` | `const string` | `{version}`, the only accepted spelling |
| `Text` | `string` | the validated pattern text, at most 200 characters |
| `ListingGlob` | `string` | `refs/tags/` + `prefix` + `*`; only narrows the remote listing |
| `prefix` | `string` (private) | the literal text before the placeholder |
| `suffix` | `string` (private) | the literal text after the placeholder |

Invariants of every instance:

- The placeholder is exactly `{version}` (lower case) and occurs exactly once; no other braces
  (`AgentControl-GitTagPattern-RequireSinglePlaceholder`).
- The literal parts use only `A-Z a-z 0-9 . _ / + -` and a sample tag built from the pattern (`1.0.0` for the
  placeholder) is a legal tag name: no empty, dot-leading or `.lock`-ending path component, no `..`, not ending in
  `.`, not starting with `-` (`AgentControl-GitTagPattern-RejectInvalidRefNames`). The pattern itself is at most 200
  characters; the tag built from it and a version may be longer, up to the 255-character tag-name limit.
- A tag matches when it starts with the prefix and ends with the suffix (ordinal, so case-sensitive) and the text
  between is a strict Semantic Versioning 2.0 version: exactly three numeric parts without leading zeros (`0` alone
  is fine), then an optional `-` prerelease of dot-separated, non-empty identifiers of letters, digits and hyphens in
  which a numeric identifier has no leading zero. Build metadata (`+...`), a fourth part and leading zeros
  (`v01.2.3`, `v1.2.3-01`) do not match, so `v01.2.3` and `v1.2.3` can never be two spellings of one version. This is
  stricter than `PackageVersion.TryParseStrict`, which keeps the looser grammar the zip file-name splitting has always
  used; the text must also pass `TryParseStrict` to give the `PackageVersion`
  (`AgentControl-GitTagPattern-ExtractStrictVersion`).
- `ListingGlob` is `refs/tags/` + prefix + `*` (`AgentControl-GitTagPattern-ListingPrefix`).

#### Key Methods

**TryCreate** (static): Validates pattern text and creates the pattern.

- *Parameters*: `string? text` - the pattern as typed or read from settings; `out GitTagPattern? pattern`;
  `out string? problem`.
- *Returns*: `bool` - `true` when the pattern is acceptable.
- *Postconditions*: On `true`, `pattern` is set and `problem` is `null`; on `false`, `pattern` is `null` and `problem`
  is a message the user can act on that never repeats `text`. Never throws, including for `null` or very long text
  (`AgentControl-GitTagPattern-RequireSinglePlaceholder`, `AgentControl-GitTagPattern-RejectInvalidRefNames`).

**GetProblem** (static): `(string? text)` returns `string?` - `null` when the pattern is acceptable, otherwise the same
message `TryCreate` gives.

**TryMatch**: Decides whether a tag is a package version under the pattern.

- *Parameters*: `string? tagName` - the tag without `refs/tags/`, as a server reported it;
  `out PackageVersion? version`.
- *Returns*: `bool`.
- *Postconditions*: `true` only when the name is a safe tag name (`IsSafeTagName`), starts with the prefix, ends with
  the suffix and has a strict Semantic Versioning 2.0 version between them; then `version` is set. Otherwise `false`
  and `version` is `null`. Never throws (`AgentControl-GitTagPattern-ExtractStrictVersion`).

**Format**: Builds the tag that stands for a version.

- *Parameters*: `PackageVersion version` - must not be `null`.
- *Returns*: `string?` - the prefix, the version's text and the suffix (`AgentControl-GitTagPattern-FormatTag`), or
  `null` when that name would not be a safe tag name (for example a prerelease so long that the tag exceeds 255
  characters) or `TryMatch` would not read it back as an equal version (build metadata, a fourth part or a leading zero
  in a version parsed by the looser grammars). A `null` result means the version has no tag under this pattern; the
  caller must not invent one (`AgentControl-GitTagPattern-FormatRefusesUnsafeTag`).
- *Preconditions*: `version` is not `null`; otherwise `ArgumentNullException`.

**IsSafeTagName** (static): Vets a tag name a server reports before it can reach a refspec.

- *Parameters*: `string? tagName`.
- *Returns*: `bool` - `true` only for `A-Z a-z 0-9 . _ / + -`, at most 255 characters, a legal ref name, not starting
  with `-` (`AgentControl-GitTagPattern-VetServerTagNames`). `TryMatch` applies it first. The rule itself is
  implemented once, in `GitRemoteClient.IsSafeTagName` (`GitIntegration`), and this method delegates to it, so the
  client applies exactly the same check to what a server reports and to the tag it is asked to fetch; the rule lives in
  the lower subsystem because `GitIntegration` must not depend on `AgentPackageManagement`.

#### Error Handling

Nothing throws for invalid text (`false` and a message, or `null` from `Format` for a version without a tag); `Format`
throws `ArgumentNullException` for a null version. No message repeats the pattern text or a tag name.

#### Dependencies

- **PackageVersion** (same subsystem) - strict version parsing and the version's text.
- **GitRemoteClient** (GitIntegration) - `IsSafeTagName`, the one implementation of the tag-name rule.

#### Callers

- **PackageSourceDefinition** - `GetGitSettingsProblem`.
- **GitSourceProvider** - listing glob, matching of listed tags, vetting of server tag names, check of a package's tag.
- **SourceEditorViewModel** (`LauncherUI`) - `GetProblem` validates the tag pattern the user types before a Git
  source can be tested or saved, and its tag preview shows which listed tags match the pattern (the matching is
  `GitSourceProvider.PreviewAsync`, which applies `TryMatch`).
- **RepoCardViewModel** (`LauncherUI`) - `TryCreate` and `Format` name the tag of a pinned version from the source's
  tag pattern as configured now (no tag when the version cannot be written as a safe tag name).
