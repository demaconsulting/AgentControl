### PackageZipExtractor

![RepoSync Structure](RepoSyncView.svg)

#### Purpose

`PackageZipExtractor` implements the blind-delete-and-replace upgrade sequence for a repo's
four managed agent folders (`.github/agents`, `.github/standards`, `.github/templates`,
`.github/skills`) from a package zip file. Per architecture.md's "Blind delete of the four
known folders" decision (see `docs/design/agent-control/repo-sync.md`), it performs: (1) open/
validate the zip; (2) delete the four known folders if present; (3) extract only those same
four folders from the zip — root-level files such as `release-notes.md` are never extracted to
disk. Rewriting the `.agentcontrol.json` pin is the caller's responsibility (the `RepoConfig`
subsystem), so this class stays focused on file operations alone.

#### Data Model

`PackageZipExtractor` holds no instance state; it is a static class.

**ManagedFolders**: `static readonly string[]` — the four folder paths (relative to a repo
root) that are blind-deleted and replaced on every sync/upgrade; the single source of truth
consulted by both `Extract` and `AllManagedFoldersExist`.

#### Key Methods

**Extract**: Opens/validates the zip, deletes the four managed folders if present, and
extracts the same four folders from the zip into the repo.

- *Parameters*: `string zipPath`, `string repoRoot`.
- *Returns*: `void`.
- *Preconditions*: Neither parameter `null`.
- *Postconditions*: The four managed folders under `repoRoot` match the zip's contents exactly
  (`AgentControl-PackageZipExtractor-Extract`). There is intentionally no rollback on failure —
  a partially-applied change is possible and must be resolved manually by the caller, per
  architecture.md's "Upgrade sequence and failure handling" decision.

**AllManagedFoldersExist**: Determines whether all four managed folders currently exist under
a repo root.

- *Parameters*: `string repoRoot`.
- *Returns*: `bool`.
- *Postconditions*: Does not inspect folder contents — a managed folder that exists but is
  empty (or only partially populated) still counts as "existing"
  (`AgentControl-PackageZipExtractor-AllManagedFoldersExist`). A managed folder reached through
  a reparse-point (symlink/junction) repo root, ancestor, or the folder itself is treated as
  **not** existing, using `PathHelpers.FindReparsePointInAncestry` (the same filesystem-aware
  primitive `Extract`'s own symlink guard is built on) — this prevents a caller from trusting
  content reached through a link and skipping `Extract`'s own reparse-point protections
  entirely. Consulted by `RepoCardViewModel.EnsureAgentFilesSyncedBeforeLaunch` to decide
  whether a silent re-extraction is needed before launch.

**ReadReleaseNotes**: Reads the content of the package zip's root-level `release-notes.md`
entry without extracting it to disk.

- *Parameters*: `string zipPath`.
- *Returns*: `string?` — the release notes text, or `null` if the zip has no such entry
  (`AgentControl-PackageZipExtractor-ReadReleaseNotes`).

#### Error Handling

`Extract` throws `ArgumentNullException` for a null `zipPath`/`repoRoot`, and
`InvalidOperationException` when the zip cannot be opened/is not a valid archive, a managed
folder cannot be deleted, extraction fails partway through, or a zip entry would resolve outside
`repoRoot` — wrapping the underlying `IOException`/`UnauthorizedAccessException`/
`InvalidDataException` with a message naming the zip path and repo root. When the repo root, an
ancestor, a managed folder, or any of its descendants is a symlink/junction, `Extract` instead
throws `UnsafeRepositoryStateException` (a dedicated `InvalidOperationException` subtype) —
this distinguishes a genuine security concern (content reached through an unexpected link) from
an ordinary extraction failure, letting callers that need to react differently (e.g.
`RepoCardViewModel.Launch`, which otherwise never lets sync state block a launch) catch it
specifically, while callers that only care about "did this fail" can still catch the base
`InvalidOperationException` type unchanged. The symlink checks
(`PathHelpers.FindReparsePointInAncestry` via the `EnsureNoSymlinkAncestors` wrapper) walk from
the affected path up to *and including* the repo root itself (not just its ancestors) before
both the blind-delete step and each entry's extraction, using `File.GetAttributes` rather than
`Directory.Exists` so a *dangling* symlink/junction (whose target does not currently exist) is
still detected. The blind-delete step additionally uses `DeleteDirectoryRejectingReparsePoints`,
which preflights the entire managed-folder tree with `PathHelpers.FindReparsePointInDescendants`
*before* deleting anything, then performs a plain recursive delete — this two-phase split avoids
a partial delete: interleaving the reparse-point check with the delete itself would still let an
ordinary sibling file be permanently removed before a reparse point discovered later in the same
tree aborts the operation. `ReadReleaseNotes` throws the same `InvalidOperationException` pattern
for a zip that cannot be opened or whose release-notes entry cannot be read.

#### Dependencies

- **PathHelpers** (`Utilities` subsystem) — `SafePathCombine` resolves every managed-folder
  and zip-entry destination path safely relative to the repo root; `FindReparsePointInAncestry`
  and `FindReparsePointInDescendants` detect reparse points that a lexical path check alone
  cannot see, underpinning `EnsureNoSymlinkAncestors` and `DeleteDirectoryRejectingReparsePoints`
  respectively.
- **UnsafeRepositoryStateException** (`RepoSync` subsystem) — the dedicated exception type
  thrown when a reparse point is detected, letting callers distinguish this security concern
  from ordinary `InvalidOperationException` failures.
- **.NET BCL** — `System.IO.Compression.ZipFile`/`ZipArchive`.

#### Callers

- **RepoCardViewModel** — calls `Extract` (via `ApplyPackageAndShowReleaseNotes`) after a
  Select-Package or Upgrade action, calls `AllManagedFoldersExist` and, on a miss, `Extract`
  directly during `EnsureAgentFilesSyncedBeforeLaunch`, and calls `ReadReleaseNotes` to obtain
  the text shown via `ReleaseNotesViewerViewModel`. `Launch` catches
  `UnsafeRepositoryStateException` specifically (propagated up through
  `EnsureAgentFilesSyncedBeforeLaunch`) to abort the launch entirely, rather than treating it as
  a best-effort sync failure.
