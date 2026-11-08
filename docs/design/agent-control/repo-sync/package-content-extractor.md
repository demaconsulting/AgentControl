### PackageContentExtractor

![RepoSync Structure](RepoSyncView.svg)

#### Purpose

`PackageContentExtractor` implements the blind-delete-and-replace upgrade sequence for a repo's
four managed agent folders (`.github/agents`, `.github/standards`, `.github/templates`,
`.github/skills`) from a package's content, whatever the package format. Packages are presented
through `IPackageContentReader`, with two readers in this unit: `ZipPackageContentReader` (a zip
file, content root = the zip's root) and `DirectoryPackageContentReader` (an unpacked folder such
as a NuGet package in a package cache, content root = the folder's `content` sub-folder). One
extractor and one managed-folder list serve both, so a repo ends up in the same state however the
package arrived (`AgentControl-RepoSync-SyncFromFolderPackage`). `PackageZipExtractor` remains as
the zip-file facade over it.

#### Data Model

`PackageContentExtractor` holds no instance state; it is a static class.

**ManagedFolders**: `static readonly string[]` — the four folder paths (relative to a repo root)
that are blind-deleted and replaced on every sync/upgrade; the single source of truth consulted by
both `Extract` and `AllManagedFoldersExist`.

**IPackageContentReader**: a read-only view of package content — `Description` (a path for
messages), `ListFiles()` (relative paths with `/` separators), `OpenRead(path)` and
`ReadText(path)` (null when absent). Implementations never write to the package.

**ZipPackageContentReader**: holds a `ZipArchive` open until disposed; `Open` wraps open
failures - an unreadable or invalid archive, and a path that is invalid or unsupported
(`ArgumentException`, `NotSupportedException`, `PathTooLongException`, `SecurityException`) - in
`InvalidOperationException` naming the zip, so no path exception escapes to a caller that handles one failure type
(`AgentControl-PackageContentExtractor-ZipOpenFailure`).

**DirectoryPackageContentReader**: holds only paths. `Open` requires a `content` sub-folder
(otherwise `InvalidOperationException` — a wrong-layout package must never make the extractor
delete a repo's managed folders for nothing; `AgentControl-PackageContentExtractor-RejectMissingContentFolder`) and
rejects a package folder or a `content` folder
that is itself a link (`AgentControl-PackageContentExtractor-RejectLinkedPackageFolder`). `ListFiles` walks the
content tree and rejects any file or directory that is a symbolic link,
junction or other reparse point (`FileAttributes.ReparsePoint` or a link target;
`AgentControl-PackageContentExtractor-RejectLinkedPackageDirectory`). `OpenRead` and
`ReadText` resolve each path with `PathHelpers.SafePathCombine` against the content root (a path that leaves it is
refused: `AgentControl-PackageContentExtractor-RejectPathOutsideContent`) and
refuse a path when the file, or any directory between the content root and the file, is a link, so
a linked parent directory holding an ordinary file cannot redirect a read
(`AgentControl-PackageContentExtractor-RejectLinkedPackageFile`). The link check inspects the entry's own
attributes before it decides the entry is absent, so a **broken** link - one whose target does not exist - at a
requested root-level file such as `AGENTS.md` is rejected with `InvalidOperationException` like any other link,
rather than being treated as a missing file for which `ReadText` returns `null`
(`AgentControl-PackageContentExtractor-RejectBrokenLink`).

#### Key Methods

**Extract**: Validates every file path in the package, deletes the four managed folders under the
repo if present, and copies the same four folders from the package into the repo.

- *Parameters*: `IPackageContentReader reader`, `string repoRoot`, `ILogger? logger = null`
  (defaults to a fresh `AppLogging.Factory`-resolved logger per call rather than a cached static
  field, so logging configured after this class is first touched is never missed).
- *Postconditions*: The four managed folders under `repoRoot` match the package's managed
  folders exactly (`AgentControl-PackageContentExtractor-ExtractFromFolder`,
  `-ReplaceManagedFolders`, `-PackageNeverModified`, each prefixed `AgentControl-PackageContentExtractor`). Root-level files
  such as `release-notes.md` and `AGENTS.md` and any file outside the managed folders are not
  copied (`AgentControl-PackageContentExtractor-NoRootFilesCopied`). Every file's destination is resolved with
  `PathHelpers.SafePathCombine` and its
  membership in a managed folder decided on the canonical (`..`-resolved) relative path *before*
  anything is deleted, so a path that escapes the repo (or a link inside a folder package, which
  `ListFiles` rejects) fails the whole call with the repo untouched
  (`AgentControl-PackageContentExtractor-RejectLinksAndTraversal`). Containment is also checked
  against the repo's real layout, still before anything is deleted:   `FindUnsafeComponent` walks
  every existing folder from just below the repo root down to each managed folder and to each
  file's destination folder, and a symbolic link, junction or other reparse point
  (`FileAttributes.ReparsePoint` or a link target), a component that exists but is not a folder
  (`AgentControl-PackageContentExtractor-RefuseNonFolderRepoComponent`) — or a component whose type cannot be
  determined — refuses the whole extraction with a clear error, so a link can never redirect the
  delete or copy outside the repo and a refused extraction changes nothing
  (`AgentControl-PackageContentExtractor-RefuseLinkedRepoFolders`; the requirement covers a link in the
  `.github` folder, a managed folder or the destination chain as one criterion - a linked folder on the way to
  anything that would be deleted or written). The repo root itself and the
  components above it are never inspected, because a root may legitimately be reached through a
  link such as a redirected user profile; only components at or below the root must be link-free
  (`AgentControl-PackageContentExtractor-AcceptRepoRootThroughLink`).
  `CopyFile` repeats the check on its destination chain just before creating directories, as
  defense in depth, and checks the destination file itself for a link or reparse point
  immediately before opening it (`AgentControl-PackageContentExtractor-RefuseLinkedDestinationFile`). A delete or copy I/O failure can
  still leave the repo partially upgraded — there is intentionally no rollback, per
  architecture.md's "Upgrade sequence and failure handling" decision. Nothing is ever written to
  the package.

**AllManagedFoldersExist**: Reports whether all four managed folders exist under a repo root as
ordinary folders, without inspecting contents. A managed folder that is a link, or sits below a
linked `.github`, is not valid, so a caller that skips syncing when the folders exist falls
through to `Extract`, which refuses the link with a clear error
(`AgentControl-PackageContentExtractor-ManagedFoldersExist`).

**ReadReleaseNotes / ReadAgentsMdTemplate**: Return the package's root-level `release-notes.md`
or `AGENTS.md` text, or `null` when absent, without copying anything to disk
(`AgentControl-PackageContentExtractor-ReadReleaseNotes`,
`AgentControl-PackageContentExtractor-ReadAgentsMdTemplate`).

#### Error Handling

`ArgumentNullException` for null arguments; `InvalidOperationException` for a package that cannot
be read, a path that is invalid or resolves outside the repo, a link in a folder package, a link in
the repo's `.github`/managed-folder/destination chain, a missing
`content` folder, a managed folder that cannot be deleted, or a copy that fails partway through
(wrapping `IOException`/`UnauthorizedAccessException`/`ArgumentException`/`NotSupportedException`
with a message naming the package or file). Delete and copy failures are wrapped and not
separately logged here: the caller (`RepoCardViewModel`) already logs the exception with repo and
package context. A zip that cannot be opened is also reported as `InvalidOperationException`, including when its
path is invalid or unsupported.

**Residual risk - check-then-use on links (accepted).** Link validation is check-then-use, not an atomic
no-follow operation: `DirectoryPackageContentReader` validates each component of a package path
(the package folder, the content folder, each directory and the file)
before it opens the file, and `PackageContentExtractor` checks the repo's folder chain before deleting and
re-checks the destination directory chain immediately before each `CreateDirectory` and file creation. A process
that replaces a checked component with a link in the gap between the check and the use could therefore make a
read or write follow the link outside the validated boundary (a time-of-check-to-time-of-use race). The risk is
**accepted and not eliminated in code**, for these reasons:

- Exploiting it needs a concurrent process running as the same user that can already write anywhere that user
  can write, so a successful race gains the attacker nothing they could not do directly.
- Mitigations in place: every path component is checked before use; the destination directory chain is checked
  again immediately before each directory or file is created, which narrows the window to the interval between
  that last check and the operating-system call.
- The supported concurrency model is that the repo working tree and the package folder are **not modified by
  another process while an extraction runs**; the application does not defend against a hostile concurrent writer
  within that boundary.
- A stronger no-follow or handle-based open (for example opening each component relative to the previous
  directory handle without following links) is not portable across Windows, Linux and macOS in .NET, so it is
  not used.

This residual risk is repeated in the `RepoSync` subsystem design and is recorded as a verification limitation
(it cannot be tested deterministically).

#### Dependencies

- **PathHelpers** (`Utilities` subsystem) — `SafePathCombine` for every destination and package
  file path.
- **AppLogging**, **ILogger** (`Logging` subsystem) — extraction/delete diagnostics under a string
  category name (a static class cannot be a generic type argument).
- **.NET BCL** — `System.IO.Compression`, `System.IO`.

#### Callers

- **PackageZipExtractor** — zip-file facade delegating to it.
- **RepoCardViewModel** — extracts a package acquired from a source provider, reads its release
  notes and `AGENTS.md` template.
- **ZipFolderSourceProvider**, **NuGetSourceProvider** (`AgentPackageManagement` subsystem) —
  return the readers it consumes.
