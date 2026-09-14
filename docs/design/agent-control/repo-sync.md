## RepoSync

![RepoSync Structure](RepoSyncView.svg)

### Overview

The `RepoSync` subsystem spans `PackageZipExtractor.cs` (extracts a package zip's managed
folders into a repo, blind delete-replace), `ReleaseNotesViewerViewModel.cs` (backs the
release-notes dialog shown after a sync), and `GitIgnoreEnsurer.cs` (proactively ensures a
repo's `.gitignore` covers the four managed folders immediately after a successful sync); the
`ReleaseNotesViewer.axaml` view has no dedicated test file and is folded into this
subsystem-level description. The subsystem provides the observable behavior of syncing a
repo's agent files to a package's contents. It contains three units: `PackageZipExtractor`,
`ReleaseNotesViewerViewModel`, and `GitIgnoreEnsurer`.

### Interfaces

**PackageZipExtractor.Extract**: Replaces a repo's managed agent-file folders with a package
zip's contents.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Opens/validates the zip, deletes the four known managed folders
  (`.github/agents`, `.github/standards`, `.github/templates`, `.github/skills`) if present,
  and extracts the new files excluding root-level files such as `release-notes.md`
  (`AgentControl-PackageZipExtractor-Extract`).
- *Constraints*: Throws `InvalidOperationException` for a file that is not a valid zip
  archive; per architecture.md's "blind delete of the four known folders" decision, any local
  customizations a developer added inside those folders are silently removed — an accepted
  risk, not a defect (see architecture.md's Open Concerns #3).

**PackageZipExtractor.AllManagedFoldersExist**: Reports whether all four managed folders are
present on disk.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Returns `false` if any of the four folders is missing
  (`AgentControl-PackageZipExtractor-AllManagedFoldersExist`); used by
  `RepoCardViewModel`'s ensure-synced-before-launch check to decide whether a re-extraction is
  needed without re-scanning file contents.
- *Constraints*: Performs only existence checks, not content verification.

**PackageZipExtractor.ReadReleaseNotes**: Reads a package's release notes without extracting.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Returns the `release-notes.md` entry's content if present, `null` otherwise
  (`AgentControl-PackageZipExtractor-ReadReleaseNotes`).
- *Constraints*: Does not modify the repo or the zip.

**ReleaseNotesViewerViewModel**'s display properties (`RepoName`, `Title`, `ReleaseNotes`).

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

`PackageZipExtractor` implements the upgrade/sync sequence from architecture.md's
"Upgrade sequence and failure handling": (1) open/validate the new zip — if it opens without
error, its contents are assumed good; (2) delete the four old folders if present; (3) extract
the new files, excluding root-level files like `release-notes.md`. It is a static class with
no persistent state; the pin-file rewrite and release-notes display happen in the calling
`RepoCardViewModel`, deliberately *after* a successful extraction, so a failure partway
through extraction never leaves a repo pinned to a version whose files were not actually
applied (`AgentControl-RepoSync-Sync`).

`ReleaseNotesViewerViewModel` is a simple, stateless-beyond-construction display view model:
it is constructed once per shown dialog with the repo name, package title, and release notes
text, and exposes them (with a placeholder substituted for an empty release-notes string) for
`ReleaseNotesViewer`'s bindings. `ReleaseNotesViewer` itself is shown via `Window.Show()`,
never `ShowDialog`, so the main window remains usable while the developer reads release notes
— a non-modal presentation is an explicit design requirement, not merely a style preference,
since a modal dialog would block launching the agent tool while release notes are open
(`AgentControl-RepoSync-ReleaseNotes`).

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
