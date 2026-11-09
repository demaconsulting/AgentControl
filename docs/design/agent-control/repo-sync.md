## RepoSync

![RepoSync Structure](RepoSyncView.svg)

### Overview

The `RepoSync` subsystem spans `PackageContentExtractor.cs` (applies a package's managed
folders to a repo, blind delete-replace, from any package format presented through the content
readers `ZipPackageContentReader.cs` and `DirectoryPackageContentReader.cs` behind
`IPackageContentReader.cs`), `PackageZipExtractor.cs` (the zip-file entry point to it),
`ReleaseNotesViewerViewModel.cs` (backs the
release-notes dialog shown after a sync), and `GitIgnoreEnsurer.cs` (proactively ensures a
repo's `.gitignore` covers the four managed folders immediately after a successful sync); the
`ReleaseNotesViewer.axaml` view has no dedicated test file and is folded into this
subsystem-level description. The subsystem provides the observable behavior of syncing a
repo's agent files to a package's contents. It contains four units: `PackageContentExtractor`,
`PackageZipExtractor`, `ReleaseNotesViewerViewModel`, and `GitIgnoreEnsurer`; the content
reader types are folded into the `PackageContentExtractor` unit. Every reader also reports the content identity of
the package it reads (`AgentControl-RepoSync-ContentIdentity`, which supports
`AgentControl-System-PackageContentIntegrity`): a zip reader hashes the zip and reads its entries from the one open
handle, so the identity recorded in a pin is always that of the bytes that were extracted; a folder reader carries
the identity its source supplied. The subsystem only reports the identity - what a difference means is decided by
the `LauncherUI` card. A Git package is delivered as a checked-out folder whose root *is* the package layout, so
`DirectoryPackageContentReader.OpenLayoutRoot` reads such a folder: it lists and reads only the four managed folders and
the two root files, never `.git`, other `.github` content or any other file, and - unlike the `content`-folder mode,
which refuses a link anywhere - it neither inspects nor refuses a link that is not part of the layout while still
refusing one that is (`AgentControl-RepoSync-LayoutRoot`, which supports `AgentControl-System-GitSourceSafety`). The list
of layout paths has one source of truth, `PackageContentExtractor.LayoutPaths`, shared with the Git source provider.

### Interfaces

**PackageContentExtractor.Extract**: Replaces a repo's managed agent-file folders with the
contents of a package presented as an `IPackageContentReader` — a zip file or an unpacked
package folder (a NuGet package's `content` folder).

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: The same sequence as `PackageZipExtractor.Extract` below for either package
  format, rejecting - before deleting anything - any file whose destination falls outside the
  repo and, for a folder package, any symbolic link or junction
  (`AgentControl-PackageContentExtractor-ExtractFromFolder`,
  `AgentControl-PackageContentExtractor-RejectLinksAndTraversal`); also reads the package's
  root-level release notes and `AGENTS.md` template (`AgentControl-PackageContentExtractor-ReadReleaseNotes`,
  `AgentControl-PackageContentExtractor-ReadAgentsMdTemplate`). A broken link at a requested root-level file is
  rejected like any other link (`AgentControl-PackageContentExtractor-RejectBrokenLink`), and a zip that cannot be
  opened - including one given an invalid or unsupported path - is reported as `InvalidOperationException`
  (`AgentControl-PackageContentExtractor-ZipOpenFailure`).
  It also refuses - before deleting or writing anything - to work in a repo whose `.github`
  folder, a managed folder or a destination folder is a symbolic link or junction, since the
  delete and copy would follow it outside the repo; the repo root itself may be reached through a
  link (`AgentControl-PackageContentExtractor-RefuseLinkedRepoFolders`,
  `AgentControl-RepoSync-RefuseLinkedRepoFolders`).
- *Constraints*: Never writes to the package it reads (a NuGet package cache is shared).

**PackageZipExtractor.Extract**: Replaces a repo's managed agent-file folders with a package
zip's contents (a facade over `PackageContentExtractor` for a zip file).

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Opens/validates the zip, deletes the four known managed folders
  (`.github/agents`, `.github/standards`, `.github/templates`, `.github/skills`) if present,
  and extracts the new files excluding root-level files such as `release-notes.md` and
  `AGENTS.md` (`AgentControl-PackageZipExtractor-Extract`).
- *Constraints*: Throws `InvalidOperationException` for a file that is not a valid zip
  archive; per architecture.md's "blind delete of the four known folders" decision, any local
  customizations a developer added inside those folders are silently removed — an accepted
  risk, not a defect (see architecture.md's Open Concerns #3).

**PackageZipExtractor.AllManagedFoldersExist**: Reports whether all four managed folders are
present on disk.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Returns `false` if any of the four folders is missing or is a symbolic link or
  junction (`AgentControl-PackageZipExtractor-AllManagedFoldersExist`); used by
  `RepoCardViewModel`'s ensure-synced-before-launch check to decide whether a re-extraction is
  needed without re-scanning file contents.
- *Constraints*: Performs only existence checks, not content verification.

**PackageZipExtractor.ReadReleaseNotes**: Reads a package's release notes without extracting.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Returns the `release-notes.md` entry's content if present, `null` otherwise
  (`AgentControl-PackageZipExtractor-ReadReleaseNotes`).
- *Constraints*: Does not modify the repo or the zip.

**PackageZipExtractor.ReadAgentsMdTemplate**: Reads a package's optional root-level
`AGENTS.md` template without extracting, mirroring `ReadReleaseNotes`.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Returns the `AGENTS.md` entry's content if present, `null` otherwise
  (`AgentControl-PackageZipExtractor-ReadAgentsMdTemplate`); consulted by
  `RepoCardViewModel` after every successful `Extract` to decide whether to offer the
  template to the user.
- *Constraints*: Does not modify the repo or the zip.

**ReleaseNotesViewerViewModel**'s display properties (`Title`, `ReleaseNotes`).

- *Type*: In-process .NET properties, data-bound to `ReleaseNotesViewer`.
- *Role*: Provider.
- *Contract*: Substitutes a placeholder message when the package's release notes are empty
  (`AgentControl-ReleaseNotesViewerViewModel-Display`).
- *Constraints*: Rejects a null repo name or release notes value in its constructor.

**GitIgnoreEnsurer.Ensure**: Ensures a repo's root `.gitignore` covers the four managed
agent-file folders.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Appends a marker-delimited block covering the four managed folders (creating the
  file if absent) unless the fixed marker comment is already present, and never modifies or
  removes any pre-existing line (`AgentControl-GitIgnoreEnsurer-Ensure`).
- *Constraints*: Idempotency is a marker-comment scan only — never `git check-ignore`, never a
  gitignore-pattern/glob analysis, and no git dependency of any kind. Throws
  `InvalidOperationException` for an I/O failure.

### Design

`PackageContentExtractor` implements the upgrade/sync sequence for every package format, and
`PackageZipExtractor` is the thin zip-file facade over it, kept as the stable zip API
(`AgentControl-RepoSync-SyncFromFolderPackage`). The sequence is from architecture.md's "Upgrade
sequence and failure handling": (1) open/validate the new zip — if it opens without error, its
contents are assumed good; (2) delete the four old folders if present; (3) extract the new files,
excluding root-level files like `release-notes.md` and `AGENTS.md`. It is a static class with no
persistent state, and so is `PackageContentExtractor`; the pin-file rewrite, release-notes display,
and AGENTS.md template offer happen in the calling `RepoCardViewModel`, deliberately *after* a
successful extraction, so a failure partway through extraction never leaves a repo pinned to a
version whose files were not actually applied (`AgentControl-RepoSync-Sync`). `PackageZipExtractor`
also exposes `ReadAgentsMdTemplate`, reading a package's optional root-level `AGENTS.md` template
without extracting it, so `RepoCardViewModel` can decide whether to offer it to the user after a
successful sync (`AgentControl-RepoSync-AgentsMdTemplate`).

`ReleaseNotesViewerViewModel` is a simple, stateless-beyond-construction display view model:
it is constructed once per shown dialog with the repo name, package title, and release notes
text, and exposes them (with a placeholder substituted for an empty release-notes string) for
`ReleaseNotesViewer`'s bindings. `ReleaseNotesViewer` itself is shown via `Window.Show()`,
never `ShowDialog`, so the main window remains usable while the developer reads release notes
— a non-modal presentation is an explicit design requirement, not merely a style preference,
since a modal dialog would block launching the agent tool while release notes are open
(`AgentControl-RepoSync-ReleaseNotes`). `ReleaseNotesViewer`'s code-behind renders each parsed
`MarkdownBlock` into a `TextBlock`: heading blocks render every inline run bold (in addition to
their larger font size), regardless of whether individual runs were themselves marked
`**bold**`, so the heading's visual weight matches its larger size; non-heading (body) blocks
bold only the runs the Markdown source explicitly marked `**bold**`.

`GitIgnoreEnsurer` runs from `RepoCardViewModel` rather than from inside
`PackageZipExtractor.Extract` itself: `Extract` stays a pure file-sync operation with a single
responsibility (replacing the managed folders' contents), while ensuring `.gitignore` coverage
is a distinct post-extraction side effect — following the same architectural precedent as the
pin-file write, which also happens in the caller immediately after a successful extraction,
not inside `Extract`. This keeps `GitIgnoreEnsurer` independently testable and independently
skippable (its own non-blocking try/catch) without complicating `PackageZipExtractor`'s error
handling. Per architecture.md's "Committed agent files" badge description, AgentControl does
not modify git tracking state itself; `GitIgnoreEnsurer` is a deliberate, narrow exception
limited to `.gitignore` *content* (never git tracking state) and does not contradict that
stance (`AgentControl-RepoSync-GitIgnorePrevention`).

#### Residual risks

**Link validation is check-then-use (accepted).** Package and repo paths are validated against symbolic links and
junctions and then used in a separate step - the package reader checks every component of a path before it opens
the file, and the extractor checks the repo's folder chain before deleting and re-checks the destination directory
chain immediately before each directory or file is created - so a link planted in the gap between check and use
could redirect that one read or write outside the validated boundary (a time-of-check-to-time-of-use race). The risk
is accepted without a code change: exploiting it needs a concurrent process of the same user, which can already write
anywhere that user can write and so gains nothing new; every path component is checked before use and the
destination chain is re-checked immediately before each creation; the supported concurrency model is that the repo
working tree and the package folder are not modified by another process during extraction; and a stronger
no-follow or handle-based open is not portable across Windows, Linux and macOS in .NET. The detail is in the
`PackageContentExtractor` unit design and the limitation is recorded in its verification.
