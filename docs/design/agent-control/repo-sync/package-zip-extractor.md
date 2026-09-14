### PackageZipExtractor

![RepoSync Structure](RepoSyncView.svg)

#### Purpose

`PackageZipExtractor` implements the blind-delete-and-replace upgrade sequence for a repo's
four managed agent folders (`.github/agents`, `.github/standards`, `.github/templates`,
`.github/skills`) from a package zip file. Per architecture.md's "Blind delete of the four
known folders" decision (see `docs/design/agent-control/repo-sync.md`), it performs: (1) open/
validate the zip; (2) validate every entry's destination path up front, before anything is
deleted; (3) delete the four known folders if present; (4) extract only the already-validated
entries that fall within those same four folders — root-level files such as `release-notes.md`
are never extracted to disk. Validating every entry before the delete step means an invalid or
path-traversing entry rejects the whole upgrade without ever touching the existing managed
folders. Rewriting the `.agentcontrol.json` pin is the caller's responsibility (the
`RepoConfig` subsystem), so this class stays focused on file operations alone.

#### Data Model

`PackageZipExtractor` holds no instance state; it is a static class.

**ManagedFolders**: `static readonly string[]` — the four folder paths (relative to a repo
root) that are blind-deleted and replaced on every sync/upgrade; the single source of truth
consulted by both `Extract` and `AllManagedFoldersExist`.

#### Key Methods

**Extract**: Opens/validates the zip, validates every entry's destination path, deletes the
four managed folders if present, and extracts the already-validated entries into the repo.

- *Parameters*: `string zipPath`, `string repoRoot`.
- *Returns*: `void`.
- *Preconditions*: Neither parameter `null`.
- *Postconditions*: The four managed folders under `repoRoot` match the zip's contents exactly
  (`AgentControl-PackageZipExtractor-Extract`). An entry with an invalid destination path is
  rejected before any managed folder is deleted, so that failure mode never leaves the repo
  partially upgraded. A delete or extraction I/O failure can still do so — there is
  intentionally no rollback for that case, and it must be resolved manually by the caller, per
  architecture.md's "Upgrade sequence and failure handling" decision.

**AllManagedFoldersExist**: Determines whether all four managed folders currently exist under
a repo root.

- *Parameters*: `string repoRoot`.
- *Returns*: `bool`.
- *Postconditions*: Does not inspect folder contents — a managed folder that exists but is
  empty (or only partially populated) still counts as "existing"
  (`AgentControl-PackageZipExtractor-AllManagedFoldersExist`). Consulted by
  `RepoCardViewModel.EnsureAgentFilesSyncedBeforeLaunch` to decide whether a silent
  re-extraction is needed before launch.

**ReadReleaseNotes**: Reads the content of the package zip's root-level `release-notes.md`
entry without extracting it to disk.

- *Parameters*: `string zipPath`.
- *Returns*: `string?` — the release notes text, or `null` if the zip has no such entry
  (`AgentControl-PackageZipExtractor-ReadReleaseNotes`).

#### Error Handling

`Extract` throws `ArgumentNullException` for a null `zipPath`/`repoRoot`, and
`InvalidOperationException` when the zip cannot be opened/is not a valid archive, an entry's
destination path is invalid (including resolving outside `repoRoot`), a managed folder cannot
be deleted, or extraction fails partway through — wrapping the underlying
`IOException`/`UnauthorizedAccessException`/`InvalidDataException`/`ArgumentException`/
`NotSupportedException` with a message naming the zip path and repo root (or the offending
entry). `ReadReleaseNotes` throws the same `InvalidOperationException` pattern for a zip that
cannot be opened or whose release-notes entry cannot be read.

#### Dependencies

- **PathHelpers** (`Utilities` subsystem) — `SafePathCombine` resolves every managed-folder
  and zip-entry destination path safely relative to the repo root.
- **.NET BCL** — `System.IO.Compression.ZipFile`/`ZipArchive`.

#### Callers

- **RepoCardViewModel** — calls `Extract` (via `ApplyPackageAndShowReleaseNotes`) after a
  Select-Package or Upgrade action, calls `AllManagedFoldersExist` and, on a miss, `Extract`
  directly during `EnsureAgentFilesSyncedBeforeLaunch`, and calls `ReadReleaseNotes` to obtain
  the text shown via `ReleaseNotesViewerViewModel`.
