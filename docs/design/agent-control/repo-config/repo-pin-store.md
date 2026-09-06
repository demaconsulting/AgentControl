### RepoPinStore

![RepoConfig Structure](RepoConfigView.svg)

#### Purpose

`RepoPinStore` reads and writes the per-repo `.agentcontrol.json` pin file at a repo's root,
recording which package name/version is currently synced into that repo. It is the sole
reader/writer of the pin file; `PackageZipExtractor` never touches it directly, keeping file
extraction and pin bookkeeping as separate concerns per architecture.md.

#### Data Model

`RepoPinStore` holds no instance state; it is a static class.

**PinFileName**: `const string` — `.agentcontrol.json`, always located directly at the repo
root.

**JsonOptions**: `static readonly JsonSerializerOptions` — shared `WriteIndented = true`
options used by both `Load` and `Save`, so the on-disk file is human-readable.

#### Key Methods

**Load**: Loads the `RepoPin` for a repo, or `null` if it has no pin file yet.

- *Parameters*: `string repoRoot`.
- *Returns*: `RepoPin?`.
- *Postconditions*: Returns `null` (not an exception) when no pin file exists yet — a
  never-synced repo is an expected, ordinary state (`AgentControl-RepoPinStore-Load`).

**Save**: Writes the `RepoPin` for a repo, creating or overwriting `.agentcontrol.json`.

- *Parameters*: `string repoRoot`, `RepoPin pin`.
- *Returns*: `void`.
- *Preconditions*: `repoRoot` must already exist.
- *Postconditions*: The pin file at the repo root reflects `pin` exactly
  (`AgentControl-RepoPinStore-Save`).

#### Error Handling

`Load` and `Save` throw `ArgumentNullException` for a null `repoRoot` (and `Save` also for a
null `pin`). `Load` throws `InvalidOperationException` when the pin file exists but cannot be
read or contains invalid JSON (wrapping `IOException`/`UnauthorizedAccessException`/
`JsonException`). `Save` throws `InvalidOperationException` when the file cannot be written
(wrapping `IOException`/`UnauthorizedAccessException`).

#### Dependencies

- **RepoPin** (record, same subsystem) — the data type read/written.
- **PathHelpers** (`Utilities` subsystem) — `SafePathCombine` resolves the pin file path so a
  malformed repo root cannot escape the intended directory.
- **.NET BCL** — `System.Text.Json`.

#### Callers

- **RepoCardViewModel** — calls `Load` during `RefreshPin` and `Save` inside
  `ApplyPackageAndShowReleaseNotes` after a successful extraction.
